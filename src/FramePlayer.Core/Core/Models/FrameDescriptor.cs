using System;
using System.Diagnostics.CodeAnalysis;

namespace FramePlayer.Core.Models
{
    /// <summary>
    /// Immutable metadata for a decoded display-order frame.
    /// </summary>
    /// <remarks>
    /// The frame index can be provisional while an index is still being built. Callers that
    /// display or persist frame numbers must check <see cref="IsFrameIndexAbsolute"/> before
    /// treating <see cref="FrameIndex"/> as a file-global frame identity.
    /// </remarks>
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

        /// <summary>
        /// Gets the zero-based frame index. This is file-global only when <see cref="IsFrameIndexAbsolute"/> is true.
        /// </summary>
        public long? FrameIndex { get; }

        /// <summary>
        /// Gets the presentation time reported for the decoded frame.
        /// </summary>
        public TimeSpan PresentationTime { get; }

        /// <summary>
        /// Gets whether FFmpeg marked this frame as a key frame.
        /// </summary>
        public bool IsKeyFrame { get; }

        /// <summary>
        /// Gets whether <see cref="FrameIndex"/> is proven against the file-global display-order index.
        /// </summary>
        public bool IsFrameIndexAbsolute { get; }

        /// <summary>
        /// Gets the decoded buffer width before display-crop adjustment.
        /// </summary>
        public int PixelWidth { get; }

        /// <summary>
        /// Gets the decoded buffer height before display-crop adjustment.
        /// </summary>
        public int PixelHeight { get; }

        /// <summary>
        /// Gets the neutral pixel format exposed to the shell.
        /// </summary>
        public string PixelFormatName { get; }

        /// <summary>
        /// Gets the source pixel format reported by the decoder before conversion.
        /// </summary>
        public string SourcePixelFormatName { get; }

        /// <summary>
        /// Gets the raw stream presentation timestamp when FFmpeg exposes one.
        /// </summary>
        public long? PresentationTimestamp { get; }

        /// <summary>
        /// Gets the raw stream decode timestamp when FFmpeg exposes one.
        /// </summary>
        public long? DecodeTimestamp { get; }

        /// <summary>
        /// Gets the raw stream frame duration timestamp when FFmpeg exposes one.
        /// </summary>
        public long? DurationTimestamp { get; }

        /// <summary>
        /// Gets the visible display width after any safe crop adjustment.
        /// </summary>
        public int DisplayWidth { get; }

        /// <summary>
        /// Gets the visible display height after any safe crop adjustment.
        /// </summary>
        public int DisplayHeight { get; }
    }
}
