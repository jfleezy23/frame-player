using System;
using System.Diagnostics.CodeAnalysis;

namespace FramePlayer.Core.Models
{
    public sealed class FrameDescriptor
    {
        [SuppressMessage(
            "Major Code Smell",
            "S107:Methods should not have too many parameters",
            Justification = "Frame descriptors are immutable decode-path snapshots and keep explicit scalar metadata fields to avoid extra transport objects in frame review flows.")]
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
            long? durationTimestamp,
            int? displayWidth = null,
            int? displayHeight = null)
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
            DisplayWidth = displayWidth.GetValueOrDefault(pixelWidth);
            DisplayHeight = displayHeight.GetValueOrDefault(pixelHeight);
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

        public int DisplayWidth { get; }

        public int DisplayHeight { get; }
    }
}
