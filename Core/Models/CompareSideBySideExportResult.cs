using System;

namespace FramePlayer.Core.Models
{
    public sealed class CompareSideBySideExportResult
    {
        private string _message = string.Empty;
        private TimeSpan _elapsed = TimeSpan.Zero;
        private string _standardOutput = string.Empty;
        private string _standardError = string.Empty;

        public bool Succeeded { get; init; }

        public CompareSideBySideExportPlan Plan { get; init; }

        public string Message
        {
            get { return _message; }
            init { _message = value ?? string.Empty; }
        }

        public int ExitCode { get; init; }

        public TimeSpan Elapsed
        {
            get { return _elapsed; }
            init { _elapsed = value < TimeSpan.Zero ? TimeSpan.Zero : value; }
        }

        public TimeSpan? ProbedDuration { get; init; }

        public int? ProbedVideoWidth { get; init; }

        public int? ProbedVideoHeight { get; init; }

        public bool? ProbedHasAudioStream { get; init; }

        public string StandardOutput
        {
            get { return _standardOutput; }
            init { _standardOutput = value ?? string.Empty; }
        }

        public string StandardError
        {
            get { return _standardError; }
            init { _standardError = value ?? string.Empty; }
        }
    }
}
