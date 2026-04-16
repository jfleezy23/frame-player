using System;
using FramePlayer.Core.Models;

namespace FramePlayer.Core.Hosting
{
    public sealed class PaneViewState
    {
        public PaneViewState()
        {
            PaneId = string.Empty;
            DisplayLabel = string.Empty;
            CurrentFilePath = string.Empty;
            CurrentPosition = TimeSpan.Zero;
            Duration = TimeSpan.Zero;
            Loop = LoopCommandState.Empty;
        }

        public string PaneId { get; set; }

        public string DisplayLabel { get; set; }

        public bool IsPrimary { get; set; }

        public bool IsActive { get; set; }

        public bool IsFocused { get; set; }

        public bool IsMediaOpen { get; set; }

        public string CurrentFilePath { get; set; }

        public ReviewPlaybackState PlaybackState { get; set; }

        public TimeSpan CurrentPosition { get; set; }

        public TimeSpan Duration { get; set; }

        public long? FrameIndex { get; set; }

        public bool IsFrameIndexAbsolute { get; set; }

        public LoopCommandState Loop { get; set; }
    }
}
