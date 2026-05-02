using System;
using System.Diagnostics.CodeAnalysis;

namespace FramePlayer.Core.Models
{
    public sealed class AudioInsertionResult
    {
        [SuppressMessage(
            "Major Code Smell",
            "S107:Methods should not have too many parameters",
            Justification = "Audio insertion results are immutable cold-path status snapshots, so explicit scalar fields are preferred over extra wrapper types.")]
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
