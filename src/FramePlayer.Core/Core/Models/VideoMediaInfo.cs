using System;
using System.Diagnostics.CodeAnalysis;

namespace FramePlayer.Core.Models
{
    public sealed class VideoMediaInfo
    {
        public static VideoMediaInfo Empty { get; } =
            new VideoMediaInfo(string.Empty, TimeSpan.Zero, TimeSpan.Zero, 0d, 0, 0, string.Empty, -1, 0, 0, 0, 0);

        [SuppressMessage(
            "Major Code Smell",
            "S107:Methods should not have too many parameters",
            Justification = "Video media info is an immutable FFmpeg metadata snapshot; splitting it into builder layers would widen churn across decode and inspection call sites without a frame-first payoff.")]
        public VideoMediaInfo(
            string filePath,
            TimeSpan duration,
            TimeSpan positionStep,
            double framesPerSecond,
            int pixelWidth,
            int pixelHeight,
            string videoCodecName,
            int videoStreamIndex,
            int nominalFrameRateNumerator,
            int nominalFrameRateDenominator,
            int streamTimeBaseNumerator,
            int streamTimeBaseDenominator,
            bool hasAudioStream = false,
            bool isAudioPlaybackAvailable = false,
            string audioCodecName = "",
            int audioStreamIndex = -1,
            int audioSampleRate = 0,
            int audioChannelCount = 0,
            int? displayWidth = null,
            int? displayHeight = null,
            int? displayAspectRatioNumerator = null,
            int? displayAspectRatioDenominator = null,
            string sourcePixelFormatName = null,
            int? videoBitDepth = null,
            long? videoBitRate = null,
            string videoColorSpace = null,
            string videoColorRange = null,
            string videoColorPrimaries = null,
            string videoColorTransfer = null,
            long? audioBitRate = null,
            int? audioBitDepth = null)
        {
            FilePath = filePath ?? string.Empty;
            Duration = duration;
            PositionStep = positionStep;
            FramesPerSecond = framesPerSecond;
            PixelWidth = pixelWidth;
            PixelHeight = pixelHeight;
            VideoCodecName = videoCodecName ?? string.Empty;
            VideoStreamIndex = videoStreamIndex;
            NominalFrameRateNumerator = nominalFrameRateNumerator;
            NominalFrameRateDenominator = nominalFrameRateDenominator;
            StreamTimeBaseNumerator = streamTimeBaseNumerator;
            StreamTimeBaseDenominator = streamTimeBaseDenominator;
            HasAudioStream = hasAudioStream;
            IsAudioPlaybackAvailable = isAudioPlaybackAvailable;
            AudioCodecName = audioCodecName ?? string.Empty;
            AudioStreamIndex = audioStreamIndex;
            AudioSampleRate = audioSampleRate;
            AudioChannelCount = audioChannelCount;
            DisplayWidth = displayWidth;
            DisplayHeight = displayHeight;
            DisplayAspectRatioNumerator = displayAspectRatioNumerator;
            DisplayAspectRatioDenominator = displayAspectRatioDenominator;
            SourcePixelFormatName = sourcePixelFormatName;
            VideoBitDepth = videoBitDepth;
            VideoBitRate = videoBitRate;
            VideoColorSpace = videoColorSpace;
            VideoColorRange = videoColorRange;
            VideoColorPrimaries = videoColorPrimaries;
            VideoColorTransfer = videoColorTransfer;
            AudioBitRate = audioBitRate;
            AudioBitDepth = audioBitDepth;
        }

        public string FilePath { get; }

        public TimeSpan Duration { get; }

        public TimeSpan PositionStep { get; }

        public double FramesPerSecond { get; }

        public int PixelWidth { get; }

        public int PixelHeight { get; }

        public string VideoCodecName { get; }

        public int VideoStreamIndex { get; }

        public int NominalFrameRateNumerator { get; }

        public int NominalFrameRateDenominator { get; }

        public int StreamTimeBaseNumerator { get; }

        public int StreamTimeBaseDenominator { get; }

        public bool HasAudioStream { get; }

        public bool IsAudioPlaybackAvailable { get; }

        public string AudioCodecName { get; }

        public int AudioStreamIndex { get; }

        public int AudioSampleRate { get; }

        public int AudioChannelCount { get; }

        public int? DisplayWidth { get; }

        public int? DisplayHeight { get; }

        public int? DisplayAspectRatioNumerator { get; }

        public int? DisplayAspectRatioDenominator { get; }

        public string SourcePixelFormatName { get; }

        public int? VideoBitDepth { get; }

        public long? VideoBitRate { get; }

        public string VideoColorSpace { get; }

        public string VideoColorRange { get; }

        public string VideoColorPrimaries { get; }

        public string VideoColorTransfer { get; }

        public long? AudioBitRate { get; }

        public int? AudioBitDepth { get; }
    }
}
