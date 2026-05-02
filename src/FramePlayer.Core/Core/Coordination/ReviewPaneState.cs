using System;
using System.Diagnostics.CodeAnalysis;
using FramePlayer.Core.Models;

namespace FramePlayer.Core.Coordination
{
    public sealed class ReviewPaneState
    {
        [SuppressMessage(
            "Major Code Smell",
            "S107:Methods should not have too many parameters",
            Justification = "Review pane state is an immutable coordination snapshot with explicit scalar fields for predictable pane orchestration and diagnostics.")]
        public ReviewPaneState(
            string paneId,
            string displayLabel,
            string sessionId,
            ReviewSessionSnapshot session,
            TimeSpan timelineOffset,
            bool isFocused,
            bool isActive,
            bool isPrimary,
            LoopPlaybackPaneRangeSnapshot loopRange = null)
        {
            PaneId = string.IsNullOrWhiteSpace(paneId) ? "primary" : paneId;
            DisplayLabel = displayLabel ?? string.Empty;
            Session = session ?? throw new ArgumentNullException(nameof(session));
            SessionId = string.IsNullOrWhiteSpace(sessionId)
                ? Session.SessionId
                : sessionId;
            TimelineOffset = timelineOffset;
            IsFocused = isFocused;
            IsActive = isActive;
            IsPrimary = isPrimary;
            LoopRange = loopRange;
        }

        public string PaneId { get; }

        public string DisplayLabel { get; }

        public string SessionId { get; }

        public ReviewSessionSnapshot Session { get; }

        public TimeSpan TimelineOffset { get; }

        public bool IsFocused { get; }

        public bool IsActive { get; }

        public bool IsPrimary { get; }

        public LoopPlaybackPaneRangeSnapshot LoopRange { get; }
    }
}
