using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace FramePlayer.Engines.FFmpeg
{
    public static partial class RustFfmpegProbe
    {
        public static RustFfmpegProbeResult TryProbe(string runtimeDirectory)
        {
            if (string.IsNullOrWhiteSpace(runtimeDirectory))
            {
                return RustFfmpegProbeResult.Unavailable(
                    "runtime-directory-missing",
                    "FFmpeg runtime directory is not configured.");
            }

            if (!RustFfmpegNativeLayout.TryValidateNativeAbi(out var abiErrorMessage))
            {
                return RustFfmpegProbeResult.Unavailable(
                    "native-abi-mismatch",
                    abiErrorMessage);
            }

            try
            {
                NativeProbeResult nativeResult;
                var returnStatus = frameplayer_rust_ffmpeg_probe(runtimeDirectory, out nativeResult);
                var nativeStatus = nativeResult.Status == 0 ? returnStatus : nativeResult.Status;
                return RustFfmpegProbeResult.FromNative(
                    nativeStatus,
                    ResolveStatusName(nativeStatus),
                    ReadMessage(nativeResult),
                    nativeResult.AvutilVersion,
                    nativeResult.AvcodecVersion,
                    nativeResult.AvformatVersion);
            }
            catch (DllNotFoundException ex)
            {
                return RustFfmpegProbeResult.Unavailable(
                    "native-library-missing",
                    "Rust FFmpeg runtime probe library was not found: " + ex.Message);
            }
            catch (EntryPointNotFoundException ex)
            {
                return RustFfmpegProbeResult.Unavailable(
                    "native-entrypoint-missing",
                    "Rust FFmpeg runtime probe entrypoint was not found: " + ex.Message);
            }
            catch (BadImageFormatException ex)
            {
                return RustFfmpegProbeResult.Unavailable(
                    "native-library-invalid",
                    "Rust FFmpeg runtime probe library could not be loaded by this process: " + ex.Message);
            }
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
                default:
                    return "native-error";
            }
        }

        private static string ReadMessage(NativeProbeResult nativeResult)
        {
            return nativeResult.Message.ToString();
        }

        [LibraryImport("frameplayer_ffmpeg_probe", StringMarshalling = StringMarshalling.Utf8)]
        [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static partial int frameplayer_rust_ffmpeg_probe(
            string runtimeDirectory,
            out NativeProbeResult result);

        [StructLayout(LayoutKind.Sequential)]
        internal struct NativeProbeResult
        {
            public int Status;
            public uint AvutilVersion;
            public uint AvcodecVersion;
            public uint AvformatVersion;
            public RustFfmpegNativeMessage Message;
        }
    }
}
