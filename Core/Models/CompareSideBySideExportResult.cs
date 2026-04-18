using System;

namespace FramePlayer.Core.Models
{
    public sealed class CompareSideBySideExportResult
    {
        public CompareSideBySideExportResult(
            bool succeeded,
            CompareSideBySideExportPlan plan,
            string message,
            int exitCode,
            TimeSpan elapsed,
            TimeSpan? probedDuration,
            int? probedVideoWidth,
            int? probedVideoHeight,
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
            ProbedVideoWidth = probedVideoWidth;
            ProbedVideoHeight = probedVideoHeight;
            ProbedHasAudioStream = probedHasAudioStream;
            StandardOutput = standardOutput ?? string.Empty;
            StandardError = standardError ?? string.Empty;
        }

        public bool Succeeded { get; }

        public CompareSideBySideExportPlan Plan { get; }

        public string Message { get; }

        public int ExitCode { get; }

        public TimeSpan Elapsed { get; }

        public TimeSpan? ProbedDuration { get; }

        public int? ProbedVideoWidth { get; }

        public int? ProbedVideoHeight { get; }

        public bool? ProbedHasAudioStream { get; }

        public string StandardOutput { get; }

        public string StandardError { get; }
    }
}
