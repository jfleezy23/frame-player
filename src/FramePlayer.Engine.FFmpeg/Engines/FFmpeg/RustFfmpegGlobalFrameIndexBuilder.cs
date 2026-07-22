using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using FFmpeg.AutoGen;

namespace FramePlayer.Engines.FFmpeg
{
    internal static partial class RustFfmpegGlobalFrameIndexBuilder
    {
        private const long NoTimestamp = long.MinValue;
        private const string BuilderModeEnvironmentVariable = "FRAMEPLAYER_FFMPEG_INDEX_BUILDER";

        public static RustFfmpegGlobalFrameIndexBuildMode ResolveBuildMode()
        {
            var configuredValue = Environment.GetEnvironmentVariable(BuilderModeEnvironmentVariable);
            if (string.IsNullOrWhiteSpace(configuredValue))
            {
                return RustFfmpegGlobalFrameIndexBuildMode.Auto;
            }

            switch (configuredValue.Trim().ToLowerInvariant())
            {
                case "managed":
                case "csharp":
                case "c#":
                    return RustFfmpegGlobalFrameIndexBuildMode.Managed;
                case "rust":
                    return RustFfmpegGlobalFrameIndexBuildMode.Rust;
                default:
                    return RustFfmpegGlobalFrameIndexBuildMode.Auto;
            }
        }

        public static RustFfmpegGlobalFrameIndexResult TryBuild(
            string runtimeDirectory,
            string filePath,
            int videoStreamIndex,
            TimeSpan maxElapsed,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!RustFfmpegNativeLayout.TryValidateNativeAbi(out var abiErrorMessage))
            {
                return RustFfmpegGlobalFrameIndexResult.Unavailable(
                    "native-abi-mismatch",
                    abiErrorMessage);
            }

            if (!RustFfmpegNativeLayout.TryValidateDecodeCore(out var layoutErrorMessage))
            {
                return RustFfmpegGlobalFrameIndexResult.Unavailable(
                    "native-layout-mismatch",
                    layoutErrorMessage);
            }

            if (string.IsNullOrWhiteSpace(runtimeDirectory))
            {
                return RustFfmpegGlobalFrameIndexResult.Unavailable(
                    "runtime-directory-missing",
                    "FFmpeg runtime directory is not configured.");
            }

            if (string.IsNullOrWhiteSpace(filePath))
            {
                return RustFfmpegGlobalFrameIndexResult.Unavailable(
                    "file-missing",
                    "Media file path is not configured.");
            }

            if (videoStreamIndex < 0)
            {
                return RustFfmpegGlobalFrameIndexResult.Unavailable(
                    "stream-missing",
                    "Video stream index is not configured.");
            }

            if (maxElapsed <= TimeSpan.Zero)
            {
                return new RustFfmpegGlobalFrameIndexResult(
                    false,
                    "resource-limit-exceeded",
                    "Exact frame indexing reached its processing-time limit.",
                    0,
                    0,
                    Array.Empty<RustFfmpegGlobalFrameIndexEntry>());
            }

            var indexStopwatch = Stopwatch.StartNew();
            using var cancellationFlag = new RustFfmpegCancellationFlag();
            try
            {
                using (cancellationFlag.Register(cancellationToken))
                {
                    NativeGlobalFrameIndexResult nativeResult;
                    var returnStatus = frameplayer_rust_ffmpeg_global_frame_index(
                        runtimeDirectory,
                        filePath,
                        videoStreamIndex,
                        FfmpegMediaResourceLimits.GlobalFrameIndexEntryLimit,
                        checked((ulong)FfmpegMediaResourceLimits.GlobalFrameIndexNativeByteLimit),
                        checked((ulong)Math.Max(1d, Math.Ceiling(maxElapsed.TotalMilliseconds))),
                        cancellationFlag.Pointer,
                        out nativeResult);
                    var nativeStatus = nativeResult.Status == 0 ? returnStatus : nativeResult.Status;

                    try
                    {
                        if (nativeStatus == 15 || cancellationToken.IsCancellationRequested)
                        {
                            throw new OperationCanceledException(cancellationToken);
                        }

                        EnsureIndexFinalizationActive(
                            indexStopwatch,
                            maxElapsed,
                            cancellationToken);

                        var entries = nativeStatus == 0
                            ? CopyEntries(
                                nativeResult,
                                indexStopwatch,
                                maxElapsed,
                                cancellationToken)
                            : Array.Empty<RustFfmpegGlobalFrameIndexEntry>();
                        return new RustFfmpegGlobalFrameIndexResult(
                            nativeStatus == 0,
                            ResolveStatusName(nativeStatus),
                            ReadMessage(nativeResult),
                            nativeResult.TimeBaseNumerator,
                            nativeResult.TimeBaseDenominator,
                            entries);
                    }
                    finally
                    {
                        if (nativeResult.Entries != IntPtr.Zero)
                        {
                            frameplayer_rust_ffmpeg_global_frame_index_free(
                                nativeResult.Entries,
                                new UIntPtr(nativeResult.EntryCount));
                        }
                    }
                }
            }
            catch (FfmpegMediaResourceLimitException ex)
            {
                return new RustFfmpegGlobalFrameIndexResult(
                    false,
                    "resource-limit-exceeded",
                    ex.Message,
                    0,
                    0,
                    Array.Empty<RustFfmpegGlobalFrameIndexEntry>());
            }
            catch (DllNotFoundException ex)
            {
                return RustFfmpegGlobalFrameIndexResult.Unavailable(
                    "native-library-missing",
                    "Rust FFmpeg exact frame index library was not found: " + ex.Message);
            }
            catch (EntryPointNotFoundException ex)
            {
                return RustFfmpegGlobalFrameIndexResult.Unavailable(
                    "native-entrypoint-missing",
                    "Rust FFmpeg exact frame index entrypoint was not found: " + ex.Message);
            }
            catch (BadImageFormatException ex)
            {
                return RustFfmpegGlobalFrameIndexResult.Unavailable(
                    "native-library-invalid",
                    "Rust FFmpeg exact frame index library could not be loaded by this process: " + ex.Message);
            }
        }

        private static RustFfmpegGlobalFrameIndexEntry[] CopyEntries(
            NativeGlobalFrameIndexResult nativeResult,
            Stopwatch indexStopwatch,
            TimeSpan maxElapsed,
            CancellationToken cancellationToken)
        {
            if (nativeResult.EntryCount == 0 || nativeResult.Entries == IntPtr.Zero)
            {
                return Array.Empty<RustFfmpegGlobalFrameIndexEntry>();
            }

            if (nativeResult.EntryCount > int.MaxValue)
            {
                throw new InvalidOperationException("Rust FFmpeg exact frame index returned too many entries to marshal.");
            }

            if (nativeResult.EntryCount > FfmpegMediaResourceLimits.GlobalFrameIndexEntryLimit)
            {
                throw new FfmpegMediaResourceLimitException(
                    "Rust FFmpeg exact frame index returned more entries than the configured retained-entry limit.");
            }

            var entryCount = checked((int)nativeResult.EntryCount);
            var entries = new List<RustFfmpegGlobalFrameIndexEntry>(entryCount);
            for (var index = 0; index < entryCount; index++)
            {
                if ((index & 1023) == 0)
                {
                    EnsureIndexFinalizationActive(
                        indexStopwatch,
                        maxElapsed,
                        cancellationToken);
                }

                entries.Add(ToManaged(
                    RustFfmpegNativeArray.Read<NativeGlobalFrameIndexEntry>(
                        nativeResult.Entries,
                        index,
                        entryCount)));
            }

            var copiedEntries = entries.ToArray();
            EnsureIndexFinalizationActive(
                indexStopwatch,
                maxElapsed,
                cancellationToken);
            return copiedEntries;
        }

        private static void EnsureIndexFinalizationActive(
            Stopwatch indexStopwatch,
            TimeSpan maxElapsed,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (indexStopwatch.Elapsed > maxElapsed)
            {
                throw new FfmpegMediaResourceLimitException(
                    "Exact frame indexing reached its processing-time limit during managed finalization.");
            }
        }

        private static RustFfmpegGlobalFrameIndexEntry ToManaged(NativeGlobalFrameIndexEntry entry)
        {
            return new RustFfmpegGlobalFrameIndexEntry(
                entry.AbsoluteFrameIndex,
                ToNullableTimestamp(entry.PresentationTimestamp),
                ToNullableTimestamp(entry.DecodeTimestamp),
                ToNullableTimestamp(entry.SearchTimestamp),
                entry.IsKeyFrame != 0,
                entry.SeekAnchorFrameIndex,
                entry.SeekAnchorTimestamp);
        }

        private static long? ToNullableTimestamp(long timestamp)
        {
            return timestamp == NoTimestamp ? (long?)null : timestamp;
        }

        private static string ResolveStatusName(int nativeStatus)
        {
            switch (nativeStatus)
            {
                case 0:
                    return "ok";
                case 1:
                    return "invalid-argument";
                case 2:
                    return "runtime-directory-missing";
                case 3:
                    return "library-load-failed";
                case 4:
                    return "symbol-load-failed";
                case 5:
                    return "file-open-failed";
                case 6:
                    return "stream-unavailable";
                case 7:
                    return "decoder-unavailable";
                case 8:
                    return "codec-context-alloc-failed";
                case 9:
                    return "codec-context-failed";
                case 10:
                    return "packet-alloc-failed";
                case 11:
                    return "frame-alloc-failed";
                case 12:
                    return "packet-read-failed";
                case 13:
                    return "packet-send-failed";
                case 14:
                    return "frame-receive-failed";
                case 15:
                    return "cancelled";
                case 20:
                    return "resource-limit-exceeded";
                default:
                    return "native-error";
            }
        }

        private static string ReadMessage(NativeGlobalFrameIndexResult nativeResult)
        {
            return nativeResult.Message.ToString();
        }

        [LibraryImport("frameplayer_ffmpeg_probe", StringMarshalling = StringMarshalling.Utf8)]
        [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static partial int frameplayer_rust_ffmpeg_global_frame_index(
            string runtimeDirectory,
            string filePath,
            int videoStreamIndex,
            ulong maxEntries,
            ulong maxNativeBytes,
            ulong maxElapsedMilliseconds,
            IntPtr cancellationFlag,
            out NativeGlobalFrameIndexResult result);

        [LibraryImport("frameplayer_ffmpeg_probe")]
        [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static partial void frameplayer_rust_ffmpeg_global_frame_index_free(
            IntPtr entries,
            UIntPtr entryCount);

        [StructLayout(LayoutKind.Sequential)]
        internal struct NativeGlobalFrameIndexResult
        {
            public int Status;
            public IntPtr Entries;
            public ulong EntryCount;
            public int TimeBaseNumerator;
            public int TimeBaseDenominator;
            public RustFfmpegNativeMessage Message;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeGlobalFrameIndexEntry
        {
            public long AbsoluteFrameIndex;
            public long PresentationTimestamp;
            public long DecodeTimestamp;
            public long SearchTimestamp;
            public int IsKeyFrame;
            public long SeekAnchorFrameIndex;
            public long SeekAnchorTimestamp;
        }
    }

    internal enum RustFfmpegGlobalFrameIndexBuildMode
    {
        Auto,
        Managed,
        Rust
    }

    internal sealed class RustFfmpegGlobalFrameIndexResult
    {
        public RustFfmpegGlobalFrameIndexResult(
            bool succeeded,
            string statusName,
            string message,
            int timeBaseNumerator,
            int timeBaseDenominator,
            RustFfmpegGlobalFrameIndexEntry[] entries)
        {
            Succeeded = succeeded;
            StatusName = statusName ?? string.Empty;
            Message = message ?? string.Empty;
            TimeBaseNumerator = timeBaseNumerator;
            TimeBaseDenominator = timeBaseDenominator;
            Entries = entries ?? Array.Empty<RustFfmpegGlobalFrameIndexEntry>();
        }

        public bool Succeeded { get; }

        public string StatusName { get; }

        public string Message { get; }

        public int TimeBaseNumerator { get; }

        public int TimeBaseDenominator { get; }

        public RustFfmpegGlobalFrameIndexEntry[] Entries { get; }

        public bool ResourceLimitExceeded
        {
            get { return string.Equals(StatusName, "resource-limit-exceeded", StringComparison.Ordinal); }
        }

        public static RustFfmpegGlobalFrameIndexResult Unavailable(string statusName, string message)
        {
            return new RustFfmpegGlobalFrameIndexResult(
                false,
                statusName,
                message,
                0,
                0,
                Array.Empty<RustFfmpegGlobalFrameIndexEntry>());
        }
    }

    internal sealed class RustFfmpegGlobalFrameIndexEntry
    {
        public RustFfmpegGlobalFrameIndexEntry(
            long absoluteFrameIndex,
            long? presentationTimestamp,
            long? decodeTimestamp,
            long? searchTimestamp,
            bool isKeyFrame,
            long seekAnchorFrameIndex,
            long seekAnchorTimestamp)
        {
            AbsoluteFrameIndex = absoluteFrameIndex;
            PresentationTimestamp = presentationTimestamp;
            DecodeTimestamp = decodeTimestamp;
            SearchTimestamp = searchTimestamp;
            IsKeyFrame = isKeyFrame;
            SeekAnchorFrameIndex = seekAnchorFrameIndex;
            SeekAnchorTimestamp = seekAnchorTimestamp > 0L ? seekAnchorTimestamp : 0L;
        }

        public long AbsoluteFrameIndex { get; }

        public long? PresentationTimestamp { get; }

        public long? DecodeTimestamp { get; }

        public long? SearchTimestamp { get; }

        public bool IsKeyFrame { get; }

        public long SeekAnchorFrameIndex { get; }

        public long SeekAnchorTimestamp { get; }
    }
}
