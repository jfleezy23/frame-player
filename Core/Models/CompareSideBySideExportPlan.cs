using System;

namespace FramePlayer.Core.Models
{
    public sealed class CompareSideBySideExportPlan
    {
        public CompareSideBySideExportPlan(
            string outputFilePath,
            CompareSideBySideExportMode mode,
            CompareSideBySideExportAudioSource audioSource,
            string primarySourceFilePath,
            string compareSourceFilePath,
            TimeSpan primaryStartTime,
            TimeSpan primaryContentDuration,
            TimeSpan primaryLeadingPad,
            TimeSpan primaryTrailingPad,
            TimeSpan compareStartTime,
            TimeSpan compareContentDuration,
            TimeSpan compareLeadingPad,
            TimeSpan compareTrailingPad,
            string primaryEndBoundaryStrategy,
            string compareEndBoundaryStrategy,
            TimeSpan outputDuration,
            int primaryRenderWidth,
            int primaryRenderHeight,
            int compareRenderWidth,
            int compareRenderHeight,
            int outputWidth,
            int outputHeight,
            bool selectedAudioHasStream,
            string ffmpegArguments,
            string ffmpegPath,
            string ffprobePath)
        {
            OutputFilePath = outputFilePath ?? string.Empty;
            Mode = mode;
            AudioSource = audioSource;
            PrimarySourceFilePath = primarySourceFilePath ?? string.Empty;
            CompareSourceFilePath = compareSourceFilePath ?? string.Empty;
            PrimaryStartTime = primaryStartTime < TimeSpan.Zero ? TimeSpan.Zero : primaryStartTime;
            PrimaryContentDuration = primaryContentDuration < TimeSpan.Zero ? TimeSpan.Zero : primaryContentDuration;
            PrimaryLeadingPad = primaryLeadingPad < TimeSpan.Zero ? TimeSpan.Zero : primaryLeadingPad;
            PrimaryTrailingPad = primaryTrailingPad < TimeSpan.Zero ? TimeSpan.Zero : primaryTrailingPad;
            CompareStartTime = compareStartTime < TimeSpan.Zero ? TimeSpan.Zero : compareStartTime;
            CompareContentDuration = compareContentDuration < TimeSpan.Zero ? TimeSpan.Zero : compareContentDuration;
            CompareLeadingPad = compareLeadingPad < TimeSpan.Zero ? TimeSpan.Zero : compareLeadingPad;
            CompareTrailingPad = compareTrailingPad < TimeSpan.Zero ? TimeSpan.Zero : compareTrailingPad;
            PrimaryEndBoundaryStrategy = primaryEndBoundaryStrategy ?? string.Empty;
            CompareEndBoundaryStrategy = compareEndBoundaryStrategy ?? string.Empty;
            OutputDuration = outputDuration < TimeSpan.Zero ? TimeSpan.Zero : outputDuration;
            PrimaryRenderWidth = Math.Max(1, primaryRenderWidth);
            PrimaryRenderHeight = Math.Max(1, primaryRenderHeight);
            CompareRenderWidth = Math.Max(1, compareRenderWidth);
            CompareRenderHeight = Math.Max(1, compareRenderHeight);
            OutputWidth = Math.Max(1, outputWidth);
            OutputHeight = Math.Max(1, outputHeight);
            SelectedAudioHasStream = selectedAudioHasStream;
            FfmpegArguments = ffmpegArguments ?? string.Empty;
            FfmpegPath = ffmpegPath ?? string.Empty;
            FfprobePath = ffprobePath ?? string.Empty;
        }

        public string OutputFilePath { get; }

        public CompareSideBySideExportMode Mode { get; }

        public CompareSideBySideExportAudioSource AudioSource { get; }

        public string PrimarySourceFilePath { get; }

        public string CompareSourceFilePath { get; }

        public TimeSpan PrimaryStartTime { get; }

        public TimeSpan PrimaryContentDuration { get; }

        public TimeSpan PrimaryLeadingPad { get; }

        public TimeSpan PrimaryTrailingPad { get; }

        public TimeSpan CompareStartTime { get; }

        public TimeSpan CompareContentDuration { get; }

        public TimeSpan CompareLeadingPad { get; }

        public TimeSpan CompareTrailingPad { get; }

        public string PrimaryEndBoundaryStrategy { get; }

        public string CompareEndBoundaryStrategy { get; }

        public TimeSpan OutputDuration { get; }

        public int PrimaryRenderWidth { get; }

        public int PrimaryRenderHeight { get; }

        public int CompareRenderWidth { get; }

        public int CompareRenderHeight { get; }

        public int OutputWidth { get; }

        public int OutputHeight { get; }

        public bool SelectedAudioHasStream { get; }

        public string FfmpegArguments { get; }

        public string FfmpegPath { get; }

        public string FfprobePath { get; }
    }
}
