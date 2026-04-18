using System;
using System.Windows;
using FramePlayer.Core.Models;

namespace FramePlayer.Services
{
    internal readonly struct FrameSurfaceGeometry
    {
        private FrameSurfaceGeometry(
            Rect renderedRect,
            Rect visibleRect,
            int sourcePixelWidth,
            int sourcePixelHeight,
            double zoomFactor,
            Point normalizedCenter)
        {
            RenderedRect = renderedRect;
            VisibleRect = visibleRect;
            SourcePixelWidth = sourcePixelWidth;
            SourcePixelHeight = sourcePixelHeight;
            ZoomFactor = zoomFactor;
            NormalizedCenter = normalizedCenter;
        }

        public Rect RenderedRect { get; }

        public Rect VisibleRect { get; }

        public int SourcePixelWidth { get; }

        public int SourcePixelHeight { get; }

        public double ZoomFactor { get; }

        public Point NormalizedCenter { get; }

        public bool HasVisibleFrame
        {
            get
            {
                return SourcePixelWidth > 0 &&
                       SourcePixelHeight > 0 &&
                       RenderedRect.Width > 0d &&
                       RenderedRect.Height > 0d &&
                       VisibleRect.Width > 0d &&
                       VisibleRect.Height > 0d;
            }
        }

        public static FrameSurfaceGeometry Empty { get; } =
            new FrameSurfaceGeometry(Rect.Empty, Rect.Empty, 0, 0, 1d, new Point(0.5d, 0.5d));

        public static FrameSurfaceGeometry Create(
            Size hostSize,
            int sourcePixelWidth,
            int sourcePixelHeight,
            double displayWidth,
            double displayHeight,
            double zoomFactor,
            Point normalizedCenter)
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

            var baseRenderedRect = new Rect(
                (hostSize.Width - renderedWidth) / 2d,
                (hostSize.Height - renderedHeight) / 2d,
                renderedWidth,
                renderedHeight);
            var resolvedZoomFactor = !double.IsFinite(zoomFactor) || zoomFactor < 1d
                ? 1d
                : zoomFactor;
            var resolvedRenderedWidth = renderedWidth * resolvedZoomFactor;
            var resolvedRenderedHeight = renderedHeight * resolvedZoomFactor;
            var hostRect = new Rect(new Point(0d, 0d), hostSize);
            var resolvedCenter = ClampNormalizedCenter(
                hostRect,
                resolvedRenderedWidth,
                resolvedRenderedHeight,
                normalizedCenter);
            var hostCenterX = hostRect.Width / 2d;
            var hostCenterY = hostRect.Height / 2d;
            var renderedRect = new Rect(
                hostCenterX - (resolvedCenter.X * resolvedRenderedWidth),
                hostCenterY - (resolvedCenter.Y * resolvedRenderedHeight),
                resolvedRenderedWidth,
                resolvedRenderedHeight);
            var visibleRect = Rect.Intersect(renderedRect, hostRect);
            if (visibleRect.IsEmpty)
            {
                visibleRect = Rect.Empty;
            }

            if (resolvedZoomFactor <= 1d)
            {
                renderedRect = baseRenderedRect;
                visibleRect = baseRenderedRect;
                resolvedCenter = new Point(0.5d, 0.5d);
            }

            return new FrameSurfaceGeometry(
                renderedRect,
                visibleRect,
                sourcePixelWidth,
                sourcePixelHeight,
                resolvedZoomFactor,
                resolvedCenter);
        }

        public bool TryMapPointToSourcePixel(Point hostPoint, out int sourcePixelX, out int sourcePixelY)
        {
            sourcePixelX = 0;
            sourcePixelY = 0;
            if (!HasVisibleFrame || !VisibleRect.Contains(hostPoint))
            {
                return false;
            }

            var normalizedX = (hostPoint.X - RenderedRect.Left) / RenderedRect.Width;
            var normalizedY = (hostPoint.Y - RenderedRect.Top) / RenderedRect.Height;
            sourcePixelX = ClampToNearestPixel(normalizedX, SourcePixelWidth);
            sourcePixelY = ClampToNearestPixel(normalizedY, SourcePixelHeight);
            return true;
        }

        public PaneViewportSnapshot CreateViewportSnapshot()
        {
            if (!HasVisibleFrame)
            {
                return PaneViewportSnapshot.CreateFullFrame(SourcePixelWidth, SourcePixelHeight);
            }

            var normalizedLeft = ClampUnitInterval((VisibleRect.Left - RenderedRect.Left) / RenderedRect.Width);
            var normalizedTop = ClampUnitInterval((VisibleRect.Top - RenderedRect.Top) / RenderedRect.Height);
            var normalizedRight = ClampUnitInterval((VisibleRect.Right - RenderedRect.Left) / RenderedRect.Width);
            var normalizedBottom = ClampUnitInterval((VisibleRect.Bottom - RenderedRect.Top) / RenderedRect.Height);
            var cropX = ClampToPixelFloor(normalizedLeft, SourcePixelWidth);
            var cropY = ClampToPixelFloor(normalizedTop, SourcePixelHeight);
            var cropRight = ClampToPixelCeiling(normalizedRight, SourcePixelWidth);
            var cropBottom = ClampToPixelCeiling(normalizedBottom, SourcePixelHeight);
            return new PaneViewportSnapshot(
                ZoomFactor,
                NormalizedCenter.X,
                NormalizedCenter.Y,
                SourcePixelWidth,
                SourcePixelHeight,
                cropX,
                cropY,
                Math.Max(1, cropRight - cropX),
                Math.Max(1, cropBottom - cropY));
        }

        private static Point ClampNormalizedCenter(
            Rect hostRect,
            double renderedWidth,
            double renderedHeight,
            Point normalizedCenter)
        {
            var resolvedCenterX = ClampNormalizedAxis(
                normalizedCenter.X,
                hostRect.Width,
                renderedWidth);
            var resolvedCenterY = ClampNormalizedAxis(
                normalizedCenter.Y,
                hostRect.Height,
                renderedHeight);
            return new Point(resolvedCenterX, resolvedCenterY);
        }

        private static double ClampNormalizedAxis(double requestedCenter, double hostLength, double renderedLength)
        {
            if (!double.IsFinite(requestedCenter))
            {
                requestedCenter = 0.5d;
            }

            if (hostLength <= 0d || renderedLength <= hostLength)
            {
                return 0.5d;
            }

            var minimumCenter = hostLength / (2d * renderedLength);
            var maximumCenter = 1d - minimumCenter;
            return Math.Max(minimumCenter, Math.Min(maximumCenter, requestedCenter));
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

        private static int ClampToPixelFloor(double normalizedValue, int pixelCount)
        {
            if (pixelCount <= 1)
            {
                return 0;
            }

            var scaled = (int)Math.Floor(ClampUnitInterval(normalizedValue) * pixelCount);
            return Math.Max(0, Math.Min(pixelCount - 1, scaled));
        }

        private static int ClampToPixelCeiling(double normalizedValue, int pixelCount)
        {
            if (pixelCount <= 1)
            {
                return 1;
            }

            var scaled = (int)Math.Ceiling(ClampUnitInterval(normalizedValue) * pixelCount);
            return Math.Max(1, Math.Min(pixelCount, scaled));
        }

        private static double ClampUnitInterval(double value)
        {
            if (!double.IsFinite(value))
            {
                return 0d;
            }

            return Math.Max(0d, Math.Min(1d, value));
        }
    }
}
