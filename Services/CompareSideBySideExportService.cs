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
        private readonly ExportHostClient _hostClient;

        public CompareSideBySideExportService()
        {
            _hostClient = new ExportHostClient();
        }

        public bool IsBundledRuntimeAvailable
        {
            get { return _hostClient.IsBundledRuntimeAvailable; }
        }

        public string GetRuntimeAvailabilityMessage()
        {
            return _hostClient.GetRuntimeAvailabilityMessage();
        }

        public CompareSideBySideExportPlan CreatePlan(CompareSideBySideExportRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);

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
                    primarySourceFullPath,
                    compareSourceFullPath,
                    outputFullPath);
            }

            return BuildLoopPlan(
                request,
                primarySourceFullPath,
                compareSourceFullPath,
                outputFullPath);
        }

        public async Task<CompareSideBySideExportResult> ExportAsync(
            CompareSideBySideExportRequest request,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var plan = CreatePlan(request);
            return await _hostClient.ExportCompareAsync(plan, cancellationToken).ConfigureAwait(false);
        }

        public static async Task<CompareSideBySideExportResult> ExportPlanAsync(
            CompareSideBySideExportPlan plan,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            ArgumentNullException.ThrowIfNull(plan);
            return await new ExportHostClient().ExportCompareAsync(plan, cancellationToken).ConfigureAwait(false);
        }

        private static CompareSideBySideExportPlan BuildLoopPlan(
            CompareSideBySideExportRequest request,
            string primarySourceFullPath,
            string compareSourceFullPath,
            string outputFullPath)
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
            var primaryViewportSnapshot = request.PrimaryViewportSnapshot ?? PaneViewportSnapshot.CreateFullFrame(
                request.PrimarySessionSnapshot.MediaInfo.PixelWidth,
                request.PrimarySessionSnapshot.MediaInfo.PixelHeight);
            var compareViewportSnapshot = request.CompareViewportSnapshot ?? PaneViewportSnapshot.CreateFullFrame(
                request.CompareSessionSnapshot.MediaInfo.PixelWidth,
                request.CompareSessionSnapshot.MediaInfo.PixelHeight);
            var selectedAudioSession = request.AudioSource == CompareSideBySideExportAudioSource.Compare
                ? request.CompareSessionSnapshot
                : request.PrimarySessionSnapshot;
            var selectedAudioStartTime = request.AudioSource == CompareSideBySideExportAudioSource.Compare
                ? compareStartTime
                : primaryStartTime;
            var selectedAudioDuration = request.AudioSource == CompareSideBySideExportAudioSource.Compare
                ? compareContentDuration
                : primaryContentDuration;
            var selectedAudioHasStream = selectedAudioSession.MediaInfo.HasAudioStream;

            var ffmpegArguments = BuildFfmpegArguments(
                new CompareFilterComplexRequest
                {
                    Mode = request.Mode,
                    PrimarySourceFullPath = primarySourceFullPath,
                    CompareSourceFullPath = compareSourceFullPath,
                    OutputFilePath = outputFullPath,
                    PrimaryVideo = new VideoFilterSegment
                    {
                        InputIndex = 0,
                        StartTime = primaryStartTime,
                        ContentDuration = primaryContentDuration,
                        LeadingPad = primaryLeadingPad,
                        TrailingPad = primaryTrailingPad,
                        RenderWidth = primaryRenderSize.Width,
                        RenderHeight = primaryRenderSize.Height,
                        CanvasHeight = outputSize.Height,
                        ViewportSnapshot = primaryViewportSnapshot,
                        OutputLabel = "[primaryv]"
                    },
                    CompareVideo = new VideoFilterSegment
                    {
                        InputIndex = 1,
                        StartTime = compareStartTime,
                        ContentDuration = compareContentDuration,
                        LeadingPad = compareLeadingPad,
                        TrailingPad = compareTrailingPad,
                        RenderWidth = compareRenderSize.Width,
                        RenderHeight = compareRenderSize.Height,
                        CanvasHeight = outputSize.Height,
                        ViewportSnapshot = compareViewportSnapshot,
                        OutputLabel = "[comparev]"
                    },
                    SelectedAudio = new AudioFilterSegment
                    {
                        HasStream = selectedAudioHasStream,
                        InputIndex = request.AudioSource == CompareSideBySideExportAudioSource.Compare ? 1 : 0,
                        StartTime = selectedAudioStartTime,
                        ContentDuration = selectedAudioDuration,
                        LeadingPad = TimeSpan.Zero,
                        OutputDuration = outputDuration
                    }
                });

            return new CompareSideBySideExportPlan
            {
                OutputFilePath = outputFullPath,
                Mode = request.Mode,
                AudioSource = request.AudioSource,
                PrimarySourceFilePath = primarySourceFullPath,
                CompareSourceFilePath = compareSourceFullPath,
                PrimaryStartTime = primaryStartTime,
                PrimaryContentDuration = primaryContentDuration,
                PrimaryLeadingPad = primaryLeadingPad,
                PrimaryTrailingPad = primaryTrailingPad,
                CompareStartTime = compareStartTime,
                CompareContentDuration = compareContentDuration,
                CompareLeadingPad = compareLeadingPad,
                CompareTrailingPad = compareTrailingPad,
                PrimaryEndBoundaryStrategy = primaryBoundaryStrategy,
                CompareEndBoundaryStrategy = compareBoundaryStrategy,
                OutputDuration = outputDuration,
                PrimaryRenderWidth = primaryRenderSize.Width,
                PrimaryRenderHeight = primaryRenderSize.Height,
                CompareRenderWidth = compareRenderSize.Width,
                CompareRenderHeight = compareRenderSize.Height,
                OutputWidth = outputSize.Width,
                OutputHeight = outputSize.Height,
                PrimaryViewportSnapshot = primaryViewportSnapshot,
                CompareViewportSnapshot = compareViewportSnapshot,
                SelectedAudioHasStream = selectedAudioHasStream,
                FfmpegArguments = ffmpegArguments,
                FfmpegPath = string.Empty,
                FfprobePath = string.Empty
            };
        }

        private static CompareSideBySideExportPlan BuildWholeVideoPlan(
            CompareSideBySideExportRequest request,
            string primarySourceFullPath,
            string compareSourceFullPath,
            string outputFullPath)
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
            var primaryViewportSnapshot = request.PrimaryViewportSnapshot ?? PaneViewportSnapshot.CreateFullFrame(
                primarySession.MediaInfo.PixelWidth,
                primarySession.MediaInfo.PixelHeight);
            var compareViewportSnapshot = request.CompareViewportSnapshot ?? PaneViewportSnapshot.CreateFullFrame(
                compareSession.MediaInfo.PixelWidth,
                compareSession.MediaInfo.PixelHeight);
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
                new CompareFilterComplexRequest
                {
                    Mode = request.Mode,
                    PrimarySourceFullPath = primarySourceFullPath,
                    CompareSourceFullPath = compareSourceFullPath,
                    OutputFilePath = outputFullPath,
                    PrimaryVideo = new VideoFilterSegment
                    {
                        InputIndex = 0,
                        StartTime = TimeSpan.Zero,
                        ContentDuration = primaryDuration,
                        LeadingPad = primaryLeadingPad,
                        TrailingPad = primaryTrailingPad,
                        RenderWidth = primaryRenderSize.Width,
                        RenderHeight = primaryRenderSize.Height,
                        CanvasHeight = outputSize.Height,
                        ViewportSnapshot = primaryViewportSnapshot,
                        OutputLabel = "[primaryv]"
                    },
                    CompareVideo = new VideoFilterSegment
                    {
                        InputIndex = 1,
                        StartTime = TimeSpan.Zero,
                        ContentDuration = compareDuration,
                        LeadingPad = compareLeadingPad,
                        TrailingPad = compareTrailingPad,
                        RenderWidth = compareRenderSize.Width,
                        RenderHeight = compareRenderSize.Height,
                        CanvasHeight = outputSize.Height,
                        ViewportSnapshot = compareViewportSnapshot,
                        OutputLabel = "[comparev]"
                    },
                    SelectedAudio = new AudioFilterSegment
                    {
                        HasStream = selectedAudioHasStream,
                        InputIndex = request.AudioSource == CompareSideBySideExportAudioSource.Compare ? 1 : 0,
                        StartTime = TimeSpan.Zero,
                        ContentDuration = selectedAudioDuration,
                        LeadingPad = selectedAudioLeadingPad,
                        OutputDuration = outputDuration
                    }
                });

            return new CompareSideBySideExportPlan
            {
                OutputFilePath = outputFullPath,
                Mode = request.Mode,
                AudioSource = request.AudioSource,
                PrimarySourceFilePath = primarySourceFullPath,
                CompareSourceFilePath = compareSourceFullPath,
                PrimaryStartTime = TimeSpan.Zero,
                PrimaryContentDuration = primaryDuration,
                PrimaryLeadingPad = primaryLeadingPad,
                PrimaryTrailingPad = primaryTrailingPad,
                CompareStartTime = TimeSpan.Zero,
                CompareContentDuration = compareDuration,
                CompareLeadingPad = compareLeadingPad,
                CompareTrailingPad = compareTrailingPad,
                PrimaryEndBoundaryStrategy = "whole-video",
                CompareEndBoundaryStrategy = "whole-video",
                OutputDuration = outputDuration,
                PrimaryRenderWidth = primaryRenderSize.Width,
                PrimaryRenderHeight = primaryRenderSize.Height,
                CompareRenderWidth = compareRenderSize.Width,
                CompareRenderHeight = compareRenderSize.Height,
                OutputWidth = outputSize.Width,
                OutputHeight = outputSize.Height,
                PrimaryViewportSnapshot = primaryViewportSnapshot,
                CompareViewportSnapshot = compareViewportSnapshot,
                SelectedAudioHasStream = selectedAudioHasStream,
                FfmpegArguments = ffmpegArguments,
                FfmpegPath = string.Empty,
                FfprobePath = string.Empty
            };
        }

        private static string BuildFfmpegArguments(CompareFilterComplexRequest request)
        {
            // Even whole-video compare exports have known start/duration bounds.
            // Keeping explicit trim/atrim in both modes normalizes timestamps before
            // leading/trailing pad is applied, which avoids mux-time packet errors
            // on aligned whole-video exports.
            var includeTrim = true;
            var filterBuilder = new StringBuilder();
            AppendVideoFilter(filterBuilder, request.PrimaryVideo, includeTrim);
            filterBuilder.Append(';');
            AppendVideoFilter(filterBuilder, request.CompareVideo, includeTrim);
            filterBuilder.Append(';');
            filterBuilder.Append("[primaryv][comparev]hstack=inputs=2,pad=width='ceil(iw/2)*2':height='ceil(ih/2)*2':x=0:y=0:color=black,setsar=1,format=yuv420p[vout]");

            if (request.SelectedAudio != null && request.SelectedAudio.HasStream)
            {
                filterBuilder.Append(';');
                AppendAudioFilter(filterBuilder, request.SelectedAudio, includeTrim);
            }

            var arguments = new StringBuilder();
            arguments.Append("-v error -y ");
            arguments.AppendFormat(CultureInfo.InvariantCulture, "-i \"{0}\" ", request.PrimarySourceFullPath);
            arguments.AppendFormat(CultureInfo.InvariantCulture, "-i \"{0}\" ", request.CompareSourceFullPath);
            arguments.AppendFormat(CultureInfo.InvariantCulture, "-filter_complex \"{0}\" ", filterBuilder);
            arguments.Append("-map \"[vout]\" ");
            if (request.SelectedAudio != null && request.SelectedAudio.HasStream)
            {
                arguments.Append("-map \"[aout]\" ");
            }

            arguments.Append("-sn -dn -c:v libx264 -preset medium -crf 18 -pix_fmt yuv420p ");
            if (request.SelectedAudio != null && request.SelectedAudio.HasStream)
            {
                arguments.Append("-c:a aac -b:a 192k ");
            }
            else
            {
                arguments.Append("-an ");
            }

            arguments.AppendFormat(CultureInfo.InvariantCulture, "-movflags +faststart \"{0}\"", request.OutputFilePath);
            return arguments.ToString();
        }

        private static void AppendVideoFilter(StringBuilder builder, VideoFilterSegment segment, bool includeTrim)
        {
            builder.AppendFormat(CultureInfo.InvariantCulture, "[{0}:v]", segment.InputIndex);
            if (includeTrim)
            {
                builder.AppendFormat(
                    CultureInfo.InvariantCulture,
                    "trim=start={0}:duration={1},setpts=PTS-STARTPTS,",
                    FfmpegExportTiming.FormatFfmpegTime(segment.StartTime),
                    FfmpegExportTiming.FormatFfmpegTime(segment.ContentDuration));
            }
            else
            {
                builder.Append("setpts=PTS-STARTPTS,");
            }

            if (segment.ViewportSnapshot != null && segment.ViewportSnapshot.IsZoomed)
            {
                builder.AppendFormat(
                    CultureInfo.InvariantCulture,
                    "crop={0}:{1}:{2}:{3},",
                    segment.ViewportSnapshot.SourceCropWidth,
                    segment.ViewportSnapshot.SourceCropHeight,
                    segment.ViewportSnapshot.SourceCropX,
                    segment.ViewportSnapshot.SourceCropY);
            }

            builder.Append("format=rgba,");
            builder.AppendFormat(
                CultureInfo.InvariantCulture,
                "scale={0}:{1}:flags=lanczos,",
                segment.RenderWidth,
                segment.RenderHeight);
            if (segment.LeadingPad > TimeSpan.Zero || segment.TrailingPad > TimeSpan.Zero)
            {
                builder.Append("tpad=");
                if (segment.LeadingPad > TimeSpan.Zero)
                {
                    builder.AppendFormat(
                        CultureInfo.InvariantCulture,
                        "start_mode=add:start_duration={0}",
                        FfmpegExportTiming.FormatFfmpegTime(segment.LeadingPad));
                    if (segment.TrailingPad > TimeSpan.Zero)
                    {
                        builder.Append(':');
                    }
                }

                if (segment.TrailingPad > TimeSpan.Zero)
                {
                    builder.AppendFormat(
                        CultureInfo.InvariantCulture,
                        "stop_mode=add:stop_duration={0}",
                        FfmpegExportTiming.FormatFfmpegTime(segment.TrailingPad));
                }

                builder.Append(',');
            }

            builder.AppendFormat(
                CultureInfo.InvariantCulture,
                "pad=width=iw:height={0}:x=0:y=(oh-ih)/2:color=black{1}",
                segment.CanvasHeight,
                segment.OutputLabel);
        }

        private static void AppendAudioFilter(StringBuilder builder, AudioFilterSegment segment, bool includeTrim)
        {
            builder.AppendFormat(CultureInfo.InvariantCulture, "[{0}:a]", segment.InputIndex);
            if (includeTrim)
            {
                builder.AppendFormat(
                    CultureInfo.InvariantCulture,
                    "atrim=start={0}:duration={1},asetpts=PTS-STARTPTS,",
                    FfmpegExportTiming.FormatFfmpegTime(segment.StartTime),
                    FfmpegExportTiming.FormatFfmpegTime(segment.ContentDuration));
            }
            else
            {
                builder.Append("asetpts=PTS-STARTPTS,");
            }

            if (segment.LeadingPad > TimeSpan.Zero)
            {
                var delayMilliseconds = Math.Max(0d, Math.Round(segment.LeadingPad.TotalMilliseconds));
                builder.AppendFormat(
                    CultureInfo.InvariantCulture,
                    "adelay={0}:all=1,",
                    delayMilliseconds.ToString("0", CultureInfo.InvariantCulture));
            }

            builder.AppendFormat(
                CultureInfo.InvariantCulture,
                "apad=whole_dur={0},atrim=duration={0}[aout]",
                FfmpegExportTiming.FormatFfmpegTime(segment.OutputDuration));
        }

        private sealed class CompareFilterComplexRequest
        {
            public CompareSideBySideExportMode Mode { get; init; }

            public string PrimarySourceFullPath { get; init; } = string.Empty;

            public string CompareSourceFullPath { get; init; } = string.Empty;

            public string OutputFilePath { get; init; } = string.Empty;

            public VideoFilterSegment PrimaryVideo { get; init; }

            public VideoFilterSegment CompareVideo { get; init; }

            public AudioFilterSegment SelectedAudio { get; init; }
        }

        private sealed class VideoFilterSegment
        {
            public int InputIndex { get; init; }

            public TimeSpan StartTime { get; init; }

            public TimeSpan ContentDuration { get; init; }

            public TimeSpan LeadingPad { get; init; }

            public TimeSpan TrailingPad { get; init; }

            public int RenderWidth { get; init; }

            public int RenderHeight { get; init; }

            public int CanvasHeight { get; init; }

            public PaneViewportSnapshot ViewportSnapshot { get; init; }

            public string OutputLabel { get; init; } = string.Empty;
        }

        private sealed class AudioFilterSegment
        {
            public bool HasStream { get; init; }

            public int InputIndex { get; init; }

            public TimeSpan StartTime { get; init; }

            public TimeSpan ContentDuration { get; init; }

            public TimeSpan LeadingPad { get; init; }

            public TimeSpan OutputDuration { get; init; }
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
