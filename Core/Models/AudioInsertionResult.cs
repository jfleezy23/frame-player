using System;

namespace FramePlayer.Core.Models
{
    public sealed class AudioInsertionResult
    {
        public AudioInsertionResult(
            bool succeeded,
            AudioInsertionPlan plan,
            string message,
            int exitCode,
            TimeSpan elapsed,
            TimeSpan? probedDuration,
            bool? probedHasAudioStream,
            string standardOutput,
            string standardError)
        {
            Succeeded = succeeded;
            Plan = plan;
            Message = message ?? string.Empty;
            ExitCode = exitCode;
            Elapsed = elapsed < TimeSpan.Zero ? TimeSpan.Zero : elapsed;
            ProbedDuration = probedDuration;
            ProbedHasAudioStream = probedHasAudioStream;
            StandardOutput = standardOutput ?? string.Empty;
            StandardError = standardError ?? string.Empty;
        }

        public bool Succeeded { get; }

        public AudioInsertionPlan Plan { get; }

        public string Message { get; }

        public int ExitCode { get; }

        public TimeSpan Elapsed { get; }

        public TimeSpan? ProbedDuration { get; }

        public bool? ProbedHasAudioStream { get; }

        public string StandardOutput { get; }

        public string StandardError { get; }
    }
}
