using System;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FramePlayer.Core.Models;

namespace FramePlayer.Services
{
    internal static class NativeCompareSideBySideExportService
    {
        public static Task<CompareSideBySideExportResult> ExportAsync(
            CompareSideBySideExportPlan plan,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            ArgumentNullException.ThrowIfNull(plan);

            return Task.Run(
                () =>
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var outcome = NativeExportSupport.RunGraphExport(
                        plan.OutputFilePath,
                        BuildFilterGraph(plan),
                        BuildFileSources(plan),
                        plan.OutputWidth,
                        plan.OutputHeight,
                        plan.SelectedAudioHasStream,
                        cancellationToken);

                    var message = outcome.Message;
                    if (outcome.Succeeded)
                    {
                        message = "Compare export completed.";
                    }
                    else if (string.IsNullOrWhiteSpace(message))
                    {
                        message = "FFmpeg compare export failed.";
                    }

                    return new CompareSideBySideExportResult
                    {
                        Succeeded = outcome.Succeeded,
                        Plan = plan,
                        Message = message,
                        ExitCode = outcome.ExitCode,
                        Elapsed = outcome.Elapsed,
                        ProbedDuration = outcome.ProbedDuration,
                        ProbedVideoWidth = outcome.ProbedVideoWidth,
                        ProbedVideoHeight = outcome.ProbedVideoHeight,
                        ProbedHasAudioStream = outcome.ProbedHasAudioStream,
                        StandardOutput = outcome.StandardOutput,
                        StandardError = outcome.StandardError
                    };
                },
                cancellationToken);
        }

        internal static string BuildFilterGraph(CompareSideBySideExportPlan plan)
        {
            // Whole-video compare exports still know the source start/duration bounds.
            // Trimming from the known range before padding keeps filter timestamps
            // stable for the aligned whole-video export path.
            var includeTrim = true;
            var filterBuilder = new StringBuilder();

            AppendVideoChain(
                filterBuilder,
                new VideoChainSettings
                {
                    SourceInstanceName = "primaryvsrc",
                    StartTime = plan.PrimaryStartTime,
                    ContentDuration = plan.PrimaryContentDuration,
                    LeadingPad = plan.PrimaryLeadingPad,
                    TrailingPad = plan.PrimaryTrailingPad,
                    RenderWidth = plan.PrimaryRenderWidth,
                    RenderHeight = plan.PrimaryRenderHeight,
                    CanvasHeight = plan.OutputHeight,
                    ViewportSnapshot = plan.PrimaryViewportSnapshot,
                    OutputLabel = "primaryv"
                },
                includeTrim);
            filterBuilder.Append(';');
            AppendVideoChain(
                filterBuilder,
                new VideoChainSettings
                {
                    SourceInstanceName = "comparevsrc",
                    StartTime = plan.CompareStartTime,
                    ContentDuration = plan.CompareContentDuration,
                    LeadingPad = plan.CompareLeadingPad,
                    TrailingPad = plan.CompareTrailingPad,
                    RenderWidth = plan.CompareRenderWidth,
                    RenderHeight = plan.CompareRenderHeight,
                    CanvasHeight = plan.OutputHeight,
                    ViewportSnapshot = plan.CompareViewportSnapshot,
                    OutputLabel = "comparev"
                },
                includeTrim);
            filterBuilder.Append(';');
            filterBuilder.Append("[primaryv][comparev]hstack=inputs=2,pad=width='ceil(iw/2)*2':height='ceil(ih/2)*2':x=0:y=0:color=black,setsar=1,format=pix_fmts=yuv420p,buffersink@outv");

            if (plan.SelectedAudioHasStream)
            {
                filterBuilder.Append(';');
                AppendAudioChain(filterBuilder, plan, includeTrim);
            }

            return filterBuilder.ToString();
        }

        private static void AppendVideoChain(
            StringBuilder filterBuilder,
            VideoChainSettings settings,
            bool includeTrim)
        {
            filterBuilder.AppendFormat(
                CultureInfo.InvariantCulture,
                "movie@{0},",
                settings.SourceInstanceName);

            if (includeTrim)
            {
                filterBuilder.AppendFormat(
                    CultureInfo.InvariantCulture,
                    "trim=start={0}:duration={1},setpts=PTS-STARTPTS,",
                    NativeExportSupport.FormatFilterTime(settings.StartTime),
                    NativeExportSupport.FormatFilterTime(settings.ContentDuration));
            }
            else
            {
                filterBuilder.Append("setpts=PTS-STARTPTS,");
            }

            if (settings.ViewportSnapshot != null && settings.ViewportSnapshot.IsZoomed)
            {
                filterBuilder.AppendFormat(
                    CultureInfo.InvariantCulture,
                    "crop={0}:{1}:{2}:{3},",
                    settings.ViewportSnapshot.SourceCropWidth,
                    settings.ViewportSnapshot.SourceCropHeight,
                    settings.ViewportSnapshot.SourceCropX,
                    settings.ViewportSnapshot.SourceCropY);
            }

            filterBuilder.Append("format=rgba,");
            filterBuilder.AppendFormat(
                CultureInfo.InvariantCulture,
                "scale={0}:{1}:flags=lanczos,",
                settings.RenderWidth,
                settings.RenderHeight);

            if (settings.LeadingPad > TimeSpan.Zero || settings.TrailingPad > TimeSpan.Zero)
            {
                filterBuilder.Append("tpad=");
                if (settings.LeadingPad > TimeSpan.Zero)
                {
                    filterBuilder.AppendFormat(
                        CultureInfo.InvariantCulture,
                        "start_mode=add:start_duration={0}",
                        NativeExportSupport.FormatFilterTime(settings.LeadingPad));
                    if (settings.TrailingPad > TimeSpan.Zero)
                    {
                        filterBuilder.Append(':');
                    }
                }

                if (settings.TrailingPad > TimeSpan.Zero)
                {
                    filterBuilder.AppendFormat(
                        CultureInfo.InvariantCulture,
                        "stop_mode=add:stop_duration={0}",
                        NativeExportSupport.FormatFilterTime(settings.TrailingPad));
                }

                filterBuilder.Append(',');
            }

            filterBuilder.AppendFormat(
                CultureInfo.InvariantCulture,
                "pad=width=iw:height={0}:x=0:y=(oh-ih)/2:color=black[{1}]",
                settings.CanvasHeight,
                settings.OutputLabel);
        }

        private sealed class VideoChainSettings
        {
            public string SourceInstanceName { get; init; } = string.Empty;

            public TimeSpan StartTime { get; init; }

            public TimeSpan ContentDuration { get; init; }

            public TimeSpan LeadingPad { get; init; }

            public TimeSpan TrailingPad { get; init; }

            public int RenderWidth { get; init; }

            public int RenderHeight { get; init; }

            public int CanvasHeight { get; init; }

            public PaneViewportSnapshot? ViewportSnapshot { get; init; }

            public string OutputLabel { get; init; } = string.Empty;
        }

        private static void AppendAudioChain(
            StringBuilder filterBuilder,
            CompareSideBySideExportPlan plan,
            bool includeTrim)
        {
            var startTime = plan.AudioSource == CompareSideBySideExportAudioSource.Compare
                ? plan.CompareStartTime
                : plan.PrimaryStartTime;
            var contentDuration = plan.AudioSource == CompareSideBySideExportAudioSource.Compare
                ? plan.CompareContentDuration
                : plan.PrimaryContentDuration;
            var leadingPad = plan.AudioSource == CompareSideBySideExportAudioSource.Compare
                ? plan.CompareLeadingPad
                : plan.PrimaryLeadingPad;

            filterBuilder.Append("amovie@audiosrc,");

            if (includeTrim)
            {
                filterBuilder.AppendFormat(
                    CultureInfo.InvariantCulture,
                    "atrim=start={0}:duration={1},asetpts=PTS-STARTPTS,",
                    NativeExportSupport.FormatFilterTime(startTime),
                    NativeExportSupport.FormatFilterTime(contentDuration));
            }
            else
            {
                filterBuilder.Append("asetpts=PTS-STARTPTS,");
            }

            if (leadingPad > TimeSpan.Zero)
            {
                var delayMilliseconds = Math.Max(0d, Math.Round(leadingPad.TotalMilliseconds));
                filterBuilder.AppendFormat(
                    CultureInfo.InvariantCulture,
                    "adelay={0}:all=1,",
                    delayMilliseconds.ToString("0", CultureInfo.InvariantCulture));
            }

            filterBuilder.AppendFormat(
                CultureInfo.InvariantCulture,
                "apad=whole_dur={0},atrim=duration={0},aformat=sample_fmts=fltp,asetnsamples=n=1024:p=0,abuffersink@outa",
                NativeExportSupport.FormatFilterTime(plan.OutputDuration));
        }

        internal static NativeExportSupport.FilterFileSource[] BuildFileSources(
            CompareSideBySideExportPlan plan)
        {
            ArgumentNullException.ThrowIfNull(plan);

            var primaryVideoSource = new NativeExportSupport.FilterFileSource(
                "primaryvsrc",
                "movie",
                plan.PrimarySourceFilePath);
            var compareVideoSource = new NativeExportSupport.FilterFileSource(
                "comparevsrc",
                "movie",
                plan.CompareSourceFilePath);
            if (!plan.SelectedAudioHasStream)
            {
                return new[] { primaryVideoSource, compareVideoSource };
            }

            var audioSourcePath = plan.AudioSource == CompareSideBySideExportAudioSource.Compare
                ? plan.CompareSourceFilePath
                : plan.PrimarySourceFilePath;
            return new[]
            {
                primaryVideoSource,
                compareVideoSource,
                new NativeExportSupport.FilterFileSource(
                    "audiosrc",
                    "amovie",
                    audioSourcePath)
            };
        }
    }
}
