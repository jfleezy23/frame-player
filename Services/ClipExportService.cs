using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FramePlayer.Core.Abstractions;
using FramePlayer.Core.Models;
using FramePlayer.Engines.FFmpeg;

namespace FramePlayer.Services
{
    public sealed class ClipExportService
    {
        private const string ToolsFolderName = "ffmpeg-tools";
        private static readonly TimeSpan MinimumFallbackFrameStep = TimeSpan.FromMilliseconds(1d);
        private readonly Lazy<ToolAvailability> _toolAvailability;

        public ClipExportService()
        {
            _toolAvailability = new Lazy<ToolAvailability>(DiscoverToolAvailability);
        }

        public bool IsBundledToolingAvailable
        {
            get { return _toolAvailability.Value.IsAvailable; }
        }

        public string GetToolAvailabilityMessage()
        {
            return _toolAvailability.Value.Message;
        }

        public ClipExportPlan CreatePlan(ClipExportRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            var toolAvailability = _toolAvailability.Value;
            if (!toolAvailability.IsAvailable)
            {
                throw new InvalidOperationException(toolAvailability.Message);
            }

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
            var startTime = ClampTime(loopRange.LoopIn.PresentationTime, mediaDuration);
            var endBoundaryStrategy = "position-step";
            var endTimeExclusive = BuildExclusiveEndTime(
                request,
                loopRange.LoopOut,
                mediaDuration,
                out endBoundaryStrategy);
            if (endTimeExclusive <= startTime)
            {
                throw new InvalidOperationException("Clip export could not resolve a valid exclusive end boundary.");
            }

            Directory.CreateDirectory(outputDirectory);

            var ffmpegArguments = BuildFfmpegArguments(sourceFullPath, outputFullPath, startTime, endTimeExclusive - startTime);
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
                ffmpegArguments,
                toolAvailability.FfmpegPath,
                toolAvailability.FfprobePath);
        }

        public async Task<ClipExportResult> ExportAsync(ClipExportRequest request, CancellationToken cancellationToken = default(CancellationToken))
        {
            var plan = CreatePlan(request);
            return await ExportPlanAsync(plan, cancellationToken).ConfigureAwait(false);
        }

        public async Task<ClipExportResult> ExportPlanAsync(ClipExportPlan plan, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            return await Task.Run(
                () =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var stopwatch = Stopwatch.StartNew();
                    var processResult = RunProcess(plan.FfmpegPath, plan.FfmpegArguments, workingDirectory: Path.GetDirectoryName(plan.OutputFilePath));
                    stopwatch.Stop();

                    if (processResult.ExitCode != 0)
                    {
                        return new ClipExportResult(
                            false,
                            plan,
                            BuildFailureMessage(processResult, "FFmpeg clip export failed."),
                            processResult.ExitCode,
                            stopwatch.Elapsed,
                            null,
                            processResult.StandardOutput,
                            processResult.StandardError);
                    }

                    TimeSpan? probedDuration = null;
                    var probeResult = RunProcess(
                        plan.FfprobePath,
                        string.Format(
                            CultureInfo.InvariantCulture,
                            "-v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 \"{0}\"",
                            plan.OutputFilePath),
                        workingDirectory: Path.GetDirectoryName(plan.OutputFilePath));
                    if (probeResult.ExitCode == 0)
                    {
                        double parsedSeconds;
                        if (double.TryParse(
                            (probeResult.StandardOutput ?? string.Empty).Trim(),
                            NumberStyles.Float,
                            CultureInfo.InvariantCulture,
                            out parsedSeconds) &&
                            parsedSeconds >= 0d)
                        {
                            probedDuration = TimeSpan.FromSeconds(parsedSeconds);
                        }
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

        private static TimeSpan BuildExclusiveEndTime(
            ClipExportRequest request,
            LoopPlaybackAnchorSnapshot loopOut,
            TimeSpan mediaDuration,
            out string boundaryStrategy)
        {
            if (loopOut == null)
            {
                boundaryStrategy = "missing";
                return TimeSpan.Zero;
            }

            var indexedFrameTimeResolver = request.IndexedFrameTimeResolver;
            if (indexedFrameTimeResolver != null &&
                loopOut.AbsoluteFrameIndex.HasValue &&
                indexedFrameTimeResolver.TryGetIndexedPresentationTime(loopOut.AbsoluteFrameIndex.Value + 1L, out var nextIndexedTime))
            {
                boundaryStrategy = "next-indexed-frame";
                return ClampTime(nextIndexedTime, mediaDuration);
            }

            var positionStep = request.SessionSnapshot != null
                ? request.SessionSnapshot.MediaInfo.PositionStep
                : TimeSpan.Zero;
            if (positionStep <= TimeSpan.Zero)
            {
                var framesPerSecond = request.SessionSnapshot != null
                    ? request.SessionSnapshot.MediaInfo.FramesPerSecond
                    : 0d;
                if (framesPerSecond > 0d)
                {
                    positionStep = TimeSpan.FromSeconds(1d / framesPerSecond);
                }
            }

            if (positionStep <= TimeSpan.Zero)
            {
                positionStep = MinimumFallbackFrameStep;
            }

            boundaryStrategy = "position-step";
            return ClampTime(loopOut.PresentationTime + positionStep, mediaDuration);
        }

        private static string BuildFailureMessage(ProcessExecutionResult processResult, string defaultMessage)
        {
            var details = !string.IsNullOrWhiteSpace(processResult.StandardError)
                ? processResult.StandardError
                : processResult.StandardOutput;
            details = (details ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(details))
            {
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "{0} Exit code {1} (0x{2}).",
                    defaultMessage,
                    processResult.ExitCode,
                    unchecked((uint)processResult.ExitCode).ToString("X8", CultureInfo.InvariantCulture));
            }

            var condensed = details.Replace(Environment.NewLine, " ").Trim();
            return defaultMessage + " " + condensed;
        }

        private static string BuildFfmpegArguments(string sourceFilePath, string outputFilePath, TimeSpan startTime, TimeSpan duration)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "-v error -y -i \"{0}\" -ss {1} -t {2} -map 0:v:0 -map 0:a? -sn -dn -c:v libx264 -preset medium -crf 18 -pix_fmt yuv420p -c:a aac -b:a 192k -movflags +faststart \"{3}\"",
                sourceFilePath,
                FormatFfmpegTime(startTime),
                FormatFfmpegTime(duration),
                outputFilePath);
        }

        private static string FormatFfmpegTime(TimeSpan value)
        {
            return value.TotalSeconds.ToString("0.######", CultureInfo.InvariantCulture);
        }

        private static TimeSpan ClampTime(TimeSpan value, TimeSpan mediaDuration)
        {
            if (value < TimeSpan.Zero)
            {
                return TimeSpan.Zero;
            }

            if (mediaDuration > TimeSpan.Zero && value > mediaDuration)
            {
                return mediaDuration;
            }

            return value;
        }

        private static ToolAvailability DiscoverToolAvailability()
        {
            var toolsDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ToolsFolderName);
            string errorMessage;
            string ffmpegPath;
            string ffprobePath;
            if (!ExportToolsManifestService.TryGetToolPaths(toolsDirectory, out ffmpegPath, out ffprobePath, out errorMessage))
            {
                return new ToolAvailability(
                    false,
                    toolsDirectory,
                    string.Empty,
                    string.Empty,
                    string.IsNullOrWhiteSpace(errorMessage)
                        ? "The bundled FFmpeg export tools are unavailable."
                        : errorMessage);
            }

            return new ToolAvailability(
                true,
                toolsDirectory,
                ffmpegPath,
                ffprobePath,
                "Bundled FFmpeg export tools are ready.");
        }

        private static ProcessExecutionResult RunProcess(string filePath, string arguments, string workingDirectory)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = filePath,
                Arguments = arguments ?? string.Empty,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory)
                    ? AppDomain.CurrentDomain.BaseDirectory
                    : workingDirectory
            };

            using (var process = new Process { StartInfo = startInfo })
            {
                process.Start();
                var standardOutput = process.StandardOutput.ReadToEnd();
                var standardError = process.StandardError.ReadToEnd();
                process.WaitForExit();

                return new ProcessExecutionResult(process.ExitCode, standardOutput, standardError);
            }
        }

        private sealed class ToolAvailability
        {
            public ToolAvailability(
                bool isAvailable,
                string toolsDirectory,
                string ffmpegPath,
                string ffprobePath,
                string message)
            {
                IsAvailable = isAvailable;
                ToolsDirectory = toolsDirectory ?? string.Empty;
                FfmpegPath = ffmpegPath ?? string.Empty;
                FfprobePath = ffprobePath ?? string.Empty;
                Message = message ?? string.Empty;
            }

            public bool IsAvailable { get; }

            public string ToolsDirectory { get; }

            public string FfmpegPath { get; }

            public string FfprobePath { get; }

            public string Message { get; }
        }

        private sealed class ProcessExecutionResult
        {
            public ProcessExecutionResult(int exitCode, string standardOutput, string standardError)
            {
                ExitCode = exitCode;
                StandardOutput = standardOutput ?? string.Empty;
                StandardError = standardError ?? string.Empty;
            }

            public int ExitCode { get; }

            public string StandardOutput { get; }

            public string StandardError { get; }
        }
    }

    public sealed class IndexedFrameTimeResolverAdapter : IIndexedFrameTimeResolver
    {
        private readonly FfmpegReviewEngine _engine;

        public IndexedFrameTimeResolverAdapter(FfmpegReviewEngine engine)
        {
            _engine = engine;
        }

        public bool TryGetIndexedPresentationTime(long absoluteFrameIndex, out TimeSpan presentationTime)
        {
            if (_engine != null)
            {
                return _engine.TryGetIndexedPresentationTime(absoluteFrameIndex, out presentationTime);
            }

            presentationTime = TimeSpan.Zero;
            return false;
        }
    }
}
