using System;
using System.Collections.Generic;
using System.IO;
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

        private FfmpegGlobalFrameIndex(List<FfmpegGlobalFrameIndexEntry> entries)
        {
            _entries = entries ?? throw new ArgumentNullException(nameof(entries));
            _entriesByPresentationTimestamp = new Dictionary<long, FfmpegGlobalFrameIndexEntry>();
            _entriesByDecodeTimestamp = new Dictionary<long, FfmpegGlobalFrameIndexEntry>();
            _ambiguousPresentationTimestamps = new HashSet<long>();
            _ambiguousDecodeTimestamps = new HashSet<long>();

            foreach (var entry in _entries)
            {
                AddTimestampLookup(_entriesByPresentationTimestamp, _ambiguousPresentationTimestamps, entry.PresentationTimestamp, entry);
                AddTimestampLookup(_entriesByDecodeTimestamp, _ambiguousDecodeTimestamps, entry.DecodeTimestamp, entry);
            }
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

            AVFormatContext* formatContext = null;
            AVCodecContext* codecContext = null;
            AVPacket* packet = null;
            AVFrame* decodedFrame = null;

            try
            {
                FfmpegNativeHelpers.ThrowIfError(
                    FfmpegNativeHelpers.OpenInput(&formatContext, filePath, null, null),
                    "Open media container for frame index");
                FfmpegNativeHelpers.ThrowIfError(
                    ffmpeg.avformat_find_stream_info(formatContext, null),
                    "Probe media streams for frame index");

                if (videoStreamIndex < 0 || videoStreamIndex >= formatContext->nb_streams)
                {
                    throw new InvalidOperationException("The requested primary video stream is not available for indexing.");
                }

                var videoStream = formatContext->streams[videoStreamIndex];
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

                FfmpegNativeHelpers.ThrowIfError(
                    ffmpeg.avcodec_open2(codecContext, decoder, null),
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

                var entries = new List<FfmpegGlobalFrameIndexEntry>();
                FfmpegGlobalFrameIndexEntry currentAnchorEntry = null;
                bool hasPendingVideoPacket = false;
                bool inputExhausted = false;
                bool flushPacketSent = false;
                long absoluteFrameIndex = 0L;

                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    FfmpegGlobalFrameIndexEntry indexedFrame;
                    if (TryReceiveIndexedFrame(
                        formatContext,
                        videoStream,
                        codecContext,
                        decodedFrame,
                        ref absoluteFrameIndex,
                        ref currentAnchorEntry,
                        out indexedFrame))
                    {
                        entries.Add(indexedFrame);
                        continue;
                    }

                    if (hasPendingVideoPacket)
                    {
                        var sendPendingResult = ffmpeg.avcodec_send_packet(codecContext, packet);
                        if (sendPendingResult == ffmpeg.AVERROR(ffmpeg.EAGAIN))
                        {
                            continue;
                        }

                        FfmpegNativeHelpers.ThrowIfError(sendPendingResult, "Submit packet to decoder for frame index");
                        hasPendingVideoPacket = false;
                        ffmpeg.av_packet_unref(packet);
                        continue;
                    }

                    if (inputExhausted)
                    {
                        if (flushPacketSent)
                        {
                            break;
                        }

                        var flushResult = ffmpeg.avcodec_send_packet(codecContext, null);
                        if (flushResult == ffmpeg.AVERROR(ffmpeg.EAGAIN))
                        {
                            continue;
                        }

                        if (flushResult == ffmpeg.AVERROR_EOF)
                        {
                            flushPacketSent = true;
                            break;
                        }

                        FfmpegNativeHelpers.ThrowIfError(flushResult, "Flush decoder for frame index");
                        flushPacketSent = true;
                        continue;
                    }

                    var readResult = ffmpeg.av_read_frame(formatContext, packet);
                    if (readResult == ffmpeg.AVERROR_EOF)
                    {
                        inputExhausted = true;
                        continue;
                    }

                    FfmpegNativeHelpers.ThrowIfError(readResult, "Read encoded packet for frame index");
                    if (packet->stream_index != videoStreamIndex)
                    {
                        ffmpeg.av_packet_unref(packet);
                        continue;
                    }

                    hasPendingVideoPacket = true;
                }

                return new FfmpegGlobalFrameIndex(entries);
            }
            finally
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
            }
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
            AVFormatContext* formatContext,
            AVStream* videoStream,
            AVCodecContext* codecContext,
            AVFrame* decodedFrame,
            ref long absoluteFrameIndex,
            ref FfmpegGlobalFrameIndexEntry currentAnchorEntry,
            out FfmpegGlobalFrameIndexEntry entry)
        {
            while (true)
            {
                var receiveResult = ffmpeg.avcodec_receive_frame(codecContext, decodedFrame);
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
                        formatContext,
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
            AVFormatContext* formatContext,
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
                presentationTimestamp,
                decodeTimestamp,
                searchTimestamp,
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

    internal sealed class FfmpegGlobalFrameIndexEntry
    {
        public FfmpegGlobalFrameIndexEntry(
            long absoluteFrameIndex,
            TimeSpan presentationTime,
            long? presentationTimestamp,
            long? decodeTimestamp,
            long? searchTimestamp,
            bool isKeyFrame,
            long seekAnchorFrameIndex,
            long seekAnchorTimestamp,
            string seekAnchorStrategy)
        {
            AbsoluteFrameIndex = absoluteFrameIndex;
            PresentationTime = presentationTime;
            PresentationTimestamp = presentationTimestamp;
            DecodeTimestamp = decodeTimestamp;
            SearchTimestamp = searchTimestamp;
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
