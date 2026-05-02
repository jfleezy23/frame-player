using System;
using System.Diagnostics.CodeAnalysis;

namespace FramePlayer.Core.Models
{
    public sealed class ClipExportResult
    {
        [SuppressMessage(
            "Major Code Smell",
            "S107:Methods should not have too many parameters",
            Justification = "Clip export results are immutable export-path snapshots that intentionally keep scalar fields explicit for reporting and test assertions.")]
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
