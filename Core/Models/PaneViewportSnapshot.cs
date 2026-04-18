using System;
using System.Diagnostics.CodeAnalysis;

namespace FramePlayer.Core.Models
{
    public sealed class PaneViewportSnapshot
    {
        private const double DefaultCenter = 0.5d;

        [SuppressMessage(
            "Major Code Smell",
            "S107:Methods should not have too many parameters",
            Justification = "Viewport snapshots intentionally keep explicit scalar crop and center fields so hot-path callers do not build extra transport objects.")]
        public PaneViewportSnapshot(
            double zoomFactor,
            double normalizedCenterX,
            double normalizedCenterY,
            int sourcePixelWidth,
            int sourcePixelHeight,
            int sourceCropX,
            int sourceCropY,
            int sourceCropWidth,
            int sourceCropHeight)
        {
            ZoomFactor = !double.IsFinite(zoomFactor) || zoomFactor < 1d
                ? 1d
                : zoomFactor;
            NormalizedCenterX = ClampNormalizedValue(normalizedCenterX);
            NormalizedCenterY = ClampNormalizedValue(normalizedCenterY);
            SourcePixelWidth = Math.Max(1, sourcePixelWidth);
            SourcePixelHeight = Math.Max(1, sourcePixelHeight);

            SourceCropX = Math.Max(0, Math.Min(SourcePixelWidth - 1, sourceCropX));
            SourceCropY = Math.Max(0, Math.Min(SourcePixelHeight - 1, sourceCropY));

            var maxCropWidth = Math.Max(1, SourcePixelWidth - SourceCropX);
            var maxCropHeight = Math.Max(1, SourcePixelHeight - SourceCropY);
            SourceCropWidth = Math.Max(1, Math.Min(maxCropWidth, sourceCropWidth));
            SourceCropHeight = Math.Max(1, Math.Min(maxCropHeight, sourceCropHeight));
        }

        public double ZoomFactor { get; }

        public double NormalizedCenterX { get; }

        public double NormalizedCenterY { get; }

        public int SourcePixelWidth { get; }

        public int SourcePixelHeight { get; }

        public int SourceCropX { get; }

        public int SourceCropY { get; }

        public int SourceCropWidth { get; }

        public int SourceCropHeight { get; }

        public bool IsZoomed
        {
            get
            {
                return ZoomFactor > 1.0001d ||
                       SourceCropX > 0 ||
                       SourceCropY > 0 ||
                       SourceCropWidth < SourcePixelWidth ||
                       SourceCropHeight < SourcePixelHeight;
            }
        }

        public static PaneViewportSnapshot CreateFullFrame(int sourcePixelWidth, int sourcePixelHeight)
        {
            var resolvedSourceWidth = Math.Max(1, sourcePixelWidth);
            var resolvedSourceHeight = Math.Max(1, sourcePixelHeight);
            return new PaneViewportSnapshot(
                1d,
                DefaultCenter,
                DefaultCenter,
                resolvedSourceWidth,
                resolvedSourceHeight,
                0,
                0,
                resolvedSourceWidth,
                resolvedSourceHeight);
        }

        private static double ClampNormalizedValue(double value)
        {
            if (!double.IsFinite(value))
            {
                return DefaultCenter;
            }

            return Math.Max(0d, Math.Min(1d, value));
        }
    }
}
