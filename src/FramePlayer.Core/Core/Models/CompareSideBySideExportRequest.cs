using FramePlayer.Engines.FFmpeg;

namespace FramePlayer.Core.Models
{
    public sealed class CompareSideBySideExportRequest
    {
        private string _outputFilePath = string.Empty;
        private ReviewSessionSnapshot _primarySessionSnapshot = ReviewSessionSnapshot.Empty;
        private ReviewSessionSnapshot _compareSessionSnapshot = ReviewSessionSnapshot.Empty;
        private PaneViewportSnapshot _primaryViewportSnapshot = PaneViewportSnapshot.CreateFullFrame(1, 1);
        private PaneViewportSnapshot _compareViewportSnapshot = PaneViewportSnapshot.CreateFullFrame(1, 1);

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

        public PaneViewportSnapshot PrimaryViewportSnapshot
        {
            get { return _primaryViewportSnapshot; }
            init
            {
                _primaryViewportSnapshot = value ?? PaneViewportSnapshot.CreateFullFrame(
                    PrimarySessionSnapshot.MediaInfo.PixelWidth,
                    PrimarySessionSnapshot.MediaInfo.PixelHeight);
            }
        }

        public PaneViewportSnapshot CompareViewportSnapshot
        {
            get { return _compareViewportSnapshot; }
            init
            {
                _compareViewportSnapshot = value ?? PaneViewportSnapshot.CreateFullFrame(
                    CompareSessionSnapshot.MediaInfo.PixelWidth,
                    CompareSessionSnapshot.MediaInfo.PixelHeight);
            }
        }

        public LoopPlaybackPaneRangeSnapshot PrimaryLoopRange { get; init; }

        public LoopPlaybackPaneRangeSnapshot CompareLoopRange { get; init; }

        public FfmpegReviewEngine PrimaryEngine { get; init; }

        public FfmpegReviewEngine CompareEngine { get; init; }
    }
}
