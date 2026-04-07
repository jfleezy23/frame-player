using System;
using FramePlayer.Core.Models;

namespace FramePlayer.Core.Events
{
    public sealed class VideoReviewEngineStateChangedEventArgs : EventArgs
    {
        public VideoReviewEngineStateChangedEventArgs(
            bool isMediaOpen,
            bool isPlaying,
            string currentFilePath,
            string lastErrorMessage,
            VideoMediaInfo mediaInfo,
            ReviewPosition position)
        {
            IsMediaOpen = isMediaOpen;
            IsPlaying = isPlaying;
            CurrentFilePath = currentFilePath ?? string.Empty;
            LastErrorMessage = lastErrorMessage ?? string.Empty;
            MediaInfo = mediaInfo ?? VideoMediaInfo.Empty;
            Position = position ?? ReviewPosition.Empty;
        }

        public bool IsMediaOpen { get; }

        public bool IsPlaying { get; }

        public string CurrentFilePath { get; }

        public string LastErrorMessage { get; }

        public VideoMediaInfo MediaInfo { get; }

        public ReviewPosition Position { get; }
    }
}
