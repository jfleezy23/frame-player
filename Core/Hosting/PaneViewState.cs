using System;
using FramePlayer.Core.Models;

namespace FramePlayer.Core.Hosting
{
    public sealed class PaneViewState
    {
        public PaneViewState(
            string paneId,
            string displayLabel,
            bool isPrimary,
            bool isActive,
            bool isFocused,
            bool isMediaOpen,
            string currentFilePath,
            ReviewPlaybackState playbackState,
            TimeSpan currentPosition,
            TimeSpan duration,
            long? frameIndex,
            bool isFrameIndexAbsolute,
            LoopCommandState loop)
        {
            PaneId = paneId ?? string.Empty;
            DisplayLabel = displayLabel ?? string.Empty;
            IsPrimary = isPrimary;
            IsActive = isActive;
            IsFocused = isFocused;
            IsMediaOpen = isMediaOpen;
            CurrentFilePath = currentFilePath ?? string.Empty;
            PlaybackState = playbackState;
            CurrentPosition = currentPosition < TimeSpan.Zero ? TimeSpan.Zero : currentPosition;
            Duration = duration > TimeSpan.Zero ? duration : TimeSpan.Zero;
            FrameIndex = frameIndex;
            IsFrameIndexAbsolute = isFrameIndexAbsolute;
            Loop = loop ?? LoopCommandState.Empty;
        }

        public string PaneId { get; }

        public string DisplayLabel { get; }

        public bool IsPrimary { get; }

        public bool IsActive { get; }

        public bool IsFocused { get; }

        public bool IsMediaOpen { get; }

        public string CurrentFilePath { get; }

        public ReviewPlaybackState PlaybackState { get; }

        public TimeSpan CurrentPosition { get; }

        public TimeSpan Duration { get; }

        public long? FrameIndex { get; }

        public bool IsFrameIndexAbsolute { get; }

        public LoopCommandState Loop { get; }
    }
}
