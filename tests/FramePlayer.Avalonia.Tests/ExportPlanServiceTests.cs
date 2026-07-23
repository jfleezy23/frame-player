using System;
using System.IO;
using FramePlayer.Core.Models;
using FramePlayer.Services;
using Xunit;

namespace FramePlayer.Avalonia.Tests
{
    public sealed class ExportPlanServiceTests
    {
        [Fact]
        public void ClipPlan_ResolvesTimesViewportAndEvenOutputDimensions()
        {
            using var files = new TemporaryExportFiles();
            var session = CreateSession(files.PrimaryVideoPath, TimeSpan.FromSeconds(10), 641, 479);
            var loopRange = CreateLoopRange(
                "pane-primary",
                files.PrimaryVideoPath,
                TimeSpan.FromSeconds(10),
                TimeSpan.FromSeconds(2),
                60L,
                TimeSpan.FromSeconds(4),
                120L);
            var viewport = new PaneViewportSnapshot(2d, 0.5d, 0.5d, 641, 479, 7, 5, 319, 239);

            var plan = ClipExportService.CreatePlan(new ClipExportRequest(
                files.PrimaryVideoPath,
                files.ClipOutputPath,
                "Primary",
                "pane-primary",
                isPaneLocal: true,
                session,
                loopRange,
                engine: null,
                viewport));

            Assert.Equal(Path.GetFullPath(files.PrimaryVideoPath), plan.SourceFilePath);
            Assert.Equal(Path.GetFullPath(files.ClipOutputPath), plan.OutputFilePath);
            Assert.Equal(TimeSpan.FromSeconds(2), plan.StartTime);
            Assert.Equal(TimeSpan.FromSeconds(4) + session.MediaInfo.PositionStep, plan.EndTimeExclusive);
            Assert.Equal(plan.EndTimeExclusive - plan.StartTime, plan.Duration);
            Assert.Equal(60L, plan.StartFrameIndex);
            Assert.Equal(120L, plan.EndFrameIndex);
            Assert.Equal("position-step", plan.EndBoundaryStrategy);
            Assert.Same(viewport, plan.ViewportSnapshot);
            Assert.Empty(plan.FfmpegArguments);
            var filterGraph = NativeClipExportService.BuildFilterGraph(plan, includeAudio: true);
            Assert.Contains("crop=319:239:7:5,scale=642:480:flags=lanczos", filterGraph, StringComparison.Ordinal);
            Assert.Contains("trim=start=2", filterGraph, StringComparison.Ordinal);
            Assert.Contains("amovie@clipasrc", filterGraph, StringComparison.Ordinal);
        }

        [Fact]
        public void ClipPlan_RejectsMissingOrUnsafeInputs()
        {
            using var files = new TemporaryExportFiles();
            var session = CreateSession(files.PrimaryVideoPath, TimeSpan.FromSeconds(10), 640, 480);
            var validRange = CreateLoopRange(
                "pane-primary",
                files.PrimaryVideoPath,
                TimeSpan.FromSeconds(10),
                TimeSpan.FromSeconds(2),
                60L,
                TimeSpan.FromSeconds(4),
                120L);

            Assert.Throws<ArgumentNullException>(() => ClipExportService.CreatePlan(null!));
            Assert.Throws<InvalidOperationException>(() => ClipExportService.CreatePlan(CreateClipRequest(
                string.Empty,
                files.ClipOutputPath,
                session,
                validRange)));
            Assert.Throws<FileNotFoundException>(() => ClipExportService.CreatePlan(CreateClipRequest(
                Path.Combine(files.DirectoryPath, "missing.mp4"),
                files.ClipOutputPath,
                session,
                validRange)));
            Assert.Throws<InvalidOperationException>(() => ClipExportService.CreatePlan(CreateClipRequest(
                files.PrimaryVideoPath,
                string.Empty,
                session,
                validRange)));
            Assert.Throws<InvalidOperationException>(() => ClipExportService.CreatePlan(CreateClipRequest(
                files.PrimaryVideoPath,
                files.PrimaryVideoPath,
                session,
                validRange)));
            Assert.Throws<InvalidOperationException>(() => ClipExportService.CreatePlan(CreateClipRequest(
                files.PrimaryVideoPath,
                files.ClipOutputPath,
                session,
                loopRange: null!)));

            var pendingRange = new LoopPlaybackPaneRangeSnapshot(
                "pane-primary",
                "primary",
                "Primary",
                files.PrimaryVideoPath,
                TimeSpan.FromSeconds(10),
                new LoopPlaybackAnchorSnapshot("pane-primary", "primary", "Primary", TimeSpan.FromSeconds(2), null),
                CreateAnchor("pane-primary", TimeSpan.FromSeconds(4), 120L));
            Assert.Throws<InvalidOperationException>(() => ClipExportService.CreatePlan(CreateClipRequest(
                files.PrimaryVideoPath,
                files.ClipOutputPath,
                session,
                pendingRange)));

            var invalidRange = CreateLoopRange(
                "pane-primary",
                files.PrimaryVideoPath,
                TimeSpan.FromSeconds(10),
                TimeSpan.FromSeconds(5),
                150L,
                TimeSpan.FromSeconds(4),
                120L);
            Assert.Throws<InvalidOperationException>(() => ClipExportService.CreatePlan(CreateClipRequest(
                files.PrimaryVideoPath,
                files.ClipOutputPath,
                session,
                invalidRange)));
        }

        [Fact]
        public void AudioInsertionPlan_ValidatesCodecsAndBuildsBoundedAudioArguments()
        {
            using var files = new TemporaryExportFiles();
            var session = CreateSession(
                files.PrimaryVideoPath,
                TimeSpan.FromSeconds(12.5d),
                1920,
                1080,
                videoCodecName: "H.264");

            var plan = AudioInsertionService.CreatePlan(new AudioInsertionRequest(
                files.PrimaryVideoPath,
                files.ReplacementAudioPath,
                files.AudioOutputPath,
                "Primary",
                session));

            Assert.Equal(Path.GetFullPath(files.PrimaryVideoPath), plan.SourceFilePath);
            Assert.Equal(Path.GetFullPath(files.ReplacementAudioPath), plan.ReplacementAudioFilePath);
            Assert.Equal(Path.GetFullPath(files.AudioOutputPath), plan.OutputFilePath);
            Assert.Equal(TimeSpan.FromSeconds(12.5d), plan.VideoDuration);
            Assert.Empty(plan.FfmpegArguments);
            var filterGraph = NativeAudioInsertionService.BuildAudioFilterGraph(plan);
            Assert.Contains("apad=whole_dur=12.5,atrim=duration=12.5", filterGraph, StringComparison.Ordinal);
            Assert.Contains("abuffersink@outa", filterGraph, StringComparison.Ordinal);
            var m4vSourcePath = Path.ChangeExtension(files.PrimaryVideoPath, ".m4v");
            File.Copy(files.PrimaryVideoPath, m4vSourcePath);
            var m4vPlan = AudioInsertionService.CreatePlan(new AudioInsertionRequest(
                m4vSourcePath,
                files.ReplacementAudioPath,
                files.AudioOutputPath,
                "Primary",
                CreateSession(m4vSourcePath, TimeSpan.FromSeconds(10), 1920, 1080, "mpeg4")));
            Assert.Equal(Path.GetFullPath(m4vSourcePath), m4vPlan.SourceFilePath);
            var hevcPlan = AudioInsertionService.CreatePlan(new AudioInsertionRequest(
                files.PrimaryVideoPath,
                files.ReplacementAudioPath,
                files.AudioOutputPath,
                "Primary",
                CreateSession(files.PrimaryVideoPath, TimeSpan.FromSeconds(10), 1920, 1080, "hevc")));
            Assert.Equal(Path.GetFullPath(files.PrimaryVideoPath), hevcPlan.SourceFilePath);

            Assert.Throws<ArgumentNullException>(() => AudioInsertionService.CreatePlan(null!));
            Assert.Throws<InvalidOperationException>(() => AudioInsertionService.CreatePlan(new AudioInsertionRequest(
                files.PrimaryVideoPath,
                files.ReplacementAudioPath,
                files.PrimaryVideoPath,
                "Primary",
                session)));
            Assert.Throws<InvalidOperationException>(() => AudioInsertionService.CreatePlan(new AudioInsertionRequest(
                files.PrimaryVideoPath,
                files.ReplacementAudioPath,
                Path.ChangeExtension(files.AudioOutputPath, ".mov"),
                "Primary",
                session)));
            Assert.Throws<InvalidOperationException>(() => AudioInsertionService.CreatePlan(new AudioInsertionRequest(
                files.PrimaryVideoPath,
                files.UnsupportedAudioPath,
                files.AudioOutputPath,
                "Primary",
                session)));
            Assert.Throws<InvalidOperationException>(() => AudioInsertionService.CreatePlan(new AudioInsertionRequest(
                files.PrimaryVideoPath,
                files.ReplacementAudioPath,
                files.AudioOutputPath,
                "Primary",
                CreateSession(files.PrimaryVideoPath, TimeSpan.Zero, 1920, 1080, "h264"))));
        }

        [Fact]
        public void WholeVideoComparePlan_AlignsPanesAndSelectsAvailableAudio()
        {
            using var files = new TemporaryExportFiles();
            var primary = CreateSession(
                files.PrimaryVideoPath,
                TimeSpan.FromSeconds(10),
                641,
                479,
                presentationTime: TimeSpan.FromSeconds(3),
                hasAudioStream: true);
            var compare = CreateSession(
                files.CompareVideoPath,
                TimeSpan.FromSeconds(8),
                320,
                241,
                presentationTime: TimeSpan.FromSeconds(5),
                hasAudioStream: false);
            var request = new CompareSideBySideExportRequest
            {
                OutputFilePath = files.CompareOutputPath,
                Mode = CompareSideBySideExportMode.WholeVideo,
                AudioSource = CompareSideBySideExportAudioSource.Primary,
                PrimarySessionSnapshot = primary,
                CompareSessionSnapshot = compare,
                PrimaryViewportSnapshot = PaneViewportSnapshot.CreateFullFrame(641, 479),
                CompareViewportSnapshot = PaneViewportSnapshot.CreateFullFrame(320, 241)
            };

            var plan = CompareSideBySideExportService.CreatePlan(request);

            Assert.Equal(TimeSpan.FromSeconds(2), plan.PrimaryLeadingPad);
            Assert.Equal(TimeSpan.Zero, plan.CompareLeadingPad);
            Assert.Equal(TimeSpan.Zero, plan.PrimaryTrailingPad);
            Assert.Equal(TimeSpan.FromSeconds(4), plan.CompareTrailingPad);
            Assert.Equal(TimeSpan.FromSeconds(12), plan.OutputDuration);
            Assert.Equal(962, plan.OutputWidth);
            Assert.Equal(480, plan.OutputHeight);
            Assert.Equal("whole-video", plan.PrimaryEndBoundaryStrategy);
            Assert.Equal("whole-video", plan.CompareEndBoundaryStrategy);
            Assert.True(plan.SelectedAudioHasStream);
            Assert.Empty(plan.FfmpegArguments);
            var filterGraph = NativeCompareSideBySideExportService.BuildFilterGraph(plan);
            Assert.Contains("adelay=2000:all=1", filterGraph, StringComparison.Ordinal);
            Assert.Contains("tpad=start_mode=add:start_duration=2", filterGraph, StringComparison.Ordinal);
            Assert.Contains("stop_mode=add:stop_duration=4", filterGraph, StringComparison.Ordinal);
            Assert.Contains("abuffersink@outa", filterGraph, StringComparison.Ordinal);

            request = new CompareSideBySideExportRequest
            {
                OutputFilePath = files.CompareWithoutAudioOutputPath,
                Mode = CompareSideBySideExportMode.WholeVideo,
                AudioSource = CompareSideBySideExportAudioSource.Compare,
                PrimarySessionSnapshot = primary,
                CompareSessionSnapshot = compare,
                PrimaryViewportSnapshot = PaneViewportSnapshot.CreateFullFrame(641, 479),
                CompareViewportSnapshot = PaneViewportSnapshot.CreateFullFrame(320, 241)
            };
            plan = CompareSideBySideExportService.CreatePlan(request);

            Assert.False(plan.SelectedAudioHasStream);
            Assert.Empty(plan.FfmpegArguments);
            filterGraph = NativeCompareSideBySideExportService.BuildFilterGraph(plan);
            Assert.DoesNotContain("[aout]", filterGraph, StringComparison.Ordinal);
        }

        [Fact]
        public void LoopComparePlan_UsesPaneRangesViewportCropAndLongestDuration()
        {
            using var files = new TemporaryExportFiles();
            var primary = CreateSession(files.PrimaryVideoPath, TimeSpan.FromSeconds(10), 640, 480, hasAudioStream: false);
            var compare = CreateSession(files.CompareVideoPath, TimeSpan.FromSeconds(8), 1280, 720, hasAudioStream: true);
            var primaryRange = CreateLoopRange(
                "pane-primary",
                files.PrimaryVideoPath,
                TimeSpan.FromSeconds(10),
                TimeSpan.FromSeconds(2),
                60L,
                TimeSpan.FromSeconds(4),
                120L);
            var compareRange = CreateLoopRange(
                "pane-compare",
                files.CompareVideoPath,
                TimeSpan.FromSeconds(8),
                TimeSpan.FromSeconds(1),
                30L,
                TimeSpan.FromSeconds(2),
                60L);
            var compareViewport = new PaneViewportSnapshot(2d, 0.5d, 0.5d, 1280, 720, 100, 50, 640, 360);

            var plan = CompareSideBySideExportService.CreatePlan(new CompareSideBySideExportRequest
            {
                OutputFilePath = files.CompareOutputPath,
                Mode = CompareSideBySideExportMode.Loop,
                AudioSource = CompareSideBySideExportAudioSource.Compare,
                PrimarySessionSnapshot = primary,
                CompareSessionSnapshot = compare,
                PrimaryViewportSnapshot = PaneViewportSnapshot.CreateFullFrame(640, 480),
                CompareViewportSnapshot = compareViewport,
                PrimaryLoopRange = primaryRange,
                CompareLoopRange = compareRange
            });

            Assert.Equal(TimeSpan.FromSeconds(2), plan.PrimaryStartTime);
            Assert.Equal(TimeSpan.FromSeconds(1), plan.CompareStartTime);
            Assert.Equal("position-step", plan.PrimaryEndBoundaryStrategy);
            Assert.Equal("position-step", plan.CompareEndBoundaryStrategy);
            Assert.Equal(plan.PrimaryContentDuration, plan.OutputDuration);
            Assert.True(plan.CompareTrailingPad > TimeSpan.Zero);
            Assert.True(plan.SelectedAudioHasStream);
            Assert.Empty(plan.FfmpegArguments);
            var filterGraph = NativeCompareSideBySideExportService.BuildFilterGraph(plan);
            Assert.Contains("crop=640:360:100:50", filterGraph, StringComparison.Ordinal);
            Assert.Contains("atrim=start=1", filterGraph, StringComparison.Ordinal);
            Assert.Contains("hstack=inputs=2", filterGraph, StringComparison.Ordinal);
        }

        [Fact]
        public void ComparePlan_RejectsUnavailableSourcesAndInvalidRanges()
        {
            using var files = new TemporaryExportFiles();
            var primary = CreateSession(files.PrimaryVideoPath, TimeSpan.FromSeconds(10), 640, 480);
            var compare = CreateSession(files.CompareVideoPath, TimeSpan.FromSeconds(8), 1280, 720);

            Assert.Throws<ArgumentNullException>(() => CompareSideBySideExportService.CreatePlan(null!));
            Assert.Throws<InvalidOperationException>(() => CompareSideBySideExportService.CreatePlan(new CompareSideBySideExportRequest
            {
                OutputFilePath = files.CompareOutputPath,
                PrimarySessionSnapshot = ReviewSessionSnapshot.Empty,
                CompareSessionSnapshot = compare
            }));
            Assert.Throws<InvalidOperationException>(() => CompareSideBySideExportService.CreatePlan(new CompareSideBySideExportRequest
            {
                OutputFilePath = files.CompareOutputPath,
                PrimarySessionSnapshot = primary,
                CompareSessionSnapshot = ReviewSessionSnapshot.Empty
            }));
            Assert.Throws<InvalidOperationException>(() => CompareSideBySideExportService.CreatePlan(new CompareSideBySideExportRequest
            {
                OutputFilePath = files.PrimaryVideoPath,
                Mode = CompareSideBySideExportMode.WholeVideo,
                PrimarySessionSnapshot = primary,
                CompareSessionSnapshot = compare
            }));
            Assert.Throws<InvalidOperationException>(() => CompareSideBySideExportService.CreatePlan(new CompareSideBySideExportRequest
            {
                OutputFilePath = files.CompareOutputPath,
                Mode = CompareSideBySideExportMode.Loop,
                PrimarySessionSnapshot = primary,
                CompareSessionSnapshot = compare
            }));

            var pendingRange = new LoopPlaybackPaneRangeSnapshot(
                "pane-primary",
                "primary",
                "Primary",
                files.PrimaryVideoPath,
                TimeSpan.FromSeconds(10),
                new LoopPlaybackAnchorSnapshot("pane-primary", "primary", "Primary", TimeSpan.FromSeconds(2), null),
                CreateAnchor("pane-primary", TimeSpan.FromSeconds(4), 120L));
            var validCompareRange = CreateLoopRange(
                "pane-compare",
                files.CompareVideoPath,
                TimeSpan.FromSeconds(8),
                TimeSpan.FromSeconds(1),
                30L,
                TimeSpan.FromSeconds(2),
                60L);
            Assert.Throws<InvalidOperationException>(() => CompareSideBySideExportService.CreatePlan(new CompareSideBySideExportRequest
            {
                OutputFilePath = files.CompareOutputPath,
                Mode = CompareSideBySideExportMode.Loop,
                PrimarySessionSnapshot = primary,
                CompareSessionSnapshot = compare,
                PrimaryLoopRange = pendingRange,
                CompareLoopRange = validCompareRange
            }));

            Assert.Throws<InvalidOperationException>(() => CompareSideBySideExportService.CreatePlan(new CompareSideBySideExportRequest
            {
                OutputFilePath = files.CompareOutputPath,
                Mode = CompareSideBySideExportMode.WholeVideo,
                PrimarySessionSnapshot = CreateSession(files.PrimaryVideoPath, TimeSpan.Zero, 640, 480),
                CompareSessionSnapshot = compare
            }));
        }

        private static ClipExportRequest CreateClipRequest(
            string sourceFilePath,
            string outputFilePath,
            ReviewSessionSnapshot session,
            LoopPlaybackPaneRangeSnapshot loopRange)
        {
            return new ClipExportRequest(
                sourceFilePath,
                outputFilePath,
                "Primary",
                "pane-primary",
                isPaneLocal: true,
                session,
                loopRange,
                engine: null,
                PaneViewportSnapshot.CreateFullFrame(640, 480));
        }

        private static ReviewSessionSnapshot CreateSession(
            string filePath,
            TimeSpan duration,
            int width,
            int height,
            string videoCodecName = "h264",
            TimeSpan? presentationTime = null,
            bool hasAudioStream = true)
        {
            var mediaInfo = new VideoMediaInfo(
                filePath,
                duration,
                TimeSpan.FromSeconds(1d / 30d),
                30d,
                width,
                height,
                videoCodecName,
                0,
                30,
                1,
                1,
                90_000,
                hasAudioStream: hasAudioStream);
            var position = new ReviewPosition(
                presentationTime ?? TimeSpan.Zero,
                0L,
                isFrameAccurate: true,
                isFrameIndexAbsolute: true,
                presentationTimestamp: 0L,
                decodeTimestamp: 0L);
            return new ReviewSessionSnapshot(
                "primary",
                "Primary",
                ReviewPlaybackState.Paused,
                filePath,
                mediaInfo,
                position);
        }

        private static LoopPlaybackPaneRangeSnapshot CreateLoopRange(
            string paneId,
            string filePath,
            TimeSpan duration,
            TimeSpan loopInTime,
            long loopInFrame,
            TimeSpan loopOutTime,
            long loopOutFrame)
        {
            return new LoopPlaybackPaneRangeSnapshot(
                paneId,
                paneId,
                paneId,
                filePath,
                duration,
                CreateAnchor(paneId, loopInTime, loopInFrame),
                CreateAnchor(paneId, loopOutTime, loopOutFrame));
        }

        private static LoopPlaybackAnchorSnapshot CreateAnchor(string paneId, TimeSpan time, long frameIndex)
        {
            return new LoopPlaybackAnchorSnapshot(
                paneId,
                paneId,
                paneId,
                time,
                new LoopPlaybackFrameIdentitySnapshot(frameIndex, true, null, null));
        }

        private sealed class TemporaryExportFiles : IDisposable
        {
            public TemporaryExportFiles()
            {
                DirectoryPath = Path.Combine(Path.GetTempPath(), "frame-player-export-tests-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(DirectoryPath);
                PrimaryVideoPath = CreateFile("primary.mp4");
                CompareVideoPath = CreateFile("compare.mp4");
                ReplacementAudioPath = CreateFile("replacement.wav");
                UnsupportedAudioPath = CreateFile("replacement.flac");
                ClipOutputPath = Path.Combine(DirectoryPath, "exports", "clip.mp4");
                AudioOutputPath = Path.Combine(DirectoryPath, "exports", "audio.mp4");
                CompareOutputPath = Path.Combine(DirectoryPath, "exports", "compare.mp4");
                CompareWithoutAudioOutputPath = Path.Combine(DirectoryPath, "exports", "compare-silent.mp4");
            }

            public string DirectoryPath { get; }

            public string PrimaryVideoPath { get; }

            public string CompareVideoPath { get; }

            public string ReplacementAudioPath { get; }

            public string UnsupportedAudioPath { get; }

            public string ClipOutputPath { get; }

            public string AudioOutputPath { get; }

            public string CompareOutputPath { get; }

            public string CompareWithoutAudioOutputPath { get; }

            public void Dispose()
            {
                Directory.Delete(DirectoryPath, recursive: true);
            }

            private string CreateFile(string fileName)
            {
                var path = Path.Combine(DirectoryPath, fileName);
                File.WriteAllBytes(path, Array.Empty<byte>());
                return path;
            }
        }
    }
}
