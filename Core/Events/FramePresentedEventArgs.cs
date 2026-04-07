using System;
using FramePlayer.Core.Models;

namespace FramePlayer.Core.Events
{
    public sealed class FramePresentedEventArgs : EventArgs
    {
        public FramePresentedEventArgs(DecodedVideoFrame frame)
        {
            Frame = frame ?? throw new ArgumentNullException(nameof(frame));
        }

        public DecodedVideoFrame Frame { get; }
    }
}
