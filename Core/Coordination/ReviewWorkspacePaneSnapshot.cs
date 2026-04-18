using System;
using System.Diagnostics.CodeAnalysis;
using FramePlayer.Core.Models;

namespace FramePlayer.Core.Coordination
{
    public sealed class ReviewWorkspacePaneSnapshot
    {
        [SuppressMessage(
            "Major Code Smell",
            "S107:Methods should not have too many parameters",
            Justification = "Workspace pane snapshots intentionally keep explicit scalar fields and position state so frame review coordination remains easy to inspect.")]
        public ReviewWorkspacePaneSnapshot(
            string paneId,
            string sessionId,
            string displayLabel,
            bool isBound,
            bool isPrimary,
            bool isFocused,
            bool isActive,
            TimeSpan timelineOffset,
            ReviewPlaybackState playbackState,
            string currentFilePath,
            ReviewPosition position,
            LoopPlaybackPaneRangeSnapshot loopRange)
        {
            PaneId = paneId ?? string.Empty;
            SessionId = sessionId ?? string.Empty;
            DisplayLabel = displayLabel ?? string.Empty;
            IsBound = isBound;
            IsPrimary = isPrimary;
            IsFocused = isFocused;
            IsActive = isActive;
            TimelineOffset = timelineOffset;
            PlaybackState = playbackState;
            CurrentFilePath = currentFilePath ?? string.Empty;
            Position = position ?? ReviewPosition.Empty;
            LoopRange = loopRange;
        }

        public string PaneId { get; }

        public string SessionId { get; }

        public string DisplayLabel { get; }

        public bool IsBound { get; }

        public bool IsPrimary { get; }

        public bool IsFocused { get; }

        public bool IsActive { get; }

        public TimeSpan TimelineOffset { get; }

        public ReviewPlaybackState PlaybackState { get; }

        public string CurrentFilePath { get; }

        public ReviewPosition Position { get; }

        public LoopPlaybackPaneRangeSnapshot LoopRange { get; }

        public TimeSpan PresentationTime
        {
            get { return Position.PresentationTime; }
        }

        public long? FrameIndex
        {
            get { return Position.FrameIndex; }
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
    }
}
