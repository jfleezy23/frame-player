using System;

namespace FramePlayer.Core.Models
{
    public sealed class ReviewPosition
    {
        public static ReviewPosition Empty { get; } = new ReviewPosition(TimeSpan.Zero, null, false, false, null, null);

        public ReviewPosition(
            TimeSpan presentationTime,
            long? frameIndex,
            bool isFrameAccurate,
            bool isFrameIndexAbsolute,
            long? presentationTimestamp,
            long? decodeTimestamp)
        {
            PresentationTime = presentationTime;
            FrameIndex = frameIndex;
            IsFrameAccurate = isFrameAccurate;
            IsFrameIndexAbsolute = isFrameIndexAbsolute;
            PresentationTimestamp = presentationTimestamp;
            DecodeTimestamp = decodeTimestamp;
        }

        public TimeSpan PresentationTime { get; }

        public long? FrameIndex { get; }

        public bool IsFrameAccurate { get; }

        public bool IsFrameIndexAbsolute { get; }

        public long? PresentationTimestamp { get; }

        public long? DecodeTimestamp { get; }
    }
}
