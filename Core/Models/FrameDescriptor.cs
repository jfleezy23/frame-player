using System;

namespace FramePlayer.Core.Models
{
    public sealed class FrameDescriptor
    {
        public FrameDescriptor(
            long? frameIndex,
            TimeSpan presentationTime,
            bool isKeyFrame,
            bool isFrameIndexAbsolute,
            int pixelWidth,
            int pixelHeight,
            string pixelFormatName,
            string sourcePixelFormatName,
            long? presentationTimestamp,
            long? decodeTimestamp,
            long? durationTimestamp)
        {
            FrameIndex = frameIndex;
            PresentationTime = presentationTime;
            IsKeyFrame = isKeyFrame;
            IsFrameIndexAbsolute = isFrameIndexAbsolute;
            PixelWidth = pixelWidth;
            PixelHeight = pixelHeight;
            PixelFormatName = pixelFormatName ?? string.Empty;
            SourcePixelFormatName = sourcePixelFormatName ?? string.Empty;
            PresentationTimestamp = presentationTimestamp;
            DecodeTimestamp = decodeTimestamp;
            DurationTimestamp = durationTimestamp;
        }

        public long? FrameIndex { get; }

        public TimeSpan PresentationTime { get; }

        public bool IsKeyFrame { get; }

        public bool IsFrameIndexAbsolute { get; }

        public int PixelWidth { get; }

        public int PixelHeight { get; }

        public string PixelFormatName { get; }

        public string SourcePixelFormatName { get; }

        public long? PresentationTimestamp { get; }

        public long? DecodeTimestamp { get; }

        public long? DurationTimestamp { get; }
    }
}
