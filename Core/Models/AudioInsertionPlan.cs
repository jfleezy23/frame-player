using System;

namespace FramePlayer.Core.Models
{
    public sealed class AudioInsertionPlan
    {
        public AudioInsertionPlan(
            string sourceFilePath,
            string replacementAudioFilePath,
            string outputFilePath,
            string displayLabel,
            TimeSpan videoDuration,
            string ffmpegArguments,
            string ffmpegPath,
            string ffprobePath)
        {
            SourceFilePath = sourceFilePath ?? string.Empty;
            ReplacementAudioFilePath = replacementAudioFilePath ?? string.Empty;
            OutputFilePath = outputFilePath ?? string.Empty;
            DisplayLabel = displayLabel ?? string.Empty;
            VideoDuration = videoDuration < TimeSpan.Zero ? TimeSpan.Zero : videoDuration;
            FfmpegArguments = ffmpegArguments ?? string.Empty;
            FfmpegPath = ffmpegPath ?? string.Empty;
            FfprobePath = ffprobePath ?? string.Empty;
        }

        public string SourceFilePath { get; }

        public string ReplacementAudioFilePath { get; }

        public string OutputFilePath { get; }

        public string DisplayLabel { get; }

        public TimeSpan VideoDuration { get; }

        public string FfmpegArguments { get; }

        public string FfmpegPath { get; }

        public string FfprobePath { get; }
    }
}
