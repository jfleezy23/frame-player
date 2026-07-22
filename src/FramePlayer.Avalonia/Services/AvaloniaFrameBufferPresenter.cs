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
            if (frameBuffer == null)
            {
                return null;
            }

            DecodedFrameBuffer retainedFrameBuffer;
            try
            {
                retainedFrameBuffer = frameBuffer.Retain();
            }
            catch (ObjectDisposedException)
            {
                return null;
            }

            using (retainedFrameBuffer)
            {
                if (!retainedFrameBuffer.HasPixelBuffer)
                {
                    return null;
                }

                var descriptor = retainedFrameBuffer.Descriptor;
                if (descriptor == null || descriptor.PixelWidth <= 0 || descriptor.PixelHeight <= 0)
                {
                    return null;
                }

                var crop = ResolveCrop(descriptor.PixelWidth, descriptor.PixelHeight, viewportSnapshot);
                if (!HasValidSourceLayout(retainedFrameBuffer, crop))
                {
                    return null;
                }

                var bitmap = ResolveOrCreateBitmap(crop.Width, crop.Height, ref reusableBitmap);

                using (var locked = bitmap.Lock())
                {
                    var requiredDestinationRowBytes = checked(crop.Width * 4);
                    if (locked.Address == IntPtr.Zero || locked.RowBytes < requiredDestinationRowBytes)
                    {
                        throw new InvalidOperationException("Avalonia returned an invalid writable frame buffer.");
                    }

                    unsafe
                    {
                        if (retainedFrameBuffer.TryGetPixelBufferPointer(out var nativePixelBufferPointer))
                        {
                            CopyRows((byte*)nativePixelBufferPointer, retainedFrameBuffer.Stride, crop, locked.Address, locked.RowBytes);
                        }
                        else
                        {
                            fixed (byte* sourceStart = retainedFrameBuffer.PixelBuffer)
                            {
                                CopyRows(sourceStart, retainedFrameBuffer.Stride, crop, locked.Address, locked.RowBytes);
                            }
                        }
                    }
                }

                reusableBitmap = bitmap;
                return bitmap;
            }
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
            var copyBytesPerRow = Math.Min(checked(crop.Width * bytesPerPixel), destinationStride);
            for (var y = 0; y < crop.Height; y++)
            {
                var sourceOffset = checked(((crop.Y + y) * sourceStride) + (crop.X * bytesPerPixel));
                var destinationOffset = checked(y * destinationStride);
                var sourceRow = sourceStart + sourceOffset;
                var destinationRow = (byte*)destinationStart + destinationOffset;
                Buffer.MemoryCopy(sourceRow, destinationRow, destinationStride, copyBytesPerRow);
            }
        }

        private static bool HasValidSourceLayout(DecodedFrameBuffer frameBuffer, PixelRect crop)
        {
            const int bytesPerPixel = 4;
            if (frameBuffer.Stride <= 0 ||
                frameBuffer.PixelBufferLength <= 0 ||
                crop.X < 0 ||
                crop.Y < 0 ||
                crop.Width <= 0 ||
                crop.Height <= 0)
            {
                return false;
            }

            var sourceRowEnd = ((long)crop.X + crop.Width) * bytesPerPixel;
            if (sourceRowEnd > frameBuffer.Stride)
            {
                return false;
            }

            var lastSourceRow = (long)crop.Y + crop.Height - 1L;
            var requiredSourceBytes = (lastSourceRow * frameBuffer.Stride) + sourceRowEnd;
            return requiredSourceBytes > 0L && requiredSourceBytes <= frameBuffer.PixelBufferLength;
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
