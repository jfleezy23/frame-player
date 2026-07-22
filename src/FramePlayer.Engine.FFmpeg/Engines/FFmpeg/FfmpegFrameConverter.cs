using System;
using FFmpeg.AutoGen;
using FramePlayer.Core.Models;

namespace FramePlayer.Engines.FFmpeg
{
    internal unsafe sealed class FfmpegFrameConverter : IDisposable
    {
        private const AVPixelFormat OutputPixelFormat = AVPixelFormat.AV_PIX_FMT_BGRA;
        private const string ConverterModeEnvironmentVariable = "FRAMEPLAYER_FFMPEG_FRAME_CONVERTER";
        private SwsContext* _scaleContext;
        private RustFfmpegBgraFrameConverter _rustConverter;
        private bool _rustConverterUnavailable;
        private string _rustConverterErrorMessage = string.Empty;

        public DecodedFrameBuffer Convert(AVFrame* sourceFrame, FrameDescriptor descriptor, long maxFrameBytes)
        {
            if (sourceFrame == null)
            {
                throw new ArgumentNullException(nameof(sourceFrame));
            }

            FfmpegMediaResourceLimits.EnsureBgraFrameWithinLimit(
                sourceFrame->width,
                sourceFrame->height,
                maxFrameBytes);

            var converterMode = ResolveConverterMode();
            if (converterMode != FfmpegFrameConverterMode.Managed &&
                TryConvertWithRust(
                    sourceFrame,
                    descriptor,
                    converterMode,
                    maxFrameBytes,
                    out var frameBuffer))
            {
                return frameBuffer;
            }

            return ConvertManaged(sourceFrame, descriptor, maxFrameBytes);
        }

        public void Dispose()
        {
            if (_scaleContext != null)
            {
                ffmpeg.sws_freeContext(_scaleContext);
                _scaleContext = null;
            }

            _rustConverter?.Dispose();
            _rustConverter = null;
        }

        private DecodedFrameBuffer ConvertManaged(AVFrame* sourceFrame, FrameDescriptor descriptor, long maxFrameBytes)
        {
            EnsureScaleContext(sourceFrame);

            var width = sourceFrame->width;
            var height = sourceFrame->height;
            var bufferSize = ffmpeg.av_image_get_buffer_size(OutputPixelFormat, width, height, 1);
            FfmpegNativeHelpers.ThrowIfError(bufferSize, "Allocate BGRA conversion buffer");
            if (!FfmpegMediaResourceLimits.TryReserveBytes(0L, bufferSize, maxFrameBytes, out _))
            {
                throw new FfmpegMediaResourceLimitException(
                    "FFmpeg's decoded BGRA buffer size exceeded the active frame limit.");
            }

            var pixelBuffer = new byte[bufferSize];
            int stride;

            fixed (byte* pixelBufferPointer = pixelBuffer)
            {
                var destinationData = new byte_ptrArray4();
                var destinationLinesize = new int_array4();
                FfmpegNativeHelpers.ThrowIfError(
                    ffmpeg.av_image_fill_arrays(
                        ref destinationData,
                        ref destinationLinesize,
                        pixelBufferPointer,
                        OutputPixelFormat,
                        width,
                        height,
                        1),
                    "Describe BGRA frame buffer");

                var scaledHeight = ffmpeg.sws_scale(
                    _scaleContext,
                    sourceFrame->data.ToArray(),
                    sourceFrame->linesize.ToArray(),
                    0,
                    height,
                    destinationData.ToArray(),
                    destinationLinesize.ToArray());
                if (scaledHeight <= 0)
                {
                    throw new InvalidOperationException("Convert decoded frame to BGRA");
                }

                stride = destinationLinesize[0];
            }

            return new DecodedFrameBuffer(
                descriptor,
                pixelBuffer,
                stride,
                "bgra");
        }

        private bool TryConvertWithRust(
            AVFrame* sourceFrame,
            FrameDescriptor descriptor,
            FfmpegFrameConverterMode converterMode,
            long maxFrameBytes,
            out DecodedFrameBuffer frameBuffer)
        {
            frameBuffer = null;
            if (_rustConverterUnavailable)
            {
                if (converterMode == FfmpegFrameConverterMode.Rust)
                {
                    throw new InvalidOperationException("Rust FFmpeg frame converter is unavailable: " + _rustConverterErrorMessage);
                }

                return false;
            }

            if (_rustConverter == null &&
                !RustFfmpegBgraFrameConverter.TryCreate(ffmpeg.RootPath, out _rustConverter, out _rustConverterErrorMessage))
            {
                _rustConverterUnavailable = true;
                if (converterMode == FfmpegFrameConverterMode.Rust)
                {
                    throw new InvalidOperationException("Rust FFmpeg frame converter is unavailable: " + _rustConverterErrorMessage);
                }

                return false;
            }

            if (_rustConverter.TryConvert(
                (IntPtr)sourceFrame,
                descriptor,
                maxFrameBytes,
                out frameBuffer,
                out _rustConverterErrorMessage))
            {
                return true;
            }

            if (converterMode == FfmpegFrameConverterMode.Rust)
            {
                throw new InvalidOperationException("Rust FFmpeg frame converter failed: " + _rustConverterErrorMessage);
            }

            return false;
        }

        private void EnsureScaleContext(AVFrame* sourceFrame)
        {
            _scaleContext = ffmpeg.sws_getCachedContext(
                _scaleContext,
                sourceFrame->width,
                sourceFrame->height,
                (AVPixelFormat)sourceFrame->format,
                sourceFrame->width,
                sourceFrame->height,
                OutputPixelFormat,
                (int)SwsFlags.SWS_BILINEAR,
                null,
                null,
                null);

            if (_scaleContext == null)
            {
                throw new InvalidOperationException("Create swscale BGRA conversion context");
            }
        }

        private static FfmpegFrameConverterMode ResolveConverterMode()
        {
            var configuredValue = Environment.GetEnvironmentVariable(ConverterModeEnvironmentVariable);
            if (string.IsNullOrWhiteSpace(configuredValue))
            {
                return FfmpegFrameConverterMode.Auto;
            }

            switch (configuredValue.Trim().ToLowerInvariant())
            {
                case "managed":
                case "csharp":
                case "c#":
                    return FfmpegFrameConverterMode.Managed;
                case "rust":
                    return FfmpegFrameConverterMode.Rust;
                default:
                    return FfmpegFrameConverterMode.Auto;
            }
        }
    }

    internal enum FfmpegFrameConverterMode
    {
        Auto,
        Managed,
        Rust
    }
}
