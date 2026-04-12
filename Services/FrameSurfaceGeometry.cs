using System;
using System.Windows;

namespace FramePlayer.Services
{
    internal readonly struct FrameSurfaceGeometry
    {
        private FrameSurfaceGeometry(Rect renderedRect, int sourcePixelWidth, int sourcePixelHeight)
        {
            RenderedRect = renderedRect;
            SourcePixelWidth = sourcePixelWidth;
            SourcePixelHeight = sourcePixelHeight;
        }

        public Rect RenderedRect { get; }

        public int SourcePixelWidth { get; }

        public int SourcePixelHeight { get; }

        public bool HasVisibleFrame
        {
            get
            {
                return SourcePixelWidth > 0 &&
                       SourcePixelHeight > 0 &&
                       RenderedRect.Width > 0d &&
                       RenderedRect.Height > 0d;
            }
        }

        public static FrameSurfaceGeometry Empty { get; } =
            new FrameSurfaceGeometry(Rect.Empty, 0, 0);

        public static FrameSurfaceGeometry Create(
            Size hostSize,
            int sourcePixelWidth,
            int sourcePixelHeight,
            double displayWidth,
            double displayHeight)
        {
            if (hostSize.Width <= 0d ||
                hostSize.Height <= 0d ||
                sourcePixelWidth <= 0 ||
                sourcePixelHeight <= 0 ||
                displayWidth <= 0d ||
                displayHeight <= 0d)
            {
                return Empty;
            }

            var sourceAspectRatio = displayWidth / displayHeight;
            var hostAspectRatio = hostSize.Width / hostSize.Height;

            double renderedWidth;
            double renderedHeight;
            if (hostAspectRatio > sourceAspectRatio)
            {
                renderedHeight = hostSize.Height;
                renderedWidth = renderedHeight * sourceAspectRatio;
            }
            else
            {
                renderedWidth = hostSize.Width;
                renderedHeight = renderedWidth / sourceAspectRatio;
            }

            var renderedRect = new Rect(
                (hostSize.Width - renderedWidth) / 2d,
                (hostSize.Height - renderedHeight) / 2d,
                renderedWidth,
                renderedHeight);
            return new FrameSurfaceGeometry(renderedRect, sourcePixelWidth, sourcePixelHeight);
        }

        public bool TryMapPointToSourcePixel(Point hostPoint, out int sourcePixelX, out int sourcePixelY)
        {
            sourcePixelX = 0;
            sourcePixelY = 0;
            if (!HasVisibleFrame || !RenderedRect.Contains(hostPoint))
            {
                return false;
            }

            var normalizedX = (hostPoint.X - RenderedRect.Left) / RenderedRect.Width;
            var normalizedY = (hostPoint.Y - RenderedRect.Top) / RenderedRect.Height;
            sourcePixelX = ClampToNearestPixel(normalizedX, SourcePixelWidth);
            sourcePixelY = ClampToNearestPixel(normalizedY, SourcePixelHeight);
            return true;
        }

        private static int ClampToNearestPixel(double normalizedValue, int pixelCount)
        {
            if (pixelCount <= 1)
            {
                return 0;
            }

            var scaled = normalizedValue * (pixelCount - 1);
            var rounded = (int)Math.Round(scaled, MidpointRounding.AwayFromZero);
            return Math.Max(0, Math.Min(pixelCount - 1, rounded));
        }
    }
}
