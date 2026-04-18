namespace FramePlayer.Core.Models
{
    public sealed class AudioInsertionRequest
    {
        public AudioInsertionRequest(
            string sourceFilePath,
            string replacementAudioFilePath,
            string outputFilePath,
            string displayLabel,
            ReviewSessionSnapshot sessionSnapshot)
        {
            SourceFilePath = sourceFilePath ?? string.Empty;
            ReplacementAudioFilePath = replacementAudioFilePath ?? string.Empty;
            OutputFilePath = outputFilePath ?? string.Empty;
            DisplayLabel = displayLabel ?? string.Empty;
            SessionSnapshot = sessionSnapshot ?? ReviewSessionSnapshot.Empty;
        }

        public string SourceFilePath { get; }

        public string ReplacementAudioFilePath { get; }

        public string OutputFilePath { get; }

        public string DisplayLabel { get; }

        public ReviewSessionSnapshot SessionSnapshot { get; }
    }
}
