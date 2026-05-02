using System;

namespace FramePlayer.Core.Models
{
    public sealed class CompareSideBySideExportPlan
    {
        private string _outputFilePath = string.Empty;
        private string _primarySourceFilePath = string.Empty;
        private string _compareSourceFilePath = string.Empty;
        private TimeSpan _primaryStartTime = TimeSpan.Zero;
        private TimeSpan _primaryContentDuration = TimeSpan.Zero;
        private TimeSpan _primaryLeadingPad = TimeSpan.Zero;
        private TimeSpan _primaryTrailingPad = TimeSpan.Zero;
        private TimeSpan _compareStartTime = TimeSpan.Zero;
        private TimeSpan _compareContentDuration = TimeSpan.Zero;
        private TimeSpan _compareLeadingPad = TimeSpan.Zero;
        private TimeSpan _compareTrailingPad = TimeSpan.Zero;
        private string _primaryEndBoundaryStrategy = string.Empty;
        private string _compareEndBoundaryStrategy = string.Empty;
        private TimeSpan _outputDuration = TimeSpan.Zero;
        private int _primaryRenderWidth = 1;
        private int _primaryRenderHeight = 1;
        private int _compareRenderWidth = 1;
        private int _compareRenderHeight = 1;
        private int _outputWidth = 1;
        private int _outputHeight = 1;
        private PaneViewportSnapshot _primaryViewportSnapshot = PaneViewportSnapshot.CreateFullFrame(1, 1);
        private PaneViewportSnapshot _compareViewportSnapshot = PaneViewportSnapshot.CreateFullFrame(1, 1);
        private string _ffmpegArguments = string.Empty;
        private string _ffmpegPath = string.Empty;
        private string _ffprobePath = string.Empty;

        public string OutputFilePath
        {
            get { return _outputFilePath; }
            init { _outputFilePath = value ?? string.Empty; }
        }

        public CompareSideBySideExportMode Mode { get; init; }

        public CompareSideBySideExportAudioSource AudioSource { get; init; }

        public string PrimarySourceFilePath
        {
            get { return _primarySourceFilePath; }
            init { _primarySourceFilePath = value ?? string.Empty; }
        }

        public string CompareSourceFilePath
        {
            get { return _compareSourceFilePath; }
            init { _compareSourceFilePath = value ?? string.Empty; }
        }

        public TimeSpan PrimaryStartTime
        {
            get { return _primaryStartTime; }
            init { _primaryStartTime = value < TimeSpan.Zero ? TimeSpan.Zero : value; }
        }

        public TimeSpan PrimaryContentDuration
        {
            get { return _primaryContentDuration; }
            init { _primaryContentDuration = value < TimeSpan.Zero ? TimeSpan.Zero : value; }
        }

        public TimeSpan PrimaryLeadingPad
        {
            get { return _primaryLeadingPad; }
            init { _primaryLeadingPad = value < TimeSpan.Zero ? TimeSpan.Zero : value; }
        }

        public TimeSpan PrimaryTrailingPad
        {
            get { return _primaryTrailingPad; }
            init { _primaryTrailingPad = value < TimeSpan.Zero ? TimeSpan.Zero : value; }
        }

        public TimeSpan CompareStartTime
        {
            get { return _compareStartTime; }
            init { _compareStartTime = value < TimeSpan.Zero ? TimeSpan.Zero : value; }
        }

        public TimeSpan CompareContentDuration
        {
            get { return _compareContentDuration; }
            init { _compareContentDuration = value < TimeSpan.Zero ? TimeSpan.Zero : value; }
        }

        public TimeSpan CompareLeadingPad
        {
            get { return _compareLeadingPad; }
            init { _compareLeadingPad = value < TimeSpan.Zero ? TimeSpan.Zero : value; }
        }

        public TimeSpan CompareTrailingPad
        {
            get { return _compareTrailingPad; }
            init { _compareTrailingPad = value < TimeSpan.Zero ? TimeSpan.Zero : value; }
        }

        public string PrimaryEndBoundaryStrategy
        {
            get { return _primaryEndBoundaryStrategy; }
            init { _primaryEndBoundaryStrategy = value ?? string.Empty; }
        }

        public string CompareEndBoundaryStrategy
        {
            get { return _compareEndBoundaryStrategy; }
            init { _compareEndBoundaryStrategy = value ?? string.Empty; }
        }

        public TimeSpan OutputDuration
        {
            get { return _outputDuration; }
            init { _outputDuration = value < TimeSpan.Zero ? TimeSpan.Zero : value; }
        }

        public int PrimaryRenderWidth
        {
            get { return _primaryRenderWidth; }
            init { _primaryRenderWidth = Math.Max(1, value); }
        }

        public int PrimaryRenderHeight
        {
            get { return _primaryRenderHeight; }
            init { _primaryRenderHeight = Math.Max(1, value); }
        }

        public int CompareRenderWidth
        {
            get { return _compareRenderWidth; }
            init { _compareRenderWidth = Math.Max(1, value); }
        }

        public int CompareRenderHeight
        {
            get { return _compareRenderHeight; }
            init { _compareRenderHeight = Math.Max(1, value); }
        }

        public int OutputWidth
        {
            get { return _outputWidth; }
            init { _outputWidth = Math.Max(1, value); }
        }

        public int OutputHeight
        {
            get { return _outputHeight; }
            init { _outputHeight = Math.Max(1, value); }
        }

        public PaneViewportSnapshot PrimaryViewportSnapshot
        {
            get { return _primaryViewportSnapshot; }
            init { _primaryViewportSnapshot = value ?? PaneViewportSnapshot.CreateFullFrame(1, 1); }
        }

        public PaneViewportSnapshot CompareViewportSnapshot
        {
            get { return _compareViewportSnapshot; }
            init { _compareViewportSnapshot = value ?? PaneViewportSnapshot.CreateFullFrame(1, 1); }
        }

        public bool SelectedAudioHasStream { get; init; }

        public string FfmpegArguments
        {
            get { return _ffmpegArguments; }
            init { _ffmpegArguments = value ?? string.Empty; }
        }

        public string FfmpegPath
        {
            get { return _ffmpegPath; }
            init { _ffmpegPath = value ?? string.Empty; }
        }

        public string FfprobePath
        {
            get { return _ffprobePath; }
            init { _ffprobePath = value ?? string.Empty; }
        }
    }
}
