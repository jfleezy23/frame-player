namespace FramePlayer.Core.Hosting
{
    public sealed class ReviewHostCapabilities
    {
        public static ReviewHostCapabilities Default { get; } =
            new ReviewHostCapabilities(
                supportsTimedPlayback: true,
                hasBundledRuntime: true,
                exportToolingAvailable: false,
                idleStatusText: "Ready for review playback.",
                runtimeMissingStatusText: "Bundled playback runtime is missing.",
                timedPlaybackCapabilityText: "Timed playback is unavailable in this host.",
                exportToolingStatusText: "Clip export tooling is unavailable.");

        public ReviewHostCapabilities(
            bool supportsTimedPlayback,
            bool hasBundledRuntime,
            bool exportToolingAvailable,
            string idleStatusText,
            string runtimeMissingStatusText,
            string timedPlaybackCapabilityText,
            string exportToolingStatusText)
        {
            SupportsTimedPlayback = supportsTimedPlayback;
            HasBundledRuntime = hasBundledRuntime;
            ExportToolingAvailable = exportToolingAvailable;
            IdleStatusText = idleStatusText ?? string.Empty;
            RuntimeMissingStatusText = runtimeMissingStatusText ?? string.Empty;
            TimedPlaybackCapabilityText = timedPlaybackCapabilityText ?? string.Empty;
            ExportToolingStatusText = exportToolingStatusText ?? string.Empty;
        }

        public bool SupportsTimedPlayback { get; }

        public bool HasBundledRuntime { get; }

        public bool ExportToolingAvailable { get; }

        public string IdleStatusText { get; }

        public string RuntimeMissingStatusText { get; }

        public string TimedPlaybackCapabilityText { get; }

        public string ExportToolingStatusText { get; }
    }
}
