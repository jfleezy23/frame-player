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

            var bitmapSource = BitmapSource.Create(
                frameBuffer.Descriptor.PixelWidth,
                frameBuffer.Descriptor.PixelHeight,
                96d,
                96d,
                PixelFormats.Bgra32,
                null,
                frameBuffer.PixelBuffer,
                frameBuffer.Stride);
            bitmapSource.Freeze();
            return bitmapSource;
        }
    }
}
