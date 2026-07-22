using System;
using System.Runtime.InteropServices;
using System.Text;

namespace FramePlayer.Engines.FFmpeg
{
    public static unsafe class RustFfmpegProbe
    {
        private const int MessageCapacity = 256;

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
            var message = nativeResult.Message;
            var length = 0;
            while (length < MessageCapacity && message[length] != 0)
            {
                length++;
            }

            return Encoding.UTF8.GetString(message, length);
        }

        [DllImport("frameplayer_ffmpeg_probe", CallingConvention = CallingConvention.Cdecl)]
        private static extern int frameplayer_rust_ffmpeg_probe(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string runtimeDirectory,
            out NativeProbeResult result);

        [StructLayout(LayoutKind.Sequential)]
        private unsafe struct NativeProbeResult
        {
            public int Status;
            public uint AvutilVersion;
            public uint AvcodecVersion;
            public uint AvformatVersion;
            public fixed byte Message[MessageCapacity];
        }
    }
}
