using System;
using Avalonia;
using Avalonia.Media.Imaging;
using FramePlayer.Core.Models;
using AlphaFormat = global::Avalonia.Platform.AlphaFormat;
using PixelFormat = global::Avalonia.Platform.PixelFormat;

namespace FramePlayer.Host.Avalonia
{
    internal static class AvaloniaFrameBufferPresenter
    {
        public static WriteableBitmap CreateBitmap(DecodedFrameBuffer frameBuffer)
        {
            if (frameBuffer == null ||
                frameBuffer.Descriptor == null ||
                frameBuffer.Descriptor.PixelWidth <= 0 ||
                frameBuffer.Descriptor.PixelHeight <= 0 ||
                frameBuffer.PixelBuffer == null ||
                frameBuffer.PixelBuffer.Length == 0)
            {
                return null;
            }

            var bitmap = new WriteableBitmap(
                new PixelSize(frameBuffer.Descriptor.PixelWidth, frameBuffer.Descriptor.PixelHeight),
                new Vector(96, 96),
                PixelFormat.Bgra8888,
                AlphaFormat.Unpremul);

            using (var locked = bitmap.Lock())
            {
                var bytesPerRow = Math.Min(Math.Abs(frameBuffer.Stride), locked.RowBytes);
                for (var row = 0; row < frameBuffer.Descriptor.PixelHeight; row++)
                {
                    var sourceOffset = row * frameBuffer.Stride;
                    var destination = locked.Address + row * locked.RowBytes;
                    System.Runtime.InteropServices.Marshal.Copy(frameBuffer.PixelBuffer, sourceOffset, destination, bytesPerRow);
                }
            }

            return bitmap;
        }
    }
}
