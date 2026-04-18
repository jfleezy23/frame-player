using FramePlayer.Engines.FFmpeg;

namespace FramePlayer.Core.Models
{
    public sealed class CompareSideBySideExportRequest
    {
        public CompareSideBySideExportRequest(
            string outputFilePath,
            CompareSideBySideExportMode mode,
            CompareSideBySideExportAudioSource audioSource,
            ReviewSessionSnapshot primarySessionSnapshot,
            ReviewSessionSnapshot compareSessionSnapshot,
            LoopPlaybackPaneRangeSnapshot primaryLoopRange,
            LoopPlaybackPaneRangeSnapshot compareLoopRange,
            FfmpegReviewEngine primaryEngine,
            FfmpegReviewEngine compareEngine)
        {
            OutputFilePath = outputFilePath ?? string.Empty;
            Mode = mode;
            AudioSource = audioSource;
            PrimarySessionSnapshot = primarySessionSnapshot ?? ReviewSessionSnapshot.Empty;
            CompareSessionSnapshot = compareSessionSnapshot ?? ReviewSessionSnapshot.Empty;
            PrimaryLoopRange = primaryLoopRange;
            CompareLoopRange = compareLoopRange;
            PrimaryEngine = primaryEngine;
            CompareEngine = compareEngine;
        }

        public string OutputFilePath { get; }

        public CompareSideBySideExportMode Mode { get; }

        public CompareSideBySideExportAudioSource AudioSource { get; }

        public ReviewSessionSnapshot PrimarySessionSnapshot { get; }

        public ReviewSessionSnapshot CompareSessionSnapshot { get; }

        public LoopPlaybackPaneRangeSnapshot PrimaryLoopRange { get; }

        public LoopPlaybackPaneRangeSnapshot CompareLoopRange { get; }

        public FfmpegReviewEngine PrimaryEngine { get; }

        public FfmpegReviewEngine CompareEngine { get; }
    }
}
