using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FramePlayer.Core.Models;

namespace FramePlayer.Services
{
    internal static class NativeClipExportService
    {
        public static Task<ClipExportResult> ExportAsync(
            ClipExportPlan plan,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            ArgumentNullException.ThrowIfNull(plan);

            return Task.Run(
                () =>
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var includeAudio = false;
                    if (MediaProbeService.TryProbeVideoMediaInfo(plan.SourceFilePath, out var mediaInfo, out _))
                    {
                        includeAudio = mediaInfo.HasAudioStream;
                    }

                    var viewport = plan.ViewportSnapshot;
                    var sourcePixelWidth = viewport != null ? viewport.SourcePixelWidth : 1;
                    var sourcePixelHeight = viewport != null ? viewport.SourcePixelHeight : 1;
                    var outputWidth = viewport != null && viewport.IsZoomed
                        ? viewport.SourcePixelWidth
                        : Math.Max(1, sourcePixelWidth);
                    var outputHeight = viewport != null && viewport.IsZoomed
                        ? viewport.SourcePixelHeight
                        : Math.Max(1, sourcePixelHeight);
                    var outcome = NativeExportSupport.RunGraphExport(
                        plan.OutputFilePath,
                        BuildFilterGraph(plan, includeAudio),
                        outputWidth,
                        outputHeight,
                        includeAudio,
                        cancellationToken);

                    var message = outcome.Message;
                    if (outcome.Succeeded)
                    {
                        message = "Clip export completed.";
                    }
                    else if (string.IsNullOrWhiteSpace(message))
                    {
                        message = "FFmpeg clip export failed.";
                    }

                    return new ClipExportResult(
                        outcome.Succeeded,
                        plan,
                        message,
                        outcome.ExitCode,
                        outcome.Elapsed,
                        outcome.ProbedDuration,
                        outcome.StandardOutput,
                        outcome.StandardError);
                },
                cancellationToken);
        }

        private static string BuildFilterGraph(ClipExportPlan plan, bool includeAudio)
        {
            var duration = plan.Duration;
            if (duration <= TimeSpan.Zero)
            {
                throw new InvalidOperationException("Clip export requires a positive duration.");
            }

            var sourcePath = NativeExportSupport.EscapeFilterFilePath(plan.SourceFilePath);
            var startTime = NativeExportSupport.FormatFilterTime(plan.StartTime);
            var durationText = NativeExportSupport.FormatFilterTime(duration);
            var filterGraph = new StringBuilder();

            filterGraph.AppendFormat(
                System.Globalization.CultureInfo.InvariantCulture,
                "movie=filename='{0}',trim=start={1}:duration={2},setpts=PTS-STARTPTS,",
                sourcePath,
                startTime,
                durationText);

            if (plan.ViewportSnapshot != null && plan.ViewportSnapshot.IsZoomed)
            {
                filterGraph.AppendFormat(
                    System.Globalization.CultureInfo.InvariantCulture,
                    "crop={0}:{1}:{2}:{3},scale={4}:{5}:flags=lanczos,",
                    plan.ViewportSnapshot.SourceCropWidth,
                    plan.ViewportSnapshot.SourceCropHeight,
                    plan.ViewportSnapshot.SourceCropX,
                    plan.ViewportSnapshot.SourceCropY,
                    plan.ViewportSnapshot.SourcePixelWidth,
                    plan.ViewportSnapshot.SourcePixelHeight);
            }

            filterGraph.Append("format=pix_fmts=yuv420p,buffersink@outv");

            if (includeAudio)
            {
                filterGraph.Append(';');
                filterGraph.AppendFormat(
                    System.Globalization.CultureInfo.InvariantCulture,
                    "amovie=filename='{0}',atrim=start={1}:duration={2},asetpts=PTS-STARTPTS,aformat=sample_fmts=fltp,asetnsamples=n=1024:p=0,abuffersink@outa",
                    sourcePath,
                    startTime,
                    durationText);
            }

            return filterGraph.ToString();
        }
    }
}
