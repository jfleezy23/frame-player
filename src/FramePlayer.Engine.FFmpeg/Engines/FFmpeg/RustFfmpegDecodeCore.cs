using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using FFmpeg.AutoGen;
using FramePlayer.Core.Models;

namespace FramePlayer.Engines.FFmpeg
{
    internal static unsafe class RustFfmpegDecodeCore
    {
        private const int MessageCapacity = 256;
        private const long NoTimestamp = long.MinValue;
        private const string DecodeModeEnvironmentVariable = "FRAMEPLAYER_FFMPEG_DECODE_CORE";

        public static RustFfmpegDecodeCoreMode ResolveMode()
        {
            var configuredValue = Environment.GetEnvironmentVariable(DecodeModeEnvironmentVariable);
            if (string.IsNullOrWhiteSpace(configuredValue))
            {
                return RustFfmpegDecodeCoreMode.Auto;
            }

            switch (configuredValue.Trim().ToLowerInvariant())
            {
                case "managed":
                case "csharp":
                case "c#":
                    return RustFfmpegDecodeCoreMode.Managed;
                case "rust":
                    return RustFfmpegDecodeCoreMode.Rust;
                default:
                    return RustFfmpegDecodeCoreMode.Auto;
            }
        }

        public static bool TryDecodeIndexedWindow(
            string runtimeDirectory,
            string filePath,
            int videoStreamIndex,
            FfmpegGlobalFrameIndexEntry anchorEntry,
            FfmpegGlobalFrameIndexEntry targetEntry,
            int previousFrameLimit,
            int forwardFrameLimit,
            AVRational videoStreamTimeBase,
            CancellationToken cancellationToken,
            out List<DecodedFrameBuffer> frames,
            out int currentIndex,
            out string errorMessage)
        {
            frames = null;
            currentIndex = -1;
            errorMessage = string.Empty;

            if (string.IsNullOrWhiteSpace(runtimeDirectory) ||
                string.IsNullOrWhiteSpace(filePath) ||
                anchorEntry == null ||
                targetEntry == null ||
                videoStreamIndex < 0)
            {
                errorMessage = "Rust decode core arguments were invalid.";
                return false;
            }

            if (!RustFfmpegNativeLayout.TryValidateDecodeCore(out errorMessage))
            {
                return false;
            }

            var cancellationFlag = Marshal.AllocHGlobal(sizeof(int));
            Marshal.WriteInt32(cancellationFlag, 0);
            try
            {
                using (cancellationToken.Register(state => Marshal.WriteInt32((IntPtr)state, 1), cancellationFlag))
                {
                    NativeDecodeWindowResult nativeResult;
                    var status = frameplayer_rust_ffmpeg_decode_window(
                        runtimeDirectory,
                        filePath,
                        videoStreamIndex,
                        ToNativeEntry(anchorEntry),
                        ToNativeEntry(targetEntry),
                        previousFrameLimit,
                        forwardFrameLimit,
                        cancellationFlag,
                        out nativeResult);
                    var nativeStatus = nativeResult.Status != 0 ? nativeResult.Status : status;

                    try
                    {
                        if (nativeStatus == 15 || cancellationToken.IsCancellationRequested)
                        {
                            throw new OperationCanceledException(cancellationToken);
                        }

                        if (nativeStatus != 0)
                        {
                            errorMessage = ResolveStatusName(nativeStatus) + ": " + ReadMessage(nativeResult);
                            return false;
                        }

                        frames = CopyFrames(nativeResult, videoStreamTimeBase);
                        currentIndex = nativeResult.CurrentIndex;
                        return true;
                    }
                    finally
                    {
                        if (nativeResult.Frames != IntPtr.Zero)
                        {
                            frameplayer_rust_ffmpeg_decode_window_free(
                                nativeResult.Frames,
                                new UIntPtr(nativeResult.FrameCount));
                        }
                    }
                }
            }
            catch (DllNotFoundException ex)
            {
                errorMessage = "native-library-missing: " + ex.Message;
                return false;
            }
            catch (EntryPointNotFoundException ex)
            {
                errorMessage = "native-entrypoint-missing: " + ex.Message;
                return false;
            }
            catch (BadImageFormatException ex)
            {
                errorMessage = "native-library-invalid: " + ex.Message;
                return false;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                DisposeDecodedFrames(frames);
                frames = null;
                currentIndex = -1;
                errorMessage = "native-decode-window-marshal-failed: " + ex.Message;
                return false;
            }
            finally
            {
                Marshal.FreeHGlobal(cancellationFlag);
            }
        }

        private static void DisposeDecodedFrames(IEnumerable<DecodedFrameBuffer> frames)
        {
            if (frames == null)
            {
                return;
            }

            foreach (var frame in frames)
            {
                frame?.Dispose();
            }
        }

        private static List<DecodedFrameBuffer> CopyFrames(
            NativeDecodeWindowResult nativeResult,
            AVRational videoStreamTimeBase)
        {
            if (nativeResult.FrameCount == 0 || nativeResult.Frames == IntPtr.Zero)
            {
                return new List<DecodedFrameBuffer>();
            }

            if (nativeResult.FrameCount > int.MaxValue)
            {
                throw new InvalidOperationException("Rust decode core returned too many frames to marshal.");
            }

            var nativeFrames = (RustFfmpegBgraFrameConverter.NativeFrame*)nativeResult.Frames;
            var frames = new List<DecodedFrameBuffer>((int)nativeResult.FrameCount);
            try
            {
                for (var index = 0; index < (int)nativeResult.FrameCount; index++)
                {
                    frames.Add(ToDecodedFrameBuffer(nativeFrames[index], videoStreamTimeBase));
                }
            }
            catch
            {
                for (var index = 0; index < frames.Count; index++)
                {
                    frames[index]?.Dispose();
                }

                for (var index = frames.Count; index < (int)nativeResult.FrameCount; index++)
                {
                    RustFfmpegBgraFrameConverter.ReleaseNativeFrameBuffer(nativeFrames[index]);
                }

                throw;
            }

            return frames;
        }

        private static DecodedFrameBuffer ToDecodedFrameBuffer(
            RustFfmpegBgraFrameConverter.NativeFrame nativeFrame,
            AVRational videoStreamTimeBase)
        {
            var presentationTimestamp = ToNullableTimestamp(nativeFrame.PresentationTimestamp);
            var descriptor = new FrameDescriptor(
                nativeFrame.AbsoluteFrameIndex >= 0L ? (long?)nativeFrame.AbsoluteFrameIndex : null,
                presentationTimestamp.HasValue
                    ? FfmpegNativeHelpers.ToTimeSpan(presentationTimestamp.Value, videoStreamTimeBase)
                    : TimeSpan.Zero,
                nativeFrame.IsKeyFrame != 0,
                nativeFrame.AbsoluteFrameIndex >= 0L,
                nativeFrame.Width,
                nativeFrame.Height,
                "bgra",
                FfmpegNativeHelpers.GetPixelFormatName((AVPixelFormat)nativeFrame.SourcePixelFormat),
                presentationTimestamp,
                ToNullableTimestamp(nativeFrame.DecodeTimestamp),
                ToNullableTimestamp(nativeFrame.DurationTimestamp),
                nativeFrame.DisplayWidth > 0 ? (int?)nativeFrame.DisplayWidth : null,
                nativeFrame.DisplayHeight > 0 ? (int?)nativeFrame.DisplayHeight : null);

            return RustFfmpegBgraFrameConverter.ToFrameBuffer(nativeFrame, descriptor);
        }

        private static NativeIndexEntry ToNativeEntry(FfmpegGlobalFrameIndexEntry entry)
        {
            return new NativeIndexEntry
            {
                AbsoluteFrameIndex = entry.AbsoluteFrameIndex,
                PresentationTimestamp = entry.PresentationTimestamp ?? NoTimestamp,
                DecodeTimestamp = entry.DecodeTimestamp ?? NoTimestamp,
                SearchTimestamp = entry.SearchTimestamp ?? NoTimestamp,
                SeekAnchorFrameIndex = entry.SeekAnchorFrameIndex,
                SeekAnchorTimestamp = entry.SeekAnchorTimestamp
            };
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
                case 15:
                    return "cancelled";
                case 17:
                    return "seek-failed";
                case 18:
                    return "anchor-not-found";
                case 19:
                    return "target-not-found";
                default:
                    return "native-error";
            }
        }

        private static string ReadMessage(NativeDecodeWindowResult nativeResult)
        {
            var message = nativeResult.Message;
            var length = 0;
            while (length < MessageCapacity && message[length] != 0)
            {
                length++;
            }

            return Encoding.UTF8.GetString(message, length);
        }

        [DllImport("frameplayer_ffmpeg_probe", CallingConvention = CallingConvention.Cdecl)]
        private static extern int frameplayer_rust_ffmpeg_decode_window(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string runtimeDirectory,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string filePath,
            int videoStreamIndex,
            NativeIndexEntry anchorEntry,
            NativeIndexEntry targetEntry,
            int previousFrameLimit,
            int forwardFrameLimit,
            IntPtr cancellationFlag,
            out NativeDecodeWindowResult result);

        [DllImport("frameplayer_ffmpeg_probe", CallingConvention = CallingConvention.Cdecl)]
        private static extern void frameplayer_rust_ffmpeg_decode_window_free(
            IntPtr frames,
            UIntPtr frameCount);

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeIndexEntry
        {
            public long AbsoluteFrameIndex;
            public long PresentationTimestamp;
            public long DecodeTimestamp;
            public long SearchTimestamp;
            public long SeekAnchorFrameIndex;
            public long SeekAnchorTimestamp;
        }

        [StructLayout(LayoutKind.Sequential)]
        private unsafe struct NativeDecodeWindowResult
        {
            public int Status;
            public IntPtr Frames;
            public ulong FrameCount;
            public int CurrentIndex;
            public fixed byte Message[MessageCapacity];
        }
    }

    internal enum RustFfmpegDecodeCoreMode
    {
        Auto,
        Managed,
        Rust
    }
}
