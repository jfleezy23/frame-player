using System;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using FramePlayer.Core.Models;

namespace FramePlayer.Avalonia.Services
{
    internal static class AvaloniaFrameBufferPresenter
    {
        public static WriteableBitmap? PresentBitmap(DecodedFrameBuffer frameBuffer, PaneViewportSnapshot? viewportSnapshot, ref WriteableBitmap? reusableBitmap)
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

            var crop = ResolveCrop(descriptor.PixelWidth, descriptor.PixelHeight, viewportSnapshot);
            var bitmap = ResolveOrCreateBitmap(crop.Width, crop.Height, ref reusableBitmap);

            using (var locked = bitmap.Lock())
            {
                unsafe
                {
                    if (frameBuffer.TryGetPixelBufferPointer(out var nativePixelBufferPointer))
                    {
                        CopyRows((byte*)nativePixelBufferPointer, frameBuffer.Stride, crop, locked.Address, locked.RowBytes);
                    }
                    else
                    {
                        fixed (byte* sourceStart = frameBuffer.PixelBuffer)
                        {
                            CopyRows(sourceStart, frameBuffer.Stride, crop, locked.Address, locked.RowBytes);
                        }
                    }
                }
            }

            reusableBitmap = bitmap;
            return bitmap;
        }

        public static WriteableBitmap? CreateBitmap(DecodedFrameBuffer frameBuffer)
        {
            WriteableBitmap? unused = null;
            return PresentBitmap(frameBuffer, null, ref unused);
        }

        public static WriteableBitmap? CreateBitmap(DecodedFrameBuffer frameBuffer, PaneViewportSnapshot? viewportSnapshot)
        {
            WriteableBitmap? unused = null;
            return PresentBitmap(frameBuffer, viewportSnapshot, ref unused);
        }

        private static WriteableBitmap ResolveOrCreateBitmap(int width, int height, ref WriteableBitmap? existing)
        {
            if (existing != null &&
                existing.PixelSize.Width == width &&
                existing.PixelSize.Height == height)
            {
                return existing;
            }

            var previous = existing;
            existing = new WriteableBitmap(
                new PixelSize(width, height),
                new Vector(96d, 96d),
                PixelFormat.Bgra8888,
                AlphaFormat.Premul);

            previous?.Dispose();
            return existing;
        }

        private static unsafe void CopyRows(
            byte* sourceStart,
            int sourceStride,
            PixelRect crop,
            IntPtr destinationStart,
            int destinationStride)
        {
            const int bytesPerPixel = 4;
            var copyBytesPerRow = Math.Min(crop.Width * bytesPerPixel, destinationStride);
            for (var y = 0; y < crop.Height; y++)
            {
                var sourceRow = sourceStart + ((crop.Y + y) * sourceStride) + (crop.X * bytesPerPixel);
                var destinationRow = (byte*)destinationStart + (y * destinationStride);
                Buffer.MemoryCopy(sourceRow, destinationRow, destinationStride, copyBytesPerRow);
            }
        }

        private static PixelRect ResolveCrop(int sourceWidth, int sourceHeight, PaneViewportSnapshot? viewportSnapshot)
        {
            if (viewportSnapshot == null || !viewportSnapshot.IsZoomed)
            {
                return new PixelRect(0, 0, sourceWidth, sourceHeight);
            }

            var cropX = Math.Max(0, Math.Min(sourceWidth - 1, viewportSnapshot.SourceCropX));
            var cropY = Math.Max(0, Math.Min(sourceHeight - 1, viewportSnapshot.SourceCropY));
            var cropWidth = Math.Max(1, Math.Min(sourceWidth - cropX, viewportSnapshot.SourceCropWidth));
            var cropHeight = Math.Max(1, Math.Min(sourceHeight - cropY, viewportSnapshot.SourceCropHeight));
            return new PixelRect(cropX, cropY, cropWidth, cropHeight);
        }
    }
}
