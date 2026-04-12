using System;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FramePlayer.Core.Models;

namespace FramePlayer.Services
{
    internal static class WpfFrameBufferPresenter
    {
        public static BitmapSource CreateBitmapSource(DecodedFrameBuffer frameBuffer)
        {
            if (frameBuffer == null ||
                !frameBuffer.HasPixelBuffer ||
                frameBuffer.Descriptor == null ||
                frameBuffer.Descriptor.PixelWidth <= 0 ||
                frameBuffer.Descriptor.PixelHeight <= 0)
            {
                return null;
            }

            var dpiX = 96d;
            var dpiY = 96d;
            if (frameBuffer.Descriptor.DisplayWidth > 0 &&
                frameBuffer.Descriptor.DisplayHeight > 0)
            {
                dpiX = Math.Max(
                    1d,
                    96d * frameBuffer.Descriptor.PixelWidth / frameBuffer.Descriptor.DisplayWidth);
                dpiY = Math.Max(
                    1d,
                    96d * frameBuffer.Descriptor.PixelHeight / frameBuffer.Descriptor.DisplayHeight);
            }

            var bitmapSource = BitmapSource.Create(
                frameBuffer.Descriptor.PixelWidth,
                frameBuffer.Descriptor.PixelHeight,
                dpiX,
                dpiY,
                PixelFormats.Bgra32,
                null,
                frameBuffer.PixelBuffer,
                frameBuffer.Stride);
            bitmapSource.Freeze();
            return bitmapSource;
        }
    }
}
