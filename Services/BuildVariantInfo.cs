using System;
using System.Windows.Media;

namespace FramePlayer.Services
{
    internal enum ForcedBackend
    {
        CustomFfmpeg
    }

    internal sealed class BuildVariantInfo
    {
        public static BuildVariantInfo Current { get; } = CreateCurrent();

        private BuildVariantInfo(
            bool isComparisonBuild,
            string buildDisplayName,
            string badgeText,
            string statusText,
            string playbackCapabilityText,
            ForcedBackend forcedBackend,
            bool supportsTimedPlayback,
            Color accentColor,
            Color accentBorderColor)
        {
            IsComparisonBuild = isComparisonBuild;
            BuildDisplayName = buildDisplayName ?? string.Empty;
            BadgeText = badgeText ?? string.Empty;
            StatusText = statusText ?? string.Empty;
            PlaybackCapabilityText = playbackCapabilityText ?? string.Empty;
            ForcedBackend = forcedBackend;
            SupportsTimedPlayback = supportsTimedPlayback;
            AccentColor = accentColor;
            AccentBorderColor = accentBorderColor;
        }

        public bool IsComparisonBuild { get; }

        public string BuildDisplayName { get; }

        public string BadgeText { get; }

        public string StatusText { get; }

        public string PlaybackCapabilityText { get; }

        public ForcedBackend ForcedBackend { get; }

        public bool SupportsTimedPlayback { get; }

        public Color AccentColor { get; }

        public Color AccentBorderColor { get; }

        public static bool UsesCustomVisibleSurface => true;

        public static bool UsesZeroIndexedFrameDisplay => false;

        private static BuildVariantInfo CreateCurrent()
        {
            return new BuildVariantInfo(
                false,
                "Frame Player",
                "FRAME PLAYER",
                "A/V playback + frame review",
                "Video and audio playback are available in the custom FFmpeg engine; frame review remains decode/index based.",
                ForcedBackend.CustomFfmpeg,
                true,
                Color.FromRgb(0x5A, 0xA9, 0xE6),
                Color.FromRgb(0x5A, 0xA9, 0xE6));
        }
    }
}
