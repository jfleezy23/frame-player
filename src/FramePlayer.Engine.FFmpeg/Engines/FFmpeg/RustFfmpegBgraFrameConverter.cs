using System;
using System.Runtime.InteropServices;
using System.Text;
using FFmpeg.AutoGen;
using FramePlayer.Core.Models;
using Microsoft.Win32.SafeHandles;

namespace FramePlayer.Engines.FFmpeg
{
    internal unsafe sealed class RustFfmpegBgraFrameConverter : IDisposable
    {
        private const int MessageCapacity = 256;
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

            if (!RustFfmpegNativeLayout.TryValidateFrameConverter(out errorMessage))
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

        public bool TryConvert(AVFrame* sourceFrame, FrameDescriptor descriptor, out DecodedFrameBuffer frameBuffer, out string errorMessage)
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
                    (IntPtr)sourceFrame,
                    out result);
                var nativeStatus = result.Status != 0 ? result.Status : status;
                if (nativeStatus != 0)
                {
                    errorMessage = ResolveStatusName(nativeStatus) + ": " + ReadMessage(result);
                    ReleaseNativeFrameBuffer(result.Frame);
                    return false;
                }

                frameBuffer = ToFrameBuffer(result.Frame, descriptor);
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

        internal static DecodedFrameBuffer ToFrameBuffer(NativeFrame nativeFrame, FrameDescriptor descriptor)
        {
            if (nativeFrame.PixelBuffer == IntPtr.Zero ||
                nativeFrame.PixelData == IntPtr.Zero ||
                nativeFrame.PixelBufferLength == UIntPtr.Zero ||
                nativeFrame.PixelBufferLength.ToUInt64() > int.MaxValue)
            {
                ReleaseNativeFrameBuffer(nativeFrame);
                throw new InvalidOperationException("Rust frame conversion returned an invalid native pixel buffer.");
            }

            var pixelBufferLength = checked((int)nativeFrame.PixelBufferLength.ToUInt64());
            var handle = new RustFfmpegNativeFrameBufferHandle(nativeFrame.PixelBuffer, pixelBufferLength);
            try
            {
                return new DecodedFrameBuffer(
                    descriptor,
                    handle,
                    nativeFrame.PixelData,
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
                default:
                    return "native-error";
            }
        }

        private static string ReadMessage(NativeFrameConvertResult nativeResult)
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
        private static extern int frameplayer_rust_ffmpeg_frame_converter_create(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string runtimeDirectory,
            out IntPtr converter,
            out NativeFrameConvertResult result);

        [DllImport("frameplayer_ffmpeg_probe", CallingConvention = CallingConvention.Cdecl)]
        private static extern int frameplayer_rust_ffmpeg_frame_converter_convert(
            IntPtr converter,
            IntPtr sourceFrame,
            out NativeFrameConvertResult result);

        [DllImport("frameplayer_ffmpeg_probe", CallingConvention = CallingConvention.Cdecl)]
        private static extern void frameplayer_rust_ffmpeg_frame_converter_free(IntPtr converter);

        [DllImport("frameplayer_ffmpeg_probe", CallingConvention = CallingConvention.Cdecl)]
        private static extern void frameplayer_rust_ffmpeg_frame_buffer_free(IntPtr pixelBuffer);

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
        private unsafe struct NativeFrameConvertResult
        {
            public int Status;
            public NativeFrame Frame;
            public fixed byte Message[MessageCapacity];
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
