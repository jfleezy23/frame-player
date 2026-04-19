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
                        plan.OutputWidth,
                        plan.OutputHeight,
                        plan.SelectedAudioHasStream,
                        cancellationToken);

                    return new CompareSideBySideExportResult
                    {
                        Succeeded = outcome.Succeeded,
                        Plan = plan,
                        Message = outcome.Succeeded
                            ? "Compare export completed."
                            : string.IsNullOrWhiteSpace(outcome.Message)
                                ? "FFmpeg compare export failed."
                                : outcome.Message,
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

        private static string BuildFilterGraph(CompareSideBySideExportPlan plan)
        {
            // Whole-video compare exports still know the source start/duration bounds.
            // Trimming from the known range before padding keeps filter timestamps
            // stable for the aligned whole-video export path.
            var includeTrim = true;
            var filterBuilder = new StringBuilder();

            AppendVideoChain(
                filterBuilder,
                plan.PrimarySourceFilePath,
                plan.PrimaryStartTime,
                plan.PrimaryContentDuration,
                plan.PrimaryLeadingPad,
                plan.PrimaryTrailingPad,
                plan.PrimaryRenderWidth,
                plan.PrimaryRenderHeight,
                plan.OutputHeight,
                plan.PrimaryViewportSnapshot,
                "primaryv",
                includeTrim);
            filterBuilder.Append(';');
            AppendVideoChain(
                filterBuilder,
                plan.CompareSourceFilePath,
                plan.CompareStartTime,
                plan.CompareContentDuration,
                plan.CompareLeadingPad,
                plan.CompareTrailingPad,
                plan.CompareRenderWidth,
                plan.CompareRenderHeight,
                plan.OutputHeight,
                plan.CompareViewportSnapshot,
                "comparev",
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
            string sourceFilePath,
            TimeSpan startTime,
            TimeSpan contentDuration,
            TimeSpan leadingPad,
            TimeSpan trailingPad,
            int renderWidth,
            int renderHeight,
            int canvasHeight,
            PaneViewportSnapshot viewportSnapshot,
            string outputLabel,
            bool includeTrim)
        {
            filterBuilder.AppendFormat(
                CultureInfo.InvariantCulture,
                "movie=filename='{0}',",
                NativeExportSupport.EscapeFilterFilePath(sourceFilePath));

            if (includeTrim)
            {
                filterBuilder.AppendFormat(
                    CultureInfo.InvariantCulture,
                    "trim=start={0}:duration={1},setpts=PTS-STARTPTS,",
                    NativeExportSupport.FormatFilterTime(startTime),
                    NativeExportSupport.FormatFilterTime(contentDuration));
            }
            else
            {
                filterBuilder.Append("setpts=PTS-STARTPTS,");
            }

            if (viewportSnapshot != null && viewportSnapshot.IsZoomed)
            {
                filterBuilder.AppendFormat(
                    CultureInfo.InvariantCulture,
                    "crop={0}:{1}:{2}:{3},",
                    viewportSnapshot.SourceCropWidth,
                    viewportSnapshot.SourceCropHeight,
                    viewportSnapshot.SourceCropX,
                    viewportSnapshot.SourceCropY);
            }

            filterBuilder.Append("format=rgba,");
            filterBuilder.AppendFormat(
                CultureInfo.InvariantCulture,
                "scale={0}:{1}:flags=lanczos,",
                renderWidth,
                renderHeight);

            if (leadingPad > TimeSpan.Zero || trailingPad > TimeSpan.Zero)
            {
                filterBuilder.Append("tpad=");
                if (leadingPad > TimeSpan.Zero)
                {
                    filterBuilder.AppendFormat(
                        CultureInfo.InvariantCulture,
                        "start_mode=add:start_duration={0}",
                        NativeExportSupport.FormatFilterTime(leadingPad));
                    if (trailingPad > TimeSpan.Zero)
                    {
                        filterBuilder.Append(':');
                    }
                }

                if (trailingPad > TimeSpan.Zero)
                {
                    filterBuilder.AppendFormat(
                        CultureInfo.InvariantCulture,
                        "stop_mode=add:stop_duration={0}",
                        NativeExportSupport.FormatFilterTime(trailingPad));
                }

                filterBuilder.Append(',');
            }

            filterBuilder.AppendFormat(
                CultureInfo.InvariantCulture,
                "pad=width=iw:height={0}:x=0:y=(oh-ih)/2:color=black[{1}]",
                canvasHeight,
                outputLabel);
        }

        private static void AppendAudioChain(
            StringBuilder filterBuilder,
            CompareSideBySideExportPlan plan,
            bool includeTrim)
        {
            var sourceFilePath = plan.AudioSource == CompareSideBySideExportAudioSource.Compare
                ? plan.CompareSourceFilePath
                : plan.PrimarySourceFilePath;
            var startTime = plan.AudioSource == CompareSideBySideExportAudioSource.Compare
                ? plan.CompareStartTime
                : plan.PrimaryStartTime;
            var contentDuration = plan.AudioSource == CompareSideBySideExportAudioSource.Compare
                ? plan.CompareContentDuration
                : plan.PrimaryContentDuration;
            var leadingPad = plan.AudioSource == CompareSideBySideExportAudioSource.Compare
                ? plan.CompareLeadingPad
                : plan.PrimaryLeadingPad;

            filterBuilder.AppendFormat(
                CultureInfo.InvariantCulture,
                "amovie=filename='{0}',",
                NativeExportSupport.EscapeFilterFilePath(sourceFilePath));

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
    }
}
