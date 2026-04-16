namespace FramePlayer.Core.Hosting
{
    public sealed class LoopCommandState
    {
        public static LoopCommandState Empty { get; } =
            new LoopCommandState(false, false, false, false, false, false, "Loop: off", "No loop markers are active.");

        public LoopCommandState(
            bool canSetMarkers,
            bool canClearMarkers,
            bool hasAnyMarkers,
            bool hasReadyRange,
            bool hasPendingMarkers,
            bool isInvalidRange,
            string statusText,
            string toolTip)
        {
            CanSetMarkers = canSetMarkers;
            CanClearMarkers = canClearMarkers;
            HasAnyMarkers = hasAnyMarkers;
            HasReadyRange = hasReadyRange;
            HasPendingMarkers = hasPendingMarkers;
            IsInvalidRange = isInvalidRange;
            StatusText = statusText ?? string.Empty;
            ToolTip = toolTip ?? string.Empty;
        }

        public bool CanSetMarkers { get; }

        public bool CanClearMarkers { get; }

        public bool HasAnyMarkers { get; }

        public bool HasReadyRange { get; }

        public bool HasPendingMarkers { get; }

        public bool IsInvalidRange { get; }

        public string StatusText { get; }

        public string ToolTip { get; }
    }
}
