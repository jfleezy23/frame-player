using System;
using System.Diagnostics.CodeAnalysis;

namespace FramePlayer.Core.Models
{
    public sealed class ClipExportPlan
    {
        [SuppressMessage(
            "Major Code Smell",
            "S107:Methods should not have too many parameters",
            Justification = "Clip export plans are immutable export-time transport models with explicit scalar fields to keep command construction and diagnostics straightforward.")]
        public ClipExportPlan(
            string sourceFilePath,
            string outputFilePath,
            string displayLabel,
            string paneId,
            bool isPaneLocal,
            TimeSpan startTime,
            TimeSpan endTimeExclusive,
            long? startFrameIndex,
            long? endFrameIndex,
            string endBoundaryStrategy,
            PaneViewportSnapshot viewportSnapshot,
            string ffmpegArguments,
            string ffmpegPath,
            string ffprobePath)
        {
            SourceFilePath = sourceFilePath ?? string.Empty;
            OutputFilePath = outputFilePath ?? string.Empty;
            DisplayLabel = displayLabel ?? string.Empty;
            PaneId = paneId ?? string.Empty;
            IsPaneLocal = isPaneLocal;
            StartTime = startTime < TimeSpan.Zero ? TimeSpan.Zero : startTime;
            EndTimeExclusive = endTimeExclusive < TimeSpan.Zero ? TimeSpan.Zero : endTimeExclusive;
            StartFrameIndex = startFrameIndex;
            EndFrameIndex = endFrameIndex;
            EndBoundaryStrategy = endBoundaryStrategy ?? string.Empty;
            ViewportSnapshot = viewportSnapshot ?? PaneViewportSnapshot.CreateFullFrame(1, 1);
            FfmpegArguments = ffmpegArguments ?? string.Empty;
            FfmpegPath = ffmpegPath ?? string.Empty;
            FfprobePath = ffprobePath ?? string.Empty;
        }

        public string SourceFilePath { get; }

        public string OutputFilePath { get; }

        public string DisplayLabel { get; }

        public string PaneId { get; }

        public bool IsPaneLocal { get; }

        public TimeSpan StartTime { get; }

        public TimeSpan EndTimeExclusive { get; }

        public long? StartFrameIndex { get; }

        public long? EndFrameIndex { get; }

        public string EndBoundaryStrategy { get; }

        public PaneViewportSnapshot ViewportSnapshot { get; }

        public string FfmpegArguments { get; }

        public string FfmpegPath { get; }

        public string FfprobePath { get; }

        public TimeSpan Duration
        {
            get
            {
                var duration = EndTimeExclusive - StartTime;
                return duration > TimeSpan.Zero ? duration : TimeSpan.Zero;
            }
        }
    }
}
