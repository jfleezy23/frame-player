using System;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using FramePlayer.Core.Models;

namespace FramePlayer.Mac.Services
{
    internal static class AvaloniaFrameBufferPresenter
    {
        public static WriteableBitmap? CreateBitmap(DecodedFrameBuffer frameBuffer)
        {
            if (frameBuffer == null || !frameBuffer.HasPixelBuffer)
            {
                return null;
            }

            var descriptor = frameBuffer.Descriptor;
            if (descriptor == null || descriptor.PixelWidth <= 0 || descriptor.PixelHeight <= 0)
            {
                return null;
            }

            var bitmap = new WriteableBitmap(
                new PixelSize(descriptor.PixelWidth, descriptor.PixelHeight),
                new Vector(96d, 96d),
                PixelFormat.Bgra8888,
                AlphaFormat.Premul);

            using (var locked = bitmap.Lock())
            {
                unsafe
                {
                    fixed (byte* sourceStart = frameBuffer.PixelBuffer)
                    {
                        var sourceStride = frameBuffer.Stride;
                        var copyBytesPerRow = Math.Min(Math.Abs(sourceStride), locked.RowBytes);
                        for (var y = 0; y < descriptor.PixelHeight; y++)
                        {
                            var sourceRow = sourceStart + (y * sourceStride);
                            var destinationRow = (byte*)locked.Address + (y * locked.RowBytes);
                            Buffer.MemoryCopy(sourceRow, destinationRow, locked.RowBytes, copyBytesPerRow);
                        }
                    }
                }
            }

            return bitmap;
        }
    }
}
