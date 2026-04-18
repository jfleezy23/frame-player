using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FramePlayer.Core.Models;

namespace FramePlayer.Services
{
    internal sealed class CompareSideBySideExportService
    {
        private readonly FfmpegCliTooling _tooling;

        public CompareSideBySideExportService()
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

        public CompareSideBySideExportPlan CreatePlan(CompareSideBySideExportRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            var toolPaths = _tooling.GetRequiredToolPaths();

            var primarySession = request.PrimarySessionSnapshot ?? ReviewSessionSnapshot.Empty;
            var compareSession = request.CompareSessionSnapshot ?? ReviewSessionSnapshot.Empty;
            if (!primarySession.IsMediaOpen || string.IsNullOrWhiteSpace(primarySession.CurrentFilePath))
            {
                throw new InvalidOperationException("Primary pane media is not available for compare export.");
            }

            if (!compareSession.IsMediaOpen || string.IsNullOrWhiteSpace(compareSession.CurrentFilePath))
            {
                throw new InvalidOperationException("Compare pane media is not available for compare export.");
            }

            if (!File.Exists(primarySession.CurrentFilePath))
            {
                throw new FileNotFoundException("The primary compare source file could not be found.", primarySession.CurrentFilePath);
            }

            if (!File.Exists(compareSession.CurrentFilePath))
            {
                throw new FileNotFoundException("The compare source file could not be found.", compareSession.CurrentFilePath);
            }

            if (string.IsNullOrWhiteSpace(request.OutputFilePath))
            {
                throw new InvalidOperationException("A destination path is required for compare export.");
            }

            var outputFullPath = Path.GetFullPath(request.OutputFilePath);
            var primarySourceFullPath = Path.GetFullPath(primarySession.CurrentFilePath);
            var compareSourceFullPath = Path.GetFullPath(compareSession.CurrentFilePath);
            if (string.Equals(primarySourceFullPath, outputFullPath, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(compareSourceFullPath, outputFullPath, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Compare export cannot overwrite a reviewed source file.");
            }

            var outputDirectory = Path.GetDirectoryName(outputFullPath);
            if (string.IsNullOrWhiteSpace(outputDirectory))
            {
                throw new InvalidOperationException("Compare export requires a valid destination folder.");
            }

            Directory.CreateDirectory(outputDirectory);

            if (request.Mode == CompareSideBySideExportMode.WholeVideo)
            {
                return BuildWholeVideoPlan(
                    request,
                    toolPaths,
                    primarySourceFullPath,
                    compareSourceFullPath,
                    outputFullPath,
                    outputDirectory);
            }

            return BuildLoopPlan(
                request,
                toolPaths,
                primarySourceFullPath,
                compareSourceFullPath,
                outputFullPath,
                outputDirectory);
        }

        public async Task<CompareSideBySideExportResult> ExportAsync(
            CompareSideBySideExportRequest request,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var plan = CreatePlan(request);
            return await ExportPlanAsync(plan, cancellationToken).ConfigureAwait(false);
        }

        public async Task<CompareSideBySideExportResult> ExportPlanAsync(
            CompareSideBySideExportPlan plan,
            CancellationToken cancellationToken = default(CancellationToken))
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
                    var processResult = FfmpegCliTooling.RunProcess(
                        plan.FfmpegPath,
                        plan.FfmpegArguments,
                        Path.GetDirectoryName(plan.OutputFilePath));
                    stopwatch.Stop();

                    if (processResult.ExitCode != 0)
                    {
                        return new CompareSideBySideExportResult(
                            false,
                            plan,
                            FfmpegCliTooling.BuildFailureMessage(processResult, "FFmpeg compare export failed."),
                            processResult.ExitCode,
                            stopwatch.Elapsed,
                            null,
                            null,
                            null,
                            null,
                            processResult.StandardOutput,
                            processResult.StandardError);
                    }

                    FfmpegMediaProbe probe;
                    var probed = FfmpegCliTooling.TryProbeMediaFile(plan.FfprobePath, plan.OutputFilePath, out probe);
                    return new CompareSideBySideExportResult(
                        true,
                        plan,
                        "Compare export completed.",
                        0,
                        stopwatch.Elapsed,
                        probed && probe != null ? probe.Duration : null,
                        probed && probe != null ? probe.VideoWidth : null,
                        probed && probe != null ? probe.VideoHeight : null,
                        probed && probe != null ? (bool?)probe.HasAudioStream : null,
                        processResult.StandardOutput,
                        processResult.StandardError);
                },
                cancellationToken).ConfigureAwait(false);
        }

        private static CompareSideBySideExportPlan BuildLoopPlan(
            CompareSideBySideExportRequest request,
            FfmpegCliToolPaths toolPaths,
            string primarySourceFullPath,
            string compareSourceFullPath,
            string outputFullPath,
            string outputDirectory)
        {
            var primaryLoopRange = request.PrimaryLoopRange;
            var compareLoopRange = request.CompareLoopRange;
            if (primaryLoopRange == null || compareLoopRange == null)
            {
                throw new InvalidOperationException("Loop-mode compare export requires pane-local A/B ranges on both panes.");
            }

            ValidateLoopRange(primaryLoopRange, "Primary pane");
            ValidateLoopRange(compareLoopRange, "Compare pane");

            var primaryMediaDuration = request.PrimarySessionSnapshot.MediaInfo.Duration;
            var compareMediaDuration = request.CompareSessionSnapshot.MediaInfo.Duration;
            var primaryStartTime = FfmpegExportTiming.ClampTime(primaryLoopRange.LoopIn.PresentationTime, primaryMediaDuration);
            var compareStartTime = FfmpegExportTiming.ClampTime(compareLoopRange.LoopIn.PresentationTime, compareMediaDuration);
            var primaryEndExclusive = FfmpegExportTiming.BuildExclusiveEndTime(
                request.PrimaryEngine,
                request.PrimarySessionSnapshot,
                primaryLoopRange.LoopOut,
                primaryMediaDuration,
                out var primaryBoundaryStrategy);
            var compareEndExclusive = FfmpegExportTiming.BuildExclusiveEndTime(
                request.CompareEngine,
                request.CompareSessionSnapshot,
                compareLoopRange.LoopOut,
                compareMediaDuration,
                out var compareBoundaryStrategy);
            if (primaryEndExclusive <= primaryStartTime)
            {
                throw new InvalidOperationException("Primary pane compare export could not resolve a valid exclusive end boundary.");
            }

            if (compareEndExclusive <= compareStartTime)
            {
                throw new InvalidOperationException("Compare pane compare export could not resolve a valid exclusive end boundary.");
            }

            var primaryContentDuration = primaryEndExclusive - primaryStartTime;
            var compareContentDuration = compareEndExclusive - compareStartTime;
            var outputDuration = primaryContentDuration >= compareContentDuration
                ? primaryContentDuration
                : compareContentDuration;
            if (outputDuration <= TimeSpan.Zero)
            {
                throw new InvalidOperationException("Compare export could not resolve a usable output duration.");
            }

            var primaryLeadingPad = TimeSpan.Zero;
            var compareLeadingPad = TimeSpan.Zero;
            var primaryTrailingPad = outputDuration - primaryContentDuration;
            var compareTrailingPad = outputDuration - compareContentDuration;
            var primaryRenderSize = ResolveRenderSize(request.PrimarySessionSnapshot.MediaInfo);
            var compareRenderSize = ResolveRenderSize(request.CompareSessionSnapshot.MediaInfo);
            var outputSize = ResolveOutputCanvas(primaryRenderSize, compareRenderSize);
            var selectedAudioSession = request.AudioSource == CompareSideBySideExportAudioSource.Compare
                ? request.CompareSessionSnapshot
                : request.PrimarySessionSnapshot;
            var selectedAudioLoopRange = request.AudioSource == CompareSideBySideExportAudioSource.Compare
                ? compareLoopRange
                : primaryLoopRange;
            var selectedAudioStartTime = request.AudioSource == CompareSideBySideExportAudioSource.Compare
                ? compareStartTime
                : primaryStartTime;
            var selectedAudioDuration = request.AudioSource == CompareSideBySideExportAudioSource.Compare
                ? compareContentDuration
                : primaryContentDuration;
            var selectedAudioHasStream = selectedAudioSession.MediaInfo.HasAudioStream;

            var ffmpegArguments = BuildFfmpegArguments(
                request.Mode,
                request.AudioSource,
                primarySourceFullPath,
                compareSourceFullPath,
                outputFullPath,
                primaryStartTime,
                primaryContentDuration,
                primaryLeadingPad,
                primaryTrailingPad,
                primaryRenderSize.Width,
                primaryRenderSize.Height,
                compareStartTime,
                compareContentDuration,
                compareLeadingPad,
                compareTrailingPad,
                compareRenderSize.Width,
                compareRenderSize.Height,
                outputDuration,
                selectedAudioHasStream,
                selectedAudioStartTime,
                selectedAudioDuration,
                TimeSpan.Zero,
                outputSize.Height);

            return new CompareSideBySideExportPlan(
                outputFullPath,
                request.Mode,
                request.AudioSource,
                primarySourceFullPath,
                compareSourceFullPath,
                primaryStartTime,
                primaryContentDuration,
                primaryLeadingPad,
                primaryTrailingPad,
                compareStartTime,
                compareContentDuration,
                compareLeadingPad,
                compareTrailingPad,
                primaryBoundaryStrategy,
                compareBoundaryStrategy,
                outputDuration,
                primaryRenderSize.Width,
                primaryRenderSize.Height,
                compareRenderSize.Width,
                compareRenderSize.Height,
                outputSize.Width,
                outputSize.Height,
                selectedAudioHasStream,
                ffmpegArguments,
                toolPaths.FfmpegPath,
                toolPaths.FfprobePath);
        }

        private static CompareSideBySideExportPlan BuildWholeVideoPlan(
            CompareSideBySideExportRequest request,
            FfmpegCliToolPaths toolPaths,
            string primarySourceFullPath,
            string compareSourceFullPath,
            string outputFullPath,
            string outputDirectory)
        {
            var primarySession = request.PrimarySessionSnapshot;
            var compareSession = request.CompareSessionSnapshot;
            var primaryDuration = primarySession.MediaInfo.Duration;
            var compareDuration = compareSession.MediaInfo.Duration;
            if (primaryDuration <= TimeSpan.Zero || compareDuration <= TimeSpan.Zero)
            {
                throw new InvalidOperationException("Whole-video compare export requires known media durations for both panes.");
            }

            var primaryCurrentTime = FfmpegExportTiming.ClampTime(primarySession.Position.PresentationTime, primaryDuration);
            var compareCurrentTime = FfmpegExportTiming.ClampTime(compareSession.Position.PresentationTime, compareDuration);
            var syncAnchor = primaryCurrentTime >= compareCurrentTime
                ? primaryCurrentTime
                : compareCurrentTime;
            var primaryLeadingPad = syncAnchor - primaryCurrentTime;
            var compareLeadingPad = syncAnchor - compareCurrentTime;
            if (primaryLeadingPad < TimeSpan.Zero)
            {
                primaryLeadingPad = TimeSpan.Zero;
            }

            if (compareLeadingPad < TimeSpan.Zero)
            {
                compareLeadingPad = TimeSpan.Zero;
            }

            var primaryEndTime = primaryLeadingPad + primaryDuration;
            var compareEndTime = compareLeadingPad + compareDuration;
            var outputDuration = primaryEndTime >= compareEndTime ? primaryEndTime : compareEndTime;
            if (outputDuration <= TimeSpan.Zero)
            {
                throw new InvalidOperationException("Whole-video compare export could not resolve a usable output duration.");
            }

            var primaryTrailingPad = outputDuration - primaryEndTime;
            var compareTrailingPad = outputDuration - compareEndTime;
            var primaryRenderSize = ResolveRenderSize(primarySession.MediaInfo);
            var compareRenderSize = ResolveRenderSize(compareSession.MediaInfo);
            var outputSize = ResolveOutputCanvas(primaryRenderSize, compareRenderSize);
            var selectedAudioSession = request.AudioSource == CompareSideBySideExportAudioSource.Compare
                ? compareSession
                : primarySession;
            var selectedAudioHasStream = selectedAudioSession.MediaInfo.HasAudioStream;
            var selectedAudioLeadingPad = request.AudioSource == CompareSideBySideExportAudioSource.Compare
                ? compareLeadingPad
                : primaryLeadingPad;
            var selectedAudioDuration = request.AudioSource == CompareSideBySideExportAudioSource.Compare
                ? compareDuration
                : primaryDuration;

            var ffmpegArguments = BuildFfmpegArguments(
                request.Mode,
                request.AudioSource,
                primarySourceFullPath,
                compareSourceFullPath,
                outputFullPath,
                TimeSpan.Zero,
                primaryDuration,
                primaryLeadingPad,
                primaryTrailingPad,
                primaryRenderSize.Width,
                primaryRenderSize.Height,
                TimeSpan.Zero,
                compareDuration,
                compareLeadingPad,
                compareTrailingPad,
                compareRenderSize.Width,
                compareRenderSize.Height,
                outputDuration,
                selectedAudioHasStream,
                TimeSpan.Zero,
                selectedAudioDuration,
                selectedAudioLeadingPad,
                outputSize.Height);

            return new CompareSideBySideExportPlan(
                outputFullPath,
                request.Mode,
                request.AudioSource,
                primarySourceFullPath,
                compareSourceFullPath,
                TimeSpan.Zero,
                primaryDuration,
                primaryLeadingPad,
                primaryTrailingPad,
                TimeSpan.Zero,
                compareDuration,
                compareLeadingPad,
                compareTrailingPad,
                "whole-video",
                "whole-video",
                outputDuration,
                primaryRenderSize.Width,
                primaryRenderSize.Height,
                compareRenderSize.Width,
                compareRenderSize.Height,
                outputSize.Width,
                outputSize.Height,
                selectedAudioHasStream,
                ffmpegArguments,
                toolPaths.FfmpegPath,
                toolPaths.FfprobePath);
        }

        private static string BuildFfmpegArguments(
            CompareSideBySideExportMode mode,
            CompareSideBySideExportAudioSource audioSource,
            string primarySourceFullPath,
            string compareSourceFullPath,
            string outputFullPath,
            TimeSpan primaryStartTime,
            TimeSpan primaryContentDuration,
            TimeSpan primaryLeadingPad,
            TimeSpan primaryTrailingPad,
            int primaryRenderWidth,
            int primaryRenderHeight,
            TimeSpan compareStartTime,
            TimeSpan compareContentDuration,
            TimeSpan compareLeadingPad,
            TimeSpan compareTrailingPad,
            int compareRenderWidth,
            int compareRenderHeight,
            TimeSpan outputDuration,
            bool selectedAudioHasStream,
            TimeSpan selectedAudioStartTime,
            TimeSpan selectedAudioContentDuration,
            TimeSpan selectedAudioLeadingPad,
            int outputCanvasHeight)
        {
            var filterBuilder = new StringBuilder();
            AppendVideoFilter(
                filterBuilder,
                0,
                primaryStartTime,
                primaryContentDuration,
                primaryLeadingPad,
                primaryTrailingPad,
                primaryRenderWidth,
                primaryRenderHeight,
                outputCanvasHeight,
                "[primaryv]",
                mode == CompareSideBySideExportMode.Loop);
            filterBuilder.Append(';');
            AppendVideoFilter(
                filterBuilder,
                1,
                compareStartTime,
                compareContentDuration,
                compareLeadingPad,
                compareTrailingPad,
                compareRenderWidth,
                compareRenderHeight,
                outputCanvasHeight,
                "[comparev]",
                mode == CompareSideBySideExportMode.Loop);
            filterBuilder.Append(';');
            filterBuilder.Append("[primaryv][comparev]hstack=inputs=2,pad=width='ceil(iw/2)*2':height='ceil(ih/2)*2':x=0:y=0:color=black,setsar=1,format=yuv420p[vout]");

            if (selectedAudioHasStream)
            {
                filterBuilder.Append(';');
                AppendAudioFilter(
                    filterBuilder,
                    audioSource == CompareSideBySideExportAudioSource.Compare ? 1 : 0,
                    selectedAudioStartTime,
                    selectedAudioContentDuration,
                    selectedAudioLeadingPad,
                    outputDuration,
                    mode == CompareSideBySideExportMode.Loop);
            }

            var arguments = new StringBuilder();
            arguments.Append("-v error -y ");
            arguments.AppendFormat(CultureInfo.InvariantCulture, "-i \"{0}\" ", primarySourceFullPath);
            arguments.AppendFormat(CultureInfo.InvariantCulture, "-i \"{0}\" ", compareSourceFullPath);
            arguments.AppendFormat(CultureInfo.InvariantCulture, "-filter_complex \"{0}\" ", filterBuilder);
            arguments.Append("-map \"[vout]\" ");
            if (selectedAudioHasStream)
            {
                arguments.Append("-map \"[aout]\" ");
            }

            arguments.Append("-sn -dn -c:v libx264 -preset medium -crf 18 -pix_fmt yuv420p ");
            if (selectedAudioHasStream)
            {
                arguments.Append("-c:a aac -b:a 192k ");
            }
            else
            {
                arguments.Append("-an ");
            }

            arguments.AppendFormat(CultureInfo.InvariantCulture, "-movflags +faststart \"{0}\"", outputFullPath);
            return arguments.ToString();
        }

        private static void AppendVideoFilter(
            StringBuilder builder,
            int inputIndex,
            TimeSpan startTime,
            TimeSpan contentDuration,
            TimeSpan leadingPad,
            TimeSpan trailingPad,
            int renderWidth,
            int renderHeight,
            int canvasHeight,
            string outputLabel,
            bool includeTrim)
        {
            builder.AppendFormat(CultureInfo.InvariantCulture, "[{0}:v]", inputIndex);
            if (includeTrim)
            {
                builder.AppendFormat(
                    CultureInfo.InvariantCulture,
                    "trim=start={0}:duration={1},setpts=PTS-STARTPTS,",
                    FfmpegExportTiming.FormatFfmpegTime(startTime),
                    FfmpegExportTiming.FormatFfmpegTime(contentDuration));
            }
            else
            {
                builder.Append("setpts=PTS-STARTPTS,");
            }

            builder.Append("format=rgba,");
            builder.AppendFormat(
                CultureInfo.InvariantCulture,
                "scale={0}:{1}:flags=lanczos,",
                renderWidth,
                renderHeight);
            if (leadingPad > TimeSpan.Zero || trailingPad > TimeSpan.Zero)
            {
                builder.Append("tpad=");
                if (leadingPad > TimeSpan.Zero)
                {
                    builder.AppendFormat(
                        CultureInfo.InvariantCulture,
                        "start_mode=add:start_duration={0}",
                        FfmpegExportTiming.FormatFfmpegTime(leadingPad));
                    if (trailingPad > TimeSpan.Zero)
                    {
                        builder.Append(':');
                    }
                }

                if (trailingPad > TimeSpan.Zero)
                {
                    builder.AppendFormat(
                        CultureInfo.InvariantCulture,
                        "stop_mode=add:stop_duration={0}",
                        FfmpegExportTiming.FormatFfmpegTime(trailingPad));
                }

                builder.Append(',');
            }

            builder.AppendFormat(
                CultureInfo.InvariantCulture,
                "pad=width=iw:height={0}:x=0:y=(oh-ih)/2:color=black{1}",
                canvasHeight,
                outputLabel);
        }

        private static void AppendAudioFilter(
            StringBuilder builder,
            int inputIndex,
            TimeSpan startTime,
            TimeSpan contentDuration,
            TimeSpan leadingPad,
            TimeSpan outputDuration,
            bool includeTrim)
        {
            builder.AppendFormat(CultureInfo.InvariantCulture, "[{0}:a]", inputIndex);
            if (includeTrim)
            {
                builder.AppendFormat(
                    CultureInfo.InvariantCulture,
                    "atrim=start={0}:duration={1},asetpts=PTS-STARTPTS,",
                    FfmpegExportTiming.FormatFfmpegTime(startTime),
                    FfmpegExportTiming.FormatFfmpegTime(contentDuration));
            }
            else
            {
                builder.Append("asetpts=PTS-STARTPTS,");
            }

            if (leadingPad > TimeSpan.Zero)
            {
                var delayMilliseconds = Math.Max(0d, Math.Round(leadingPad.TotalMilliseconds));
                builder.AppendFormat(
                    CultureInfo.InvariantCulture,
                    "adelay={0}:all=1,",
                    delayMilliseconds.ToString("0", CultureInfo.InvariantCulture));
            }

            builder.AppendFormat(
                CultureInfo.InvariantCulture,
                "apad=whole_dur={0},atrim=duration={0}[aout]",
                FfmpegExportTiming.FormatFfmpegTime(outputDuration));
        }

        private static (int Width, int Height) ResolveRenderSize(VideoMediaInfo mediaInfo)
        {
            if (mediaInfo == null)
            {
                return (1, 1);
            }

            var pixelWidth = mediaInfo.PixelWidth > 0 ? mediaInfo.PixelWidth : 1;
            var pixelHeight = mediaInfo.PixelHeight > 0 ? mediaInfo.PixelHeight : 1;
            var displayWidth = mediaInfo.DisplayWidth.GetValueOrDefault(pixelWidth);
            var displayHeight = mediaInfo.DisplayHeight.GetValueOrDefault(pixelHeight);
            var canUseDisplaySize = displayWidth > 0 &&
                                    displayHeight > 0 &&
                                    displayWidth >= pixelWidth &&
                                    displayHeight >= pixelHeight;
            return canUseDisplaySize
                ? (displayWidth, displayHeight)
                : (pixelWidth, pixelHeight);
        }

        private static (int Width, int Height) ResolveOutputCanvas(
            (int Width, int Height) primaryRenderSize,
            (int Width, int Height) compareRenderSize)
        {
            var outputWidth = primaryRenderSize.Width + compareRenderSize.Width;
            var outputHeight = Math.Max(primaryRenderSize.Height, compareRenderSize.Height);
            if ((outputWidth & 1) != 0)
            {
                outputWidth++;
            }

            if ((outputHeight & 1) != 0)
            {
                outputHeight++;
            }

            return (Math.Max(2, outputWidth), Math.Max(2, outputHeight));
        }

        private static void ValidateLoopRange(LoopPlaybackPaneRangeSnapshot loopRange, string paneLabel)
        {
            if (loopRange == null || !loopRange.HasAnyMarkers)
            {
                throw new InvalidOperationException(paneLabel + " does not have a pane-local A/B range to export.");
            }

            if (!loopRange.HasLoopIn || !loopRange.HasLoopOut)
            {
                throw new InvalidOperationException(paneLabel + " compare export requires both pane-local loop markers.");
            }

            if (loopRange.HasPendingMarkers)
            {
                throw new InvalidOperationException(paneLabel + " compare export is disabled while pane-local markers are still pending exact frame identity.");
            }

            if (loopRange.IsInvalidRange)
            {
                throw new InvalidOperationException(paneLabel + " compare export is disabled because the pane-local loop range is invalid.");
            }
        }
    }
}
