namespace FramePlayer.Core.Models
{
    // Neutral single-session state snapshot that a future non-WPF shell can consume.
public sealed class ReviewSessionSnapshot
{
        public static ReviewSessionSnapshot Empty { get; } =
            new ReviewSessionSnapshot(
                "primary",
                "Primary",
                ReviewPlaybackState.Closed,
                string.Empty,
                VideoMediaInfo.Empty,
                ReviewPosition.Empty);

        public ReviewSessionSnapshot(
            string sessionId,
            string displayLabel,
            ReviewPlaybackState playbackState,
            string currentFilePath,
            VideoMediaInfo mediaInfo,
            ReviewPosition position)
        {
            SessionId = string.IsNullOrWhiteSpace(sessionId) ? "primary" : sessionId;
            DisplayLabel = displayLabel ?? string.Empty;
            PlaybackState = playbackState;
            CurrentFilePath = currentFilePath ?? string.Empty;
            MediaInfo = mediaInfo ?? VideoMediaInfo.Empty;
            Position = position ?? ReviewPosition.Empty;
        }

        public string SessionId { get; }

        public string DisplayLabel { get; }

        public ReviewPlaybackState PlaybackState { get; }

        public string CurrentFilePath { get; }

        public VideoMediaInfo MediaInfo { get; }

        public ReviewPosition Position { get; }

        public bool IsMediaOpen
        {
            get
            {
                return PlaybackState != ReviewPlaybackState.Closed &&
                       !string.IsNullOrWhiteSpace(CurrentFilePath);
            }
        }

        public bool HasAbsoluteFrameIdentity
        {
            get
            {
                return Position.FrameIndex.HasValue &&
                       Position.IsFrameAccurate &&
                       Position.IsFrameIndexAbsolute;
            }
        }

        public static ReviewPlaybackState FromEngineState(bool isMediaOpen, bool isPlaying)
        {
            if (!isMediaOpen)
            {
                return ReviewPlaybackState.Closed;
            }

            return isPlaying
                ? ReviewPlaybackState.Playing
                : ReviewPlaybackState.Paused;
        }
    }
}
