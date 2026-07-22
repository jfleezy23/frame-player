using System;
using System.IO;
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

        public static bool IsBundledRuntimeAvailable
        {
            get { return ExportHostClient.IsBundledRuntimeAvailable; }
        }

        public static string GetRuntimeAvailabilityMessage()
        {
            return ExportHostClient.GetRuntimeAvailabilityMessage();
        }

        public static CompareSideBySideExportPlan CreatePlan(CompareSideBySideExportRequest request)
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
            var selectedAudioHasStream = selectedAudioSession.MediaInfo.HasAudioStream;

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
                FfmpegArguments = string.Empty,
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
                FfmpegArguments = string.Empty,
                FfmpegPath = string.Empty,
                FfprobePath = string.Empty
            };
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
