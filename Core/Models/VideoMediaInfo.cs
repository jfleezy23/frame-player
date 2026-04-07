using System;

namespace FramePlayer.Core.Models
{
    public sealed class VideoMediaInfo
    {
        public static VideoMediaInfo Empty { get; } =
            new VideoMediaInfo(string.Empty, TimeSpan.Zero, TimeSpan.Zero, 0d, 0, 0, string.Empty, -1, 0, 0, 0, 0);

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
            int audioChannelCount = 0)
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
    }
}
