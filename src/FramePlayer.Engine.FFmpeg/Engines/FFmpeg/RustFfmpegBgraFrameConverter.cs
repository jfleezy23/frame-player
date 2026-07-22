using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using FramePlayer.Core.Models;
using Microsoft.Win32.SafeHandles;

namespace FramePlayer.Engines.FFmpeg
{
    internal sealed partial class RustFfmpegBgraFrameConverter : IDisposable
    {
        private IntPtr _converter;

        private RustFfmpegBgraFrameConverter(IntPtr converter)
        {
            _converter = converter;
        }

        public static bool TryCreate(string runtimeDirectory, out RustFfmpegBgraFrameConverter converter, out string errorMessage)
        {
            converter = null;
            errorMessage = string.Empty;
            if (string.IsNullOrWhiteSpace(runtimeDirectory))
            {
                errorMessage = "FFmpeg runtime directory is not configured.";
                return false;
            }

            if (!RustFfmpegNativeLayout.TryValidateNativeAbi(out errorMessage) ||
                !RustFfmpegNativeLayout.TryValidateFrameConverter(out errorMessage))
            {
                return false;
            }

            try
            {
                NativeFrameConvertResult result;
                IntPtr converterHandle;
                var status = frameplayer_rust_ffmpeg_frame_converter_create(
                    runtimeDirectory,
                    out converterHandle,
                    out result);
                if (status != 0 || converterHandle == IntPtr.Zero)
                {
                    errorMessage = ResolveStatusName(status != 0 ? status : result.Status) + ": " + ReadMessage(result);
                    return false;
                }

                converter = new RustFfmpegBgraFrameConverter(converterHandle);
                return true;
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
            catch (Exception ex)
            {
                errorMessage = "native-frame-converter-create-failed: " + ex.Message;
                return false;
            }
        }

        public bool TryConvert(
            IntPtr sourceFrame,
            FrameDescriptor descriptor,
            long maxFrameBytes,
            out DecodedFrameBuffer frameBuffer,
            out string errorMessage)
        {
            frameBuffer = null;
            errorMessage = string.Empty;
            if (_converter == IntPtr.Zero)
            {
                errorMessage = "Rust frame converter has been disposed.";
                return false;
            }

            try
            {
                NativeFrameConvertResult result;
                var status = frameplayer_rust_ffmpeg_frame_converter_convert(
                    _converter,
                    sourceFrame,
                    checked((ulong)maxFrameBytes),
                    out result);
                var nativeStatus = result.Status != 0 ? result.Status : status;
                if (nativeStatus != 0)
                {
                    errorMessage = ResolveStatusName(nativeStatus) + ": " + ReadMessage(result);
                    ReleaseNativeFrameBuffer(result.Frame);
                    return false;
                }

                try
                {
                    frameBuffer = ToFrameBuffer(ref result.Frame, descriptor);
                    return true;
                }
                finally
                {
                    ReleaseNativeFrameBuffer(result.Frame);
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
            catch (Exception ex)
            {
                errorMessage = "native-frame-conversion-failed: " + ex.Message;
                return false;
            }
        }

        public void Dispose()
        {
            if (_converter == IntPtr.Zero)
            {
                return;
            }

            frameplayer_rust_ffmpeg_frame_converter_free(_converter);
            _converter = IntPtr.Zero;
        }

        internal static DecodedFrameBuffer ToFrameBuffer(
            ref NativeFrame nativeFrame,
            FrameDescriptor descriptor)
        {
            if (nativeFrame.PixelBuffer == IntPtr.Zero ||
                nativeFrame.PixelData == IntPtr.Zero ||
                nativeFrame.PixelBufferLength == UIntPtr.Zero ||
                nativeFrame.PixelBufferLength.ToUInt64() > int.MaxValue ||
                nativeFrame.PixelBufferLength.ToUInt64() >
                    (ulong)FfmpegMediaResourceLimits.AbsoluteDecodedFrameByteLimit)
            {
                throw new InvalidOperationException("Rust frame conversion returned an invalid native pixel buffer.");
            }

            var pixelBufferLength = checked((int)nativeFrame.PixelBufferLength.ToUInt64());
            var pixelData = nativeFrame.PixelData;
            var handle = new RustFfmpegNativeFrameBufferHandle(nativeFrame.PixelBuffer, pixelBufferLength);
            nativeFrame.PixelBuffer = IntPtr.Zero;
            nativeFrame.PixelData = IntPtr.Zero;
            nativeFrame.PixelBufferLength = UIntPtr.Zero;
            try
            {
                return new DecodedFrameBuffer(
                    descriptor,
                    handle,
                    pixelData,
                    pixelBufferLength,
                    nativeFrame.Stride,
                    "bgra");
            }
            catch
            {
                handle.Dispose();
                throw;
            }
        }

        internal static void ReleaseNativeFrameBuffer(NativeFrame nativeFrame)
        {
            if (nativeFrame.PixelBuffer != IntPtr.Zero)
            {
                frameplayer_rust_ffmpeg_frame_buffer_free(nativeFrame.PixelBuffer);
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
                case 16:
                    return "conversion-failed";
                case 20:
                    return "resource-limit-exceeded";
                default:
                    return "native-error";
            }
        }

        private static string ReadMessage(NativeFrameConvertResult nativeResult)
        {
            return nativeResult.Message.ToString();
        }

        [LibraryImport("frameplayer_ffmpeg_probe", StringMarshalling = StringMarshalling.Utf8)]
        [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static partial int frameplayer_rust_ffmpeg_frame_converter_create(
            string runtimeDirectory,
            out IntPtr converter,
            out NativeFrameConvertResult result);

        [LibraryImport("frameplayer_ffmpeg_probe")]
        [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static partial int frameplayer_rust_ffmpeg_frame_converter_convert(
            IntPtr converter,
            IntPtr sourceFrame,
            ulong maxFrameBufferBytes,
            out NativeFrameConvertResult result);

        [LibraryImport("frameplayer_ffmpeg_probe")]
        [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static partial void frameplayer_rust_ffmpeg_frame_converter_free(IntPtr converter);

        [LibraryImport("frameplayer_ffmpeg_probe")]
        [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static partial void frameplayer_rust_ffmpeg_frame_buffer_free(IntPtr pixelBuffer);

        [StructLayout(LayoutKind.Sequential)]
        internal struct NativeFrame
        {
            public long AbsoluteFrameIndex;
            public long PresentationTimestamp;
            public long DecodeTimestamp;
            public long DurationTimestamp;
            public int IsKeyFrame;
            public IntPtr PixelBuffer;
            public IntPtr PixelData;
            public UIntPtr PixelBufferLength;
            public int Stride;
            public int Width;
            public int Height;
            public int DisplayWidth;
            public int DisplayHeight;
            public int SourcePixelFormat;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct NativeFrameConvertResult
        {
            public int Status;
            public NativeFrame Frame;
            public RustFfmpegNativeMessage Message;
        }

        private sealed class RustFfmpegNativeFrameBufferHandle : SafeHandleZeroOrMinusOneIsInvalid
        {
            private readonly int _byteCount;

            public RustFfmpegNativeFrameBufferHandle(IntPtr handle, int byteCount)
                : base(ownsHandle: true)
            {
                _byteCount = byteCount;
                if (_byteCount > 0)
                {
                    GC.AddMemoryPressure(_byteCount);
                }

                SetHandle(handle);
            }

            protected override bool ReleaseHandle()
            {
                frameplayer_rust_ffmpeg_frame_buffer_free(handle);
                if (_byteCount > 0)
                {
                    GC.RemoveMemoryPressure(_byteCount);
                }

                return true;
            }
        }
    }
}
