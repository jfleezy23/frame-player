using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Avalonia.Media.Imaging;
using FramePlayer.Avalonia.Services;
using FramePlayer.Avalonia.Views;
using FramePlayer.Core.Models;
using Xunit;

namespace FramePlayer.Avalonia.Tests
{
    public sealed class V2ReadinessCoverageTests : IClassFixture<AvaloniaHeadlessFixture>
    {
        private readonly AvaloniaHeadlessFixture _fixture;

        public V2ReadinessCoverageTests(AvaloniaHeadlessFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public void VideoInfoSnapshot_IncludesStructuredInspectorFields()
        {
            var mediaInfo = new VideoMediaInfo(
                "/tmp/camera-sample.mov",
                TimeSpan.FromMinutes(2),
                TimeSpan.FromMilliseconds(41),
                23.976,
                1920,
                1080,
                "h264",
                0,
                24000,
                1001,
                1,
                90000,
                hasAudioStream: true,
                isAudioPlaybackAvailable: true,
                audioCodecName: "aac",
                audioStreamIndex: 1,
                audioSampleRate: 48000,
                audioChannelCount: 2,
                displayWidth: 1280,
                displayHeight: 720,
                displayAspectRatioNumerator: 16,
                displayAspectRatioDenominator: 9,
                sourcePixelFormatName: "yuv420p",
                videoBitDepth: 8,
                videoBitRate: 10000000,
                videoColorSpace: "bt709",
                videoColorRange: "mpeg",
                videoColorPrimaries: "bt709",
                videoColorTransfer: "bt709",
                audioBitRate: 192000,
                audioBitDepth: 16);
            var position = new ReviewPosition(TimeSpan.FromSeconds(12), 123, true, true, null, null);

            var snapshot = BuildVideoInfoSnapshot("Primary", mediaInfo, position);

            Assert.Equal("Video Info - Primary", snapshot.WindowTitle);
            Assert.Equal("camera-sample.mov", snapshot.FileName);
            AssertField(snapshot.SummarySection, "Current position", "00:00:12.000");
            AssertField(snapshot.SummarySection, "Display aspect", "16:9");
            AssertField(snapshot.VideoSection, "Coded resolution", "1920 x 1080");
            AssertField(snapshot.VideoSection, "Display resolution", "1280 x 720");
            AssertField(snapshot.VideoSection, "Color space", "bt709");
            AssertField(snapshot.AudioSection, "Playback", "available");
            AssertField(snapshot.AudioSection, "Sample rate", "48,000 Hz");
            AssertField(snapshot.AdvancedSection, "Audio stream index", "1");

            var zeroPositionSnapshot = BuildVideoInfoSnapshot(
                "Primary",
                mediaInfo,
                new ReviewPosition(TimeSpan.Zero, 0, true, true, null, null));
            AssertField(zeroPositionSnapshot.SummarySection, "Current position", "00:00:00.000");
        }

        [Fact]
        public void DiagnosticLogService_BuildsReportAndKeepsPersistentLogBestEffort()
        {
            var service = new DiagnosticLogService();

            service.Info("Opened" + Environment.NewLine + "file");
            var report = service.BuildReport(new[] { "Frame Player diagnostics" });

            Assert.Contains("Frame Player diagnostics", report, StringComparison.Ordinal);
            Assert.Contains("Event Log", report, StringComparison.Ordinal);
            Assert.Contains("[INFO] Opened file", report, StringComparison.Ordinal);
            Assert.False(string.IsNullOrWhiteSpace(service.LatestLogPath));
            Assert.True(File.Exists(service.LatestLogPath));
        }

        [Fact]
        public void DiagnosticFileIdentifier_DoesNotExposePathOrFileName()
        {
            const string sensitivePath = "/Users/example/Client Project/private-cut.mov";

            var identifier = BuildDiagnosticFileIdentifier(sensitivePath);

            Assert.StartsWith("path-hash:", identifier, StringComparison.Ordinal);
            Assert.DoesNotContain("/Users/example/Client Project", identifier, StringComparison.Ordinal);
            Assert.DoesNotContain("private-cut.mov", identifier, StringComparison.Ordinal);
        }

        [Fact]
        public void AvaloniaFrameBufferPresenter_ReusesBitmapUntilDimensionsChange()
        {
            _fixture.Run(() =>
            {
                WriteableBitmap? reusableBitmap = null;
                using var firstFrame = CreateFrameBuffer(8, 4);
                using var secondFrame = CreateFrameBuffer(8, 4);
                using var resizedFrame = CreateFrameBuffer(4, 4);

                var firstBitmap = AvaloniaFrameBufferPresenter.PresentBitmap(firstFrame, null, ref reusableBitmap);
                var secondBitmap = AvaloniaFrameBufferPresenter.PresentBitmap(secondFrame, null, ref reusableBitmap);
                var resizedBitmap = AvaloniaFrameBufferPresenter.PresentBitmap(resizedFrame, null, ref reusableBitmap);

                Assert.NotNull(firstBitmap);
                Assert.Same(firstBitmap, secondBitmap);
                Assert.NotSame(firstBitmap, resizedBitmap);
                Assert.Same(resizedBitmap, reusableBitmap);

                reusableBitmap?.Dispose();
            });
        }

        [Fact]
        public void MainWindowSource_PinsReadinessFixesAgainstRegression()
        {
            var source = ReadRepositoryFile(
                "src",
                "FramePlayer.Avalonia",
                "Views",
                "MainWindow.axaml.cs");

            Assert.Contains("new DiagnosticLogService()", source, StringComparison.Ordinal);
            Assert.Contains("_diagnosticLogService.BuildReport(BuildDiagnosticsHeader())", source, StringComparison.Ordinal);
            Assert.Contains("UpdateCacheStatusFromEngine()", source, StringComparison.Ordinal);
            Assert.Contains("ffmpegEngine.ApproximateCachedFrameBytes", source, StringComparison.Ordinal);
            Assert.Contains("ffmpegEngine.LastCacheRefillMilliseconds", source, StringComparison.Ordinal);
            Assert.Contains("private async Task PauseHiddenComparePlaybackAsync()", source, StringComparison.Ordinal);
            Assert.Contains("private void RestartLoopPlaybackIfNeeded(", source, StringComparison.Ordinal);
            Assert.Contains("var engine = TryGetExistingEngine(pane);", source, StringComparison.Ordinal);
            Assert.Contains("Task.Run(() => RestartLoopPlaybackAsync(engine, restartRange));", source, StringComparison.Ordinal);
            Assert.Contains("_diagnosticLogService.Info(\"File opened: \" + BuildDiagnosticFileIdentifier(filePath));", source, StringComparison.Ordinal);
            Assert.Contains("private void CancelQueuedSliderScrubs()", source, StringComparison.Ordinal);
            Assert.Contains("_hasPendingSliderScrubTarget = false;", source, StringComparison.Ordinal);
            Assert.Contains("_hasPendingPaneSliderScrubTarget = false;", source, StringComparison.Ordinal);
            Assert.DoesNotContain("Task.Run(async () => await RestartLoopPlaybackAsync", source, StringComparison.Ordinal);
            Assert.DoesNotContain("_diagnosticLogService.Info($\"File opened: {filePath}\")", source, StringComparison.Ordinal);
            Assert.DoesNotContain("This build is a separate Avalonia desktop preview.", source, StringComparison.Ordinal);
            Assert.DoesNotContain("Windows WPF v1.8.4 and macOS Preview 0.1.0 remain the protected release tracks.", source, StringComparison.Ordinal);
        }

        private static VideoInfoSnapshot BuildVideoInfoSnapshot(
            string paneLabel,
            VideoMediaInfo mediaInfo,
            ReviewPosition position)
        {
            var method = typeof(MainWindow).GetMethod(
                "BuildVideoInfoSnapshot",
                BindingFlags.Static | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(typeof(MainWindow).FullName, "BuildVideoInfoSnapshot");
            return (VideoInfoSnapshot)(method.Invoke(null, new object[] { paneLabel, mediaInfo, position })
                ?? throw new InvalidOperationException("BuildVideoInfoSnapshot returned null."));
        }

        private static string BuildDiagnosticFileIdentifier(string filePath)
        {
            var method = typeof(MainWindow).GetMethod(
                "BuildDiagnosticFileIdentifier",
                BindingFlags.Static | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(typeof(MainWindow).FullName, "BuildDiagnosticFileIdentifier");
            return (string)(method.Invoke(null, new object[] { filePath })
                ?? throw new InvalidOperationException("BuildDiagnosticFileIdentifier returned null."));
        }

        private static void AssertField(VideoInfoSection section, string label, string expectedValue)
        {
            var field = section.Fields.FirstOrDefault(candidate =>
                string.Equals(candidate.Label, label, StringComparison.Ordinal));
            Assert.NotNull(field);
            Assert.Equal(expectedValue, field!.Value);
        }

        private static DecodedFrameBuffer CreateFrameBuffer(int width, int height)
        {
            var pixels = new byte[width * height * 4];
            for (var index = 0; index < pixels.Length; index += 4)
            {
                pixels[index] = 0x40;
                pixels[index + 1] = 0x80;
                pixels[index + 2] = 0xC0;
                pixels[index + 3] = 0xFF;
            }

            return new DecodedFrameBuffer(
                new FrameDescriptor(
                    0,
                    TimeSpan.Zero,
                    false,
                    true,
                    width,
                    height,
                    "bgra",
                    "bgra",
                    null,
                    null,
                    null),
                pixels,
                width * 4,
                "bgra");
        }

        private static string ReadRepositoryFile(params string[] pathParts)
        {
            var fullPath = Path.Combine(FindRepositoryRoot(AppContext.BaseDirectory), Path.Combine(pathParts));
            return File.ReadAllText(fullPath);
        }

        private static string FindRepositoryRoot(string startDirectory)
        {
            var directory = new DirectoryInfo(startDirectory);
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "FramePlayer.csproj")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException("Could not find the frame-player repository root.");
        }
    }
}
