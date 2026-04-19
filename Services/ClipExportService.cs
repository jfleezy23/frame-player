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
        private readonly ExportHostClient _hostClient;

        public ClipExportService()
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

        public ClipExportPlan CreatePlan(ClipExportRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            string sourceFullPath;
            string outputFullPath;
            string outputDirectory;
            ResolvePlanPaths(request, out sourceFullPath, out outputFullPath, out outputDirectory);
            var loopRange = ResolveLoopRange(request);
            var viewportSnapshot = ResolveViewportSnapshot(request);
            TimeSpan startTime;
            TimeSpan endTimeExclusive;
            string endBoundaryStrategy;
            ResolveExportTimes(request, loopRange, out startTime, out endTimeExclusive, out endBoundaryStrategy);
            Directory.CreateDirectory(outputDirectory);

            int outputWidth;
            int outputHeight;
            ResolveOutputDimensions(request, viewportSnapshot, out outputWidth, out outputHeight);
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
                string.Empty,
                string.Empty);
        }

        private static void ResolvePlanPaths(
            ClipExportRequest request,
            out string sourceFullPath,
            out string outputFullPath,
            out string outputDirectory)
        {
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

            sourceFullPath = Path.GetFullPath(request.SourceFilePath);
            outputFullPath = Path.GetFullPath(request.OutputFilePath);
            if (string.Equals(sourceFullPath, outputFullPath, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Clip export cannot overwrite the reviewed source file.");
            }

            outputDirectory = Path.GetDirectoryName(outputFullPath);
            if (string.IsNullOrWhiteSpace(outputDirectory))
            {
                throw new InvalidOperationException("Clip export requires a valid destination folder.");
            }
        }

        private static LoopPlaybackPaneRangeSnapshot ResolveLoopRange(ClipExportRequest request)
        {
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

            return loopRange;
        }

        private static PaneViewportSnapshot ResolveViewportSnapshot(ClipExportRequest request)
        {
            if (request.ViewportSnapshot != null)
            {
                return request.ViewportSnapshot;
            }

            var mediaInfo = request.SessionSnapshot != null
                ? request.SessionSnapshot.MediaInfo
                : VideoMediaInfo.Empty;
            return PaneViewportSnapshot.CreateFullFrame(
                mediaInfo.PixelWidth,
                mediaInfo.PixelHeight);
        }

        private static void ResolveExportTimes(
            ClipExportRequest request,
            LoopPlaybackPaneRangeSnapshot loopRange,
            out TimeSpan startTime,
            out TimeSpan endTimeExclusive,
            out string endBoundaryStrategy)
        {
            var mediaDuration = request.SessionSnapshot != null
                ? request.SessionSnapshot.MediaInfo.Duration
                : TimeSpan.Zero;
            startTime = FfmpegExportTiming.ClampTime(loopRange.LoopIn.PresentationTime, mediaDuration);
            endTimeExclusive = FfmpegExportTiming.BuildExclusiveEndTime(
                request.Engine,
                request.SessionSnapshot,
                loopRange.LoopOut,
                mediaDuration,
                out endBoundaryStrategy);
            if (endTimeExclusive <= startTime)
            {
                throw new InvalidOperationException("Clip export could not resolve a valid exclusive end boundary.");
            }
        }

        private static void ResolveOutputDimensions(
            ClipExportRequest request,
            PaneViewportSnapshot viewportSnapshot,
            out int outputWidth,
            out int outputHeight)
        {
            var mediaInfo = request.SessionSnapshot != null
                ? request.SessionSnapshot.MediaInfo
                : VideoMediaInfo.Empty;
            outputWidth = mediaInfo != null
                ? Math.Max(1, mediaInfo.PixelWidth)
                : Math.Max(1, viewportSnapshot.SourcePixelWidth);
            outputHeight = mediaInfo != null
                ? Math.Max(1, mediaInfo.PixelHeight)
                : Math.Max(1, viewportSnapshot.SourcePixelHeight);
        }

        public async Task<ClipExportResult> ExportAsync(ClipExportRequest request, CancellationToken cancellationToken = default(CancellationToken))
        {
            var plan = CreatePlan(request);
            return await _hostClient.ExportClipAsync(plan, cancellationToken).ConfigureAwait(false);
        }

        public static async Task<ClipExportResult> ExportPlanAsync(ClipExportPlan plan, CancellationToken cancellationToken = default(CancellationToken))
        {
            ArgumentNullException.ThrowIfNull(plan);
            return await new ExportHostClient().ExportClipAsync(plan, cancellationToken).ConfigureAwait(false);
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
