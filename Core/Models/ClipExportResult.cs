using System;

namespace FramePlayer.Core.Models
{
    public sealed class ClipExportResult
    {
        public ClipExportResult(
            bool succeeded,
            ClipExportPlan plan,
            string message,
            int exitCode,
            TimeSpan elapsed,
            TimeSpan? probedDuration,
            string standardOutput,
            string standardError)
        {
            Succeeded = succeeded;
            Plan = plan;
            Message = message ?? string.Empty;
            ExitCode = exitCode;
            Elapsed = elapsed < TimeSpan.Zero ? TimeSpan.Zero : elapsed;
            ProbedDuration = probedDuration;
            StandardOutput = standardOutput ?? string.Empty;
            StandardError = standardError ?? string.Empty;
        }

        public bool Succeeded { get; }

        public ClipExportPlan Plan { get; }

        public string Message { get; }

        public int ExitCode { get; }

        public TimeSpan Elapsed { get; }

        public TimeSpan? ProbedDuration { get; }

        public string StandardOutput { get; }

        public string StandardError { get; }
    }
}
