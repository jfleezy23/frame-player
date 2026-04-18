using FramePlayer.Engines.FFmpeg;

namespace FramePlayer.Core.Models
{
    public sealed class CompareSideBySideExportRequest
    {
        private string _outputFilePath = string.Empty;
        private ReviewSessionSnapshot _primarySessionSnapshot = ReviewSessionSnapshot.Empty;
        private ReviewSessionSnapshot _compareSessionSnapshot = ReviewSessionSnapshot.Empty;

        public string OutputFilePath
        {
            get { return _outputFilePath; }
            init { _outputFilePath = value ?? string.Empty; }
        }

        public CompareSideBySideExportMode Mode { get; init; }

        public CompareSideBySideExportAudioSource AudioSource { get; init; }

        public ReviewSessionSnapshot PrimarySessionSnapshot
        {
            get { return _primarySessionSnapshot; }
            init { _primarySessionSnapshot = value ?? ReviewSessionSnapshot.Empty; }
        }

        public ReviewSessionSnapshot CompareSessionSnapshot
        {
            get { return _compareSessionSnapshot; }
            init { _compareSessionSnapshot = value ?? ReviewSessionSnapshot.Empty; }
        }

        public LoopPlaybackPaneRangeSnapshot PrimaryLoopRange { get; init; }

        public LoopPlaybackPaneRangeSnapshot CompareLoopRange { get; init; }

        public FfmpegReviewEngine PrimaryEngine { get; init; }

        public FfmpegReviewEngine CompareEngine { get; init; }
    }
}
