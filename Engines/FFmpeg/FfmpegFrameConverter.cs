using System;
using FFmpeg.AutoGen;
using FramePlayer.Core.Models;

namespace FramePlayer.Engines.FFmpeg
{
    internal unsafe sealed class FfmpegFrameConverter : IDisposable
    {
        private const AVPixelFormat OutputPixelFormat = AVPixelFormat.AV_PIX_FMT_BGRA;
        private SwsContext* _scaleContext;

        public DecodedFrameBuffer Convert(AVFrame* sourceFrame, FrameDescriptor descriptor)
        {
            if (sourceFrame == null)
            {
                throw new ArgumentNullException(nameof(sourceFrame));
            }

            EnsureScaleContext(sourceFrame);

            var width = sourceFrame->width;
            var height = sourceFrame->height;
            var bufferSize = ffmpeg.av_image_get_buffer_size(OutputPixelFormat, width, height, 1);
            FfmpegNativeHelpers.ThrowIfError(bufferSize, "Allocate BGRA conversion buffer");

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

        public void Dispose()
        {
            if (_scaleContext == null)
            {
                return;
            }

            ffmpeg.sws_freeContext(_scaleContext);
            _scaleContext = null;
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
    }
}
