using System;
using FramePlayer.Core.Models;

namespace FramePlayer.Core.Events
{
    public sealed class FramePresentedEventArgs : EventArgs
    {
        public FramePresentedEventArgs(DecodedFrameBuffer frameBuffer)
        {
            FrameBuffer = frameBuffer ?? throw new ArgumentNullException(nameof(frameBuffer));
        }

        public DecodedFrameBuffer FrameBuffer { get; }

        public FrameDescriptor Descriptor
        {
            get { return FrameBuffer.Descriptor; }
        }
    }
}
