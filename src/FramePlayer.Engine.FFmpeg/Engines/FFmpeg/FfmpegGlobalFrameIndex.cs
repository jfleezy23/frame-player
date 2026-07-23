using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using FFmpeg.AutoGen;

namespace FramePlayer.Engines.FFmpeg
{
    internal unsafe sealed class FfmpegGlobalFrameIndex
    {
        private readonly List<FfmpegGlobalFrameIndexEntry> _entries;
        private readonly Dictionary<long, FfmpegGlobalFrameIndexEntry> _entriesByPresentationTimestamp;
        private readonly Dictionary<long, FfmpegGlobalFrameIndexEntry> _entriesByDecodeTimestamp;
        private readonly HashSet<long> _ambiguousPresentationTimestamps;
        private readonly HashSet<long> _ambiguousDecodeTimestamps;
        private static readonly IndexInterruptCallbackDelegate IndexInterruptCallback = InterruptIndexIo;
        private static readonly IntPtr IndexInterruptCallbackPointer =
            Marshal.GetFunctionPointerForDelegate(IndexInterruptCallback);

        private FfmpegGlobalFrameIndex(
            List<FfmpegGlobalFrameIndexEntry> entries,
            Stopwatch indexStopwatch,
            CancellationToken cancellationToken)
        {
            _entries = entries ?? throw new ArgumentNullException(nameof(entries));
            _entriesByPresentationTimestamp = new Dictionary<long, FfmpegGlobalFrameIndexEntry>();
            _entriesByDecodeTimestamp = new Dictionary<long, FfmpegGlobalFrameIndexEntry>();
            _ambiguousPresentationTimestamps = new HashSet<long>();
            _ambiguousDecodeTimestamps = new HashSet<long>();

            for (var index = 0; index < _entries.Count; index++)
            {
                if ((index & 1023) == 0)
                {
                    EnsureIndexFinalizationActive(indexStopwatch, cancellationToken);
                }

                var entry = _entries[index];
                AddTimestampLookup(_entriesByPresentationTimestamp, _ambiguousPresentationTimestamps, entry.PresentationTimestamp, entry);
                AddTimestampLookup(_entriesByDecodeTimestamp, _ambiguousDecodeTimestamps, entry.DecodeTimestamp, entry);
            }

            EnsureIndexFinalizationActive(indexStopwatch, cancellationToken);
        }

        public long Count
        {
            get { return _entries.Count; }
        }

        public bool IsAvailable
        {
            get { return _entries.Count > 0; }
        }

        public static FfmpegGlobalFrameIndex Build(string filePath, int videoStreamIndex, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException("A media file path is required.", nameof(filePath));
            }

            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException("The requested media file was not found.", filePath);
            }

            cancellationToken.ThrowIfCancellationRequested();
            var indexStopwatch = Stopwatch.StartNew();
            var rustIndex = TryBuildWithRust(
                filePath,
                videoStreamIndex,
                indexStopwatch,
                cancellationToken);
            return rustIndex ?? BuildManagedIndex(
                filePath,
                videoStreamIndex,
                indexStopwatch,
                cancellationToken);
        }

        private static FfmpegGlobalFrameIndex TryBuildWithRust(
            string filePath,
            int videoStreamIndex,
            Stopwatch indexStopwatch,
            CancellationToken cancellationToken)
        {
            var buildMode = RustFfmpegGlobalFrameIndexBuilder.ResolveBuildMode();
            if (buildMode == RustFfmpegGlobalFrameIndexBuildMode.Managed)
            {
                return null;
            }

            var rustIndex = RustFfmpegGlobalFrameIndexBuilder.TryBuild(
                ffmpeg.RootPath,
                filePath,
                videoStreamIndex,
                FfmpegMediaResourceLimits.GlobalFrameIndexTimeLimit - indexStopwatch.Elapsed,
                cancellationToken);
            if (rustIndex.Succeeded)
            {
                return FromRustIndex(rustIndex, indexStopwatch, cancellationToken);
            }

            if (ShouldFallBackToManagedIndex(buildMode, rustIndex))
            {
                FfmpegMediaResourceLimits.EnsureGlobalFrameIndexCapacity(0, indexStopwatch.Elapsed);
                return null;
            }

            if (rustIndex.ResourceLimitExceeded)
            {
                throw new FfmpegMediaResourceLimitException(
                    "Exact frame indexing exceeded its resource limits: " + rustIndex.Message);
            }

            throw new InvalidOperationException("Rust FFmpeg exact frame index failed: " +
                rustIndex.StatusName + ": " + rustIndex.Message);
        }

        private static FfmpegGlobalFrameIndex BuildManagedIndex(
            string filePath,
            int videoStreamIndex,
            Stopwatch indexStopwatch,
            CancellationToken cancellationToken)
        {
            AVFormatContext* formatContext = null;
            AVCodecContext* codecContext = null;
            AVPacket* packet = null;
            AVFrame* decodedFrame = null;
            GCHandle indexInterruptHandle = default;

            try
            {
                var interruptState = new IndexInterruptState(indexStopwatch, cancellationToken);
                indexInterruptHandle = GCHandle.Alloc(interruptState);
                formatContext = ffmpeg.avformat_alloc_context();
                if (formatContext == null)
                {
                    throw new InvalidOperationException("Could not allocate the FFmpeg format context for frame indexing.");
                }

                formatContext->interrupt_callback.callback.Pointer = IndexInterruptCallbackPointer;
                formatContext->interrupt_callback.opaque =
                    GCHandle.ToIntPtr(indexInterruptHandle).ToPointer();

                var openResult = FfmpegNativeHelpers.OpenInput(&formatContext, filePath, null, null);
                ThrowIfIndexInterrupted(interruptState, 0);
                FfmpegNativeHelpers.ThrowIfError(
                    openResult,
                    "Open media container for frame index");
                var streamInfoResult = ffmpeg.avformat_find_stream_info(formatContext, null);
                ThrowIfIndexInterrupted(interruptState, 0);
                FfmpegNativeHelpers.ThrowIfError(
                    streamInfoResult,
                    "Probe media streams for frame index");

                if (videoStreamIndex < 0 ||
                    videoStreamIndex >= formatContext->nb_streams ||
                    formatContext->streams == null)
                {
                    throw new InvalidOperationException("The requested primary video stream is not available for indexing.");
                }

                var videoStream = formatContext->streams[videoStreamIndex];
                if (videoStream == null || videoStream->codecpar == null)
                {
                    throw new InvalidOperationException("The requested primary video stream is not available for indexing.");
                }

                var decoder = ffmpeg.avcodec_find_decoder(videoStream->codecpar->codec_id);
                if (decoder == null)
                {
                    throw new InvalidOperationException("No decoder is available for the indexed video stream.");
                }

                codecContext = ffmpeg.avcodec_alloc_context3(decoder);
                if (codecContext == null)
                {
                    throw new InvalidOperationException("Could not allocate the FFmpeg codec context for frame indexing.");
                }

                FfmpegNativeHelpers.ThrowIfError(
                    ffmpeg.avcodec_parameters_to_context(codecContext, videoStream->codecpar),
                    "Copy codec parameters for frame index");

                codecContext->pkt_timebase = videoStream->time_base;
                codecContext->framerate = FfmpegNativeHelpers.GetNominalFrameRate(formatContext, videoStream, null);
                codecContext->max_pixels = FfmpegMediaResourceLimits.AbsoluteDecodedFramePixelLimit;

                var openDecoderResult = ffmpeg.avcodec_open2(codecContext, decoder, null);
                ThrowIfIndexInterrupted(interruptState, 0);
                FfmpegNativeHelpers.ThrowIfError(
                    openDecoderResult,
                    "Open video decoder for frame index");

                packet = ffmpeg.av_packet_alloc();
                if (packet == null)
                {
                    throw new InvalidOperationException("Could not allocate an FFmpeg packet for frame indexing.");
                }

                decodedFrame = ffmpeg.av_frame_alloc();
                if (decodedFrame == null)
                {
                    throw new InvalidOperationException("Could not allocate an FFmpeg frame for frame indexing.");
                }

                var entries = DecodeManagedIndexEntries(
                    formatContext,
                    videoStream,
                    codecContext,
                    packet,
                    decodedFrame,
                    videoStreamIndex,
                    interruptState);

                EnsureIndexFinalizationActive(indexStopwatch, cancellationToken);
                return new FfmpegGlobalFrameIndex(entries, indexStopwatch, cancellationToken);
            }
            finally
            {
                ReleaseManagedIndexResources(
                    decodedFrame,
                    packet,
                    codecContext,
                    formatContext,
                    indexInterruptHandle);
            }
        }

        private static List<FfmpegGlobalFrameIndexEntry> DecodeManagedIndexEntries(
            AVFormatContext* formatContext,
            AVStream* videoStream,
            AVCodecContext* codecContext,
            AVPacket* packet,
            AVFrame* decodedFrame,
            int videoStreamIndex,
            IndexInterruptState interruptState)
        {
            var entries = new List<FfmpegGlobalFrameIndexEntry>();
            FfmpegGlobalFrameIndexEntry currentAnchorEntry = null;
            var hasPendingVideoPacket = false;
            var inputExhausted = false;
            var flushPacketSent = false;
            var absoluteFrameIndex = 0L;

            while (true)
            {
                interruptState.CancellationToken.ThrowIfCancellationRequested();
                FfmpegMediaResourceLimits.EnsureGlobalFrameIndexCapacity(
                    entries.Count,
                    interruptState.Stopwatch.Elapsed);

                if (TryReceiveIndexedFrame(
                    videoStream,
                    codecContext,
                    decodedFrame,
                    ref absoluteFrameIndex,
                    ref currentAnchorEntry,
                    interruptState,
                    out var indexedFrame))
                {
                    entries.Add(indexedFrame);
                    continue;
                }

                if (hasPendingVideoPacket)
                {
                    SubmitPendingIndexPacket(codecContext, packet, ref hasPendingVideoPacket);
                    continue;
                }

                if (inputExhausted)
                {
                    if (FlushIndexDecoder(codecContext, ref flushPacketSent))
                    {
                        break;
                    }

                    continue;
                }

                ReadNextIndexPacket(
                    formatContext,
                    packet,
                    videoStreamIndex,
                    interruptState,
                    entries.Count,
                    ref inputExhausted,
                    ref hasPendingVideoPacket);
            }

            return entries;
        }

        private static void SubmitPendingIndexPacket(
            AVCodecContext* codecContext,
            AVPacket* packet,
            ref bool hasPendingVideoPacket)
        {
            var sendPendingResult = ffmpeg.avcodec_send_packet(codecContext, packet);
            if (sendPendingResult == ffmpeg.AVERROR(ffmpeg.EAGAIN))
            {
                return;
            }

            FfmpegNativeHelpers.ThrowIfError(sendPendingResult, "Submit packet to decoder for frame index");
            hasPendingVideoPacket = false;
            ffmpeg.av_packet_unref(packet);
        }

        private static bool FlushIndexDecoder(
            AVCodecContext* codecContext,
            ref bool flushPacketSent)
        {
            if (flushPacketSent)
            {
                return true;
            }

            var flushResult = ffmpeg.avcodec_send_packet(codecContext, null);
            if (flushResult == ffmpeg.AVERROR(ffmpeg.EAGAIN))
            {
                return false;
            }

            if (flushResult == ffmpeg.AVERROR_EOF)
            {
                return true;
            }

            FfmpegNativeHelpers.ThrowIfError(flushResult, "Flush decoder for frame index");
            flushPacketSent = true;
            return false;
        }

        private static void ReadNextIndexPacket(
            AVFormatContext* formatContext,
            AVPacket* packet,
            int videoStreamIndex,
            IndexInterruptState interruptState,
            int currentEntryCount,
            ref bool inputExhausted,
            ref bool hasPendingVideoPacket)
        {
            var readResult = ffmpeg.av_read_frame(formatContext, packet);
            ThrowIfIndexInterrupted(interruptState, currentEntryCount);
            if (readResult == ffmpeg.AVERROR_EOF)
            {
                inputExhausted = true;
                return;
            }

            FfmpegNativeHelpers.ThrowIfError(readResult, "Read encoded packet for frame index");
            if (packet->stream_index != videoStreamIndex)
            {
                ffmpeg.av_packet_unref(packet);
                return;
            }

            hasPendingVideoPacket = true;
        }

        private static void ReleaseManagedIndexResources(
            AVFrame* decodedFrame,
            AVPacket* packet,
            AVCodecContext* codecContext,
            AVFormatContext* formatContext,
            GCHandle indexInterruptHandle)
        {
            if (decodedFrame != null)
            {
                var frameToFree = decodedFrame;
                ffmpeg.av_frame_free(&frameToFree);
            }

            if (packet != null)
            {
                var packetToFree = packet;
                ffmpeg.av_packet_free(&packetToFree);
            }

            if (codecContext != null)
            {
                var codecContextToFree = codecContext;
                ffmpeg.avcodec_free_context(&codecContextToFree);
            }

            if (formatContext != null)
            {
                var formatContextToClose = formatContext;
                ffmpeg.avformat_close_input(&formatContextToClose);
            }

            if (indexInterruptHandle.IsAllocated)
            {
                indexInterruptHandle.Free();
            }
        }

        private static void ThrowIfIndexInterrupted(
            IndexInterruptState interruptState,
            int currentEntryCount)
        {
            interruptState.CancellationToken.ThrowIfCancellationRequested();
            FfmpegMediaResourceLimits.EnsureGlobalFrameIndexCapacity(
                currentEntryCount,
                interruptState.Stopwatch.Elapsed);
        }

        private static int InterruptIndexIo(void* opaque)
        {
            if (opaque == null)
            {
                return 0;
            }

            try
            {
                var handle = GCHandle.FromIntPtr((IntPtr)opaque);
                return handle.Target is IndexInterruptState state && state.ShouldInterrupt
                    ? 1
                    : 0;
            }
            catch
            {
                return 1;
            }
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int IndexInterruptCallbackDelegate(void* opaque);

        private sealed class IndexInterruptState
        {
            internal IndexInterruptState(Stopwatch stopwatch, CancellationToken cancellationToken)
            {
                CancellationToken = cancellationToken;
                Stopwatch = stopwatch ?? throw new ArgumentNullException(nameof(stopwatch));
            }

            internal CancellationToken CancellationToken { get; }

            internal Stopwatch Stopwatch { get; }

            internal bool ShouldInterrupt =>
                CancellationToken.IsCancellationRequested ||
                Stopwatch.Elapsed > FfmpegMediaResourceLimits.GlobalFrameIndexTimeLimit;
        }

        internal static bool ShouldFallBackToManagedIndex(
            RustFfmpegGlobalFrameIndexBuildMode buildMode,
            RustFfmpegGlobalFrameIndexResult rustIndex)
        {
            ArgumentNullException.ThrowIfNull(rustIndex);

            return buildMode == RustFfmpegGlobalFrameIndexBuildMode.Auto &&
                !rustIndex.Succeeded &&
                !rustIndex.ResourceLimitExceeded;
        }

        private static FfmpegGlobalFrameIndex FromRustIndex(
            RustFfmpegGlobalFrameIndexResult rustIndex,
            Stopwatch indexStopwatch,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(rustIndex);

            var timeBase = new AVRational
            {
                num = rustIndex.TimeBaseNumerator,
                den = rustIndex.TimeBaseDenominator
            };
            if (rustIndex.Entries.Length > FfmpegMediaResourceLimits.GlobalFrameIndexEntryLimit)
            {
                throw new FfmpegMediaResourceLimitException(
                    "Rust FFmpeg exact frame index exceeded the configured retained-entry limit.");
            }

            var entries = new List<FfmpegGlobalFrameIndexEntry>(rustIndex.Entries.Length);
            for (var index = 0; index < rustIndex.Entries.Length; index++)
            {
                if ((index & 1023) == 0)
                {
                    EnsureIndexFinalizationActive(indexStopwatch, cancellationToken);
                }

                var rustEntry = rustIndex.Entries[index];
                var presentationTime = rustEntry.SearchTimestamp.HasValue
                    ? FfmpegNativeHelpers.ToTimeSpan(rustEntry.SearchTimestamp.Value, timeBase)
                    : TimeSpan.Zero;
                var seekAnchorStrategy = rustEntry.SeekAnchorTimestamp > 0L
                    ? "global-index-keyframe"
                    : "stream-start";
                entries.Add(new FfmpegGlobalFrameIndexEntry(
                    rustEntry.AbsoluteFrameIndex,
                    presentationTime,
                    new FfmpegGlobalFrameTimestamps(
                        rustEntry.PresentationTimestamp,
                        rustEntry.DecodeTimestamp,
                        rustEntry.SearchTimestamp),
                    rustEntry.IsKeyFrame,
                    rustEntry.SeekAnchorFrameIndex,
                    rustEntry.SeekAnchorTimestamp,
                    seekAnchorStrategy));
            }

            EnsureIndexFinalizationActive(indexStopwatch, cancellationToken);
            return new FfmpegGlobalFrameIndex(entries, indexStopwatch, cancellationToken);
        }

        private static void EnsureIndexFinalizationActive(
            Stopwatch indexStopwatch,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            FfmpegMediaResourceLimits.EnsureGlobalFrameIndexCapacity(
                0,
                indexStopwatch.Elapsed);
        }

        public bool TryGetByAbsoluteFrameIndex(long frameIndex, out FfmpegGlobalFrameIndexEntry entry)
        {
            if (frameIndex >= 0L && frameIndex < _entries.Count)
            {
                entry = _entries[(int)frameIndex];
                return true;
            }

            entry = null;
            return false;
        }

        public bool TryGetLastEntry(out FfmpegGlobalFrameIndexEntry entry)
        {
            if (_entries.Count > 0)
            {
                entry = _entries[_entries.Count - 1];
                return true;
            }

            entry = null;
            return false;
        }

        public bool TryGetFirstAtOrAfterTimestamp(long timestamp, out FfmpegGlobalFrameIndexEntry entry)
        {
            foreach (var candidate in _entries)
            {
                if (!candidate.SearchTimestamp.HasValue)
                {
                    continue;
                }

                if (candidate.SearchTimestamp.Value >= timestamp)
                {
                    entry = candidate;
                    return true;
                }
            }

            entry = null;
            return false;
        }

        public bool TryResolve(long? presentationTimestamp, long? decodeTimestamp, out FfmpegGlobalFrameIndexEntry entry)
        {
            if (TryGetUniqueTimestampMatch(
                _entriesByPresentationTimestamp,
                _ambiguousPresentationTimestamps,
                presentationTimestamp,
                out entry))
            {
                return true;
            }

            return TryGetUniqueTimestampMatch(
                _entriesByDecodeTimestamp,
                _ambiguousDecodeTimestamps,
                decodeTimestamp,
                out entry);
        }

        private static bool TryReceiveIndexedFrame(
            AVStream* videoStream,
            AVCodecContext* codecContext,
            AVFrame* decodedFrame,
            ref long absoluteFrameIndex,
            ref FfmpegGlobalFrameIndexEntry currentAnchorEntry,
            IndexInterruptState interruptState,
            out FfmpegGlobalFrameIndexEntry entry)
        {
            while (true)
            {
                var receiveResult = ffmpeg.avcodec_receive_frame(codecContext, decodedFrame);
                ThrowIfIndexInterrupted(interruptState, checked((int)absoluteFrameIndex));
                if (receiveResult == ffmpeg.AVERROR(ffmpeg.EAGAIN) || receiveResult == ffmpeg.AVERROR_EOF)
                {
                    entry = null;
                    return false;
                }

                FfmpegNativeHelpers.ThrowIfError(receiveResult, "Decode video frame for frame index");

                try
                {
                    if (decodedFrame->width <= 0 || decodedFrame->height <= 0)
                    {
                        continue;
                    }

                    entry = CreateIndexEntry(
                        videoStream,
                        decodedFrame,
                        absoluteFrameIndex,
                        currentAnchorEntry);

                    absoluteFrameIndex++;
                    if (entry.IsKeyFrame)
                    {
                        currentAnchorEntry = entry;
                    }

                    return true;
                }
                finally
                {
                    ffmpeg.av_frame_unref(decodedFrame);
                }
            }
        }

        private static FfmpegGlobalFrameIndexEntry CreateIndexEntry(
            AVStream* videoStream,
            AVFrame* decodedFrame,
            long absoluteFrameIndex,
            FfmpegGlobalFrameIndexEntry currentAnchorEntry)
        {
            var presentationTimestamp = FfmpegNativeHelpers.GetBestPresentationTimestamp(decodedFrame);
            var decodeTimestamp = FfmpegNativeHelpers.AsNullableTimestamp(decodedFrame->pkt_dts);
            var searchTimestamp = FfmpegNativeHelpers.GetBestAvailableTimestamp(presentationTimestamp, decodeTimestamp);
            var presentationTime = searchTimestamp.HasValue
                ? FfmpegNativeHelpers.ToTimeSpan(searchTimestamp.Value, videoStream->time_base)
                : TimeSpan.Zero;
            var isKeyFrame = (decodedFrame->flags & ffmpeg.AV_FRAME_FLAG_KEY) != 0;

            long seekAnchorFrameIndex;
            long seekAnchorTimestamp;
            string seekAnchorStrategy;

            if (isKeyFrame && searchTimestamp.HasValue && searchTimestamp.Value > 0L)
            {
                seekAnchorFrameIndex = absoluteFrameIndex;
                seekAnchorTimestamp = searchTimestamp.Value;
                seekAnchorStrategy = "global-index-keyframe";
            }
            else if (currentAnchorEntry != null)
            {
                seekAnchorFrameIndex = currentAnchorEntry.AbsoluteFrameIndex;
                seekAnchorTimestamp = currentAnchorEntry.SeekAnchorTimestamp;
                seekAnchorStrategy = currentAnchorEntry.SeekAnchorStrategy;
            }
            else
            {
                seekAnchorFrameIndex = 0L;
                seekAnchorTimestamp = 0L;
                seekAnchorStrategy = "stream-start";
            }

            return new FfmpegGlobalFrameIndexEntry(
                absoluteFrameIndex,
                presentationTime,
                new FfmpegGlobalFrameTimestamps(
                    presentationTimestamp,
                    decodeTimestamp,
                    searchTimestamp),
                isKeyFrame,
                seekAnchorFrameIndex,
                seekAnchorTimestamp,
                seekAnchorStrategy);
        }

        private static void AddTimestampLookup(
            Dictionary<long, FfmpegGlobalFrameIndexEntry> entriesByTimestamp,
            HashSet<long> ambiguousTimestamps,
            long? timestamp,
            FfmpegGlobalFrameIndexEntry entry)
        {
            if (!timestamp.HasValue || entry == null)
            {
                return;
            }

            if (ambiguousTimestamps.Contains(timestamp.Value))
            {
                return;
            }

            FfmpegGlobalFrameIndexEntry existingEntry;
            if (entriesByTimestamp.TryGetValue(timestamp.Value, out existingEntry))
            {
                entriesByTimestamp.Remove(timestamp.Value);
                ambiguousTimestamps.Add(timestamp.Value);
                return;
            }

            entriesByTimestamp[timestamp.Value] = entry;
        }

        private static bool TryGetUniqueTimestampMatch(
            Dictionary<long, FfmpegGlobalFrameIndexEntry> entriesByTimestamp,
            HashSet<long> ambiguousTimestamps,
            long? timestamp,
            out FfmpegGlobalFrameIndexEntry entry)
        {
            if (!timestamp.HasValue || ambiguousTimestamps.Contains(timestamp.Value))
            {
                entry = null;
                return false;
            }

            return entriesByTimestamp.TryGetValue(timestamp.Value, out entry);
        }
    }

    internal readonly struct FfmpegGlobalFrameTimestamps
    {
        public FfmpegGlobalFrameTimestamps(
            long? presentationTimestamp,
            long? decodeTimestamp,
            long? searchTimestamp)
        {
            PresentationTimestamp = presentationTimestamp;
            DecodeTimestamp = decodeTimestamp;
            SearchTimestamp = searchTimestamp;
        }

        public long? PresentationTimestamp { get; }

        public long? DecodeTimestamp { get; }

        public long? SearchTimestamp { get; }
    }

    internal sealed class FfmpegGlobalFrameIndexEntry
    {
        public FfmpegGlobalFrameIndexEntry(
            long absoluteFrameIndex,
            TimeSpan presentationTime,
            FfmpegGlobalFrameTimestamps timestamps,
            bool isKeyFrame,
            long seekAnchorFrameIndex,
            long seekAnchorTimestamp,
            string seekAnchorStrategy)
        {
            AbsoluteFrameIndex = absoluteFrameIndex;
            PresentationTime = presentationTime;
            PresentationTimestamp = timestamps.PresentationTimestamp;
            DecodeTimestamp = timestamps.DecodeTimestamp;
            SearchTimestamp = timestamps.SearchTimestamp;
            IsKeyFrame = isKeyFrame;
            SeekAnchorFrameIndex = seekAnchorFrameIndex;
            SeekAnchorTimestamp = seekAnchorTimestamp > 0L ? seekAnchorTimestamp : 0L;
            SeekAnchorStrategy = string.IsNullOrWhiteSpace(seekAnchorStrategy)
                ? "stream-start"
                : seekAnchorStrategy;
        }

        public long AbsoluteFrameIndex { get; }

        public TimeSpan PresentationTime { get; }

        public long? PresentationTimestamp { get; }

        public long? DecodeTimestamp { get; }

        public long? SearchTimestamp { get; }

        public bool IsKeyFrame { get; }

        public long SeekAnchorFrameIndex { get; }

        public long SeekAnchorTimestamp { get; }

        public string SeekAnchorStrategy { get; }
    }
}
