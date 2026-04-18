using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FramePlayer.Core.Models;

namespace FramePlayer.Services
{
    internal sealed class ClipExportService
    {
        private readonly FfmpegCliTooling _tooling;

        public ClipExportService()
        {
            _tooling = new FfmpegCliTooling();
        }

        public bool IsBundledToolingAvailable
        {
            get { return _tooling.IsBundledToolingAvailable; }
        }

        public string GetToolAvailabilityMessage()
        {
            return _tooling.GetToolAvailabilityMessage();
        }

        public ClipExportPlan CreatePlan(ClipExportRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);

            var toolPaths = _tooling.GetRequiredToolPaths();

            if (string.IsNullOrWhiteSpace(request.SourceFilePath))
            {
                throw new InvalidOperationException("No reviewed source file is available for clip export.");
            }

            if (!File.Exists(request.SourceFilePath))
            {
                throw new FileNotFoundException("The reviewed source file could not be found.", request.SourceFilePath);
            }

            if (string.IsNullOrWhiteSpace(request.OutputFilePath))
            {
                throw new InvalidOperationException("A destination path is required for clip export.");
            }

            var sourceFullPath = Path.GetFullPath(request.SourceFilePath);
            var outputFullPath = Path.GetFullPath(request.OutputFilePath);
            if (string.Equals(sourceFullPath, outputFullPath, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Clip export cannot overwrite the reviewed source file.");
            }

            var outputDirectory = Path.GetDirectoryName(outputFullPath);
            if (string.IsNullOrWhiteSpace(outputDirectory))
            {
                throw new InvalidOperationException("Clip export requires a valid destination folder.");
            }

            var loopRange = request.LoopRange;
            if (loopRange == null)
            {
                throw new InvalidOperationException("No loop range is available for clip export.");
            }

            if (!loopRange.HasLoopIn || !loopRange.HasLoopOut)
            {
                throw new InvalidOperationException("Clip export requires both loop-in and loop-out markers.");
            }

            if (loopRange.HasPendingMarkers)
            {
                throw new InvalidOperationException("Clip export is disabled while loop markers are still pending exact frame identity.");
            }

            if (loopRange.IsInvalidRange)
            {
                throw new InvalidOperationException("Clip export is disabled because loop-out lands before loop-in.");
            }

            var mediaDuration = request.SessionSnapshot != null
                ? request.SessionSnapshot.MediaInfo.Duration
                : TimeSpan.Zero;
            var viewportSnapshot = request.ViewportSnapshot ?? PaneViewportSnapshot.CreateFullFrame(
                request.SessionSnapshot.MediaInfo.PixelWidth,
                request.SessionSnapshot.MediaInfo.PixelHeight);
            var startTime = FfmpegExportTiming.ClampTime(loopRange.LoopIn.PresentationTime, mediaDuration);
            var endTimeExclusive = FfmpegExportTiming.BuildExclusiveEndTime(
                request.Engine,
                request.SessionSnapshot,
                loopRange.LoopOut,
                mediaDuration,
                out var endBoundaryStrategy);
            if (endTimeExclusive <= startTime)
            {
                throw new InvalidOperationException("Clip export could not resolve a valid exclusive end boundary.");
            }

            Directory.CreateDirectory(outputDirectory);

            var outputWidth = request.SessionSnapshot != null && request.SessionSnapshot.MediaInfo != null
                ? Math.Max(1, request.SessionSnapshot.MediaInfo.PixelWidth)
                : Math.Max(1, viewportSnapshot.SourcePixelWidth);
            var outputHeight = request.SessionSnapshot != null && request.SessionSnapshot.MediaInfo != null
                ? Math.Max(1, request.SessionSnapshot.MediaInfo.PixelHeight)
                : Math.Max(1, viewportSnapshot.SourcePixelHeight);
            var ffmpegArguments = BuildFfmpegArguments(
                sourceFullPath,
                outputFullPath,
                startTime,
                endTimeExclusive - startTime,
                viewportSnapshot,
                outputWidth,
                outputHeight);
            return new ClipExportPlan(
                sourceFullPath,
                outputFullPath,
                request.DisplayLabel,
                request.PaneId,
                request.IsPaneLocal,
                startTime,
                endTimeExclusive,
                loopRange.LoopIn.AbsoluteFrameIndex,
                loopRange.LoopOut.AbsoluteFrameIndex,
                endBoundaryStrategy,
                viewportSnapshot,
                ffmpegArguments,
                toolPaths.FfmpegPath,
                toolPaths.FfprobePath);
        }

        public async Task<ClipExportResult> ExportAsync(ClipExportRequest request, CancellationToken cancellationToken = default(CancellationToken))
        {
            var plan = CreatePlan(request);
            return await ExportPlanAsync(plan, cancellationToken).ConfigureAwait(false);
        }

        public static async Task<ClipExportResult> ExportPlanAsync(ClipExportPlan plan, CancellationToken cancellationToken = default(CancellationToken))
        {
            ArgumentNullException.ThrowIfNull(plan);

            return await Task.Run(
                () =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                    var processResult = FfmpegCliTooling.RunProcess(
                        plan.FfmpegPath,
                        plan.FfmpegArguments,
                        Path.GetDirectoryName(plan.OutputFilePath));
                    stopwatch.Stop();

                    if (processResult.ExitCode != 0)
                    {
                        return new ClipExportResult(
                            false,
                            plan,
                            FfmpegCliTooling.BuildFailureMessage(processResult, "FFmpeg clip export failed."),
                            processResult.ExitCode,
                            stopwatch.Elapsed,
                            null,
                            processResult.StandardOutput,
                            processResult.StandardError);
                    }

                    TimeSpan? probedDuration = null;
                    FfmpegMediaProbe probe;
                    if (FfmpegCliTooling.TryProbeMediaFile(plan.FfprobePath, plan.OutputFilePath, out probe) &&
                        probe != null &&
                        probe.Duration.HasValue)
                    {
                        probedDuration = probe.Duration;
                    }

                    return new ClipExportResult(
                        true,
                        plan,
                        "Clip export completed.",
                        0,
                        stopwatch.Elapsed,
                        probedDuration,
                        processResult.StandardOutput,
                        processResult.StandardError);
                },
                cancellationToken).ConfigureAwait(false);
        }

        private static string BuildFfmpegArguments(
            string sourceFilePath,
            string outputFilePath,
            TimeSpan startTime,
            TimeSpan duration,
            PaneViewportSnapshot viewportSnapshot,
            int outputWidth,
            int outputHeight)
        {
            var filterArguments = BuildVideoFilterArguments(viewportSnapshot, outputWidth, outputHeight);
            return string.Format(
                CultureInfo.InvariantCulture,
                "-v error -y -i \"{0}\" -ss {1} -t {2} -map 0:v:0 -map 0:a? -sn -dn {3}-c:v libx264 -preset medium -crf 18 -pix_fmt yuv420p -c:a aac -b:a 192k -movflags +faststart \"{4}\"",
                sourceFilePath,
                FfmpegExportTiming.FormatFfmpegTime(startTime),
                FfmpegExportTiming.FormatFfmpegTime(duration),
                filterArguments,
                outputFilePath);
        }

        private static string BuildVideoFilterArguments(
            PaneViewportSnapshot viewportSnapshot,
            int outputWidth,
            int outputHeight)
        {
            if (viewportSnapshot == null || !viewportSnapshot.IsZoomed)
            {
                return string.Empty;
            }

            return string.Format(
                CultureInfo.InvariantCulture,
                "-vf \"crop={0}:{1}:{2}:{3},scale={4}:{5}:flags=lanczos\" ",
                viewportSnapshot.SourceCropWidth,
                viewportSnapshot.SourceCropHeight,
                viewportSnapshot.SourceCropX,
                viewportSnapshot.SourceCropY,
                Math.Max(1, outputWidth),
                Math.Max(1, outputHeight));
        }
    }
}
