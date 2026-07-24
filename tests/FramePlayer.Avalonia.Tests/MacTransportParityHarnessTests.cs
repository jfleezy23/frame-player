using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using FramePlayer.Core.Coordination;
using FramePlayer.Core.Models;
using FramePlayer.Engines.FFmpeg;
using FramePlayer.Avalonia.Views;
using Xunit;

namespace FramePlayer.Avalonia.Tests
{
    [Collection(MacReleaseCandidateTestGroup.Name)]
    public sealed class MacTransportParityHarnessTests
    {
        private static readonly TimeSpan PlaybackObservationDelay = TimeSpan.FromMilliseconds(900);
        private static readonly string[] SupportedExtensions = { ".avi", ".m4v", ".mkv", ".mov", ".mp4", ".ts", ".wmv" };
        private readonly MacReleaseCandidateHeadlessFixture _fixture;

        public MacTransportParityHarnessTests(MacReleaseCandidateHeadlessFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public void MacWindow_ExposesWindowsHarnessTransportContract()
        {
            var windowType = typeof(MainWindow);

            Assert.NotNull(RequireMethod(windowType, "LaunchNewWindow"));
            RequireMethod(windowType, "OpenMediaAsync", typeof(string));
            RequireMethod(windowType, "OpenMediaAsync", typeof(string), typeof(string));
            RequireMethod(windowType, "CloseMediaAsync");
            RequireMethod(windowType, "CommitSliderSeekAsync", typeof(string), typeof(TimeSpan));
            RequireMethod(windowType, "StartPlaybackAsync", typeof(SynchronizedOperationScope?), typeof(string));
            RequireMethod(windowType, "PausePlaybackAsync", typeof(bool));
            RequireMethod(windowType, "StepFrameAsync", typeof(int));
            RequireMethod(windowType, "SetLoopMarker", typeof(LoopPlaybackMarkerEndpoint));
            RequireMethod(windowType, "SetTimelineLoopMarkerAtAsync", typeof(string), typeof(LoopPlaybackMarkerEndpoint), typeof(TimeSpan));
            RequireMethod(windowType, "ClearLoopPoints");
            RequireMethod(windowType, "ExportLoopClipAsync", typeof(string), typeof(string));
            RequireMethod(windowType, "ExportSideBySideCompareAsync", typeof(string), typeof(CompareSideBySideExportMode), typeof(CompareSideBySideExportAudioSource));
            RequireMethod(windowType, "ReplaceAudioTrackAsync", typeof(string), typeof(string));
            RequireMethod(windowType, "ExportDiagnosticsAsync", typeof(string));
        }

        [Fact]
        [Trait("Category", "ReleaseCandidate")]
        public async Task MacWindow_PlaybackSubmitsAudioForAudioBearingCorpusClip()
        {
            ConfigureRuntime();
            var audioFile = FindCorpusFiles()
                .FirstOrDefault(path => Path.GetExtension(path).Equals(".mp4", StringComparison.OrdinalIgnoreCase));
            Assert.False(string.IsNullOrWhiteSpace(audioFile));

            MainWindow? window = null;
            try
            {
                window = CreateWindow();
                await InvokeWindowTaskAsync(window!, "OpenMediaAsync", audioFile!);

                var engine = GetPrimaryEngine(window!);
                Assert.True(engine.MediaInfo.HasAudioStream, audioFile + " did not report an audio stream.");
                Assert.True(engine.MediaInfo.IsAudioPlaybackAvailable, audioFile + " did not report playable audio.");

                await InvokeWindowTaskAsync(
                    window!,
                    "StartPlaybackAsync",
                    (SynchronizedOperationScope?)SynchronizedOperationScope.FocusedPane,
                    "pane-primary");
                await Task.Delay(PlaybackObservationDelay);
                await InvokeWindowTaskAsync(window!, "PausePlaybackAsync", true);

                Assert.True(
                    engine.LastAudioSubmittedBytes > 0,
                    "Timed playback did not submit any audio bytes. Error: " + engine.LastAudioErrorMessage);
                Assert.True(engine.LastPlaybackUsedAudioClock, "Timed playback did not use the audio clock.");
            }
            finally
            {
                if (window != null)
                {
                    SetUnifiedLoopPlaybackEnabled(window, false);
                    await Task.Delay(TimeSpan.FromMilliseconds(250));
                    await InvokeWindowTaskAsync(window, "PausePlaybackAsync", true)
                        .WaitAsync(TimeSpan.FromSeconds(10));
                    _fixture.Run(() => window.Close());
                }
            }
        }

        [Fact]
        [Trait("Category", "ReleaseCandidate")]
        public async Task MacWindow_LoopPlaybackStaysInsideHarnessRange()
        {
            ConfigureRuntime();
            var file = FindCorpusFiles()[0];

            MainWindow? window = null;
            try
            {
                window = CreateWindow();
                await InvokeWindowTaskAsync(window!, "OpenMediaAsync", file);

                var engine = GetPrimaryEngine(window!);
                await WaitForIndexAsync(engine);
                var frameStep = engine.MediaInfo.PositionStep > TimeSpan.Zero
                    ? engine.MediaInfo.PositionStep
                    : TimeSpan.FromSeconds(1d / Math.Max(engine.MediaInfo.FramesPerSecond, 24d));

                var loopStart = TimeSpan.FromTicks(Math.Max(frameStep.Ticks * 6, TimeSpan.FromMilliseconds(250).Ticks));
                var loopEnd = loopStart + TimeSpan.FromTicks(frameStep.Ticks * 8);
                if (loopEnd >= engine.MediaInfo.Duration)
                {
                    loopStart = TimeSpan.Zero;
                    loopEnd = TimeSpan.FromTicks(Math.Max(frameStep.Ticks * 8, engine.MediaInfo.Duration.Ticks / 3));
                }

                var inSet = await InvokeWindowTaskAsync<bool>(
                    window!,
                    "SetTimelineLoopMarkerAtAsync",
                    "pane-primary",
                    LoopPlaybackMarkerEndpoint.In,
                    loopStart);
                var blocked = await InvokeWindowTaskAsync<bool>(
                    window!,
                    "SetTimelineLoopMarkerAtAsync",
                    "pane-primary",
                    LoopPlaybackMarkerEndpoint.Out,
                    loopStart - frameStep);
                Assert.False(blocked, "Mac loop harness allowed loop-out before loop-in.");

                var outSet = await InvokeWindowTaskAsync<bool>(
                    window!,
                    "SetTimelineLoopMarkerAtAsync",
                    "pane-primary",
                    LoopPlaybackMarkerEndpoint.Out,
                    loopEnd);
                Assert.True(inSet && outSet, "Mac loop harness did not set both loop markers.");

                SetUnifiedLoopPlaybackEnabled(window!, true);
                await InvokeWindowTaskAsync(window!, "CommitSliderSeekAsync", "test", loopStart);
                await InvokeWindowTaskAsync(
                    window!,
                    "StartPlaybackAsync",
                    (SynchronizedOperationScope?)SynchronizedOperationScope.FocusedPane,
                    "pane-primary");

                var observations = new List<TimeSpan>();
                for (var index = 0; index < 8; index++)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(220));
                    observations.Add(engine.Position.PresentationTime);
                }

                await InvokeWindowTaskAsync(window!, "PausePlaybackAsync", true);

                Assert.All(
                    observations.Where(position => position >= loopStart),
                    position => Assert.True(
                        position <= loopEnd + frameStep + frameStep,
                        "Loop playback escaped the configured range. Position=" + position + " range=" + loopStart + ".." + loopEnd));

                var loopStatus = GetButtonText(window!, "LoopStatusButton");
                Assert.StartsWith("Loop: ", loopStatus, StringComparison.Ordinal);
                Assert.DoesNotContain("off", loopStatus, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                if (window != null)
                {
                    SetUnifiedLoopPlaybackEnabled(window, false);
                    await Task.Delay(TimeSpan.FromMilliseconds(250));
                    await InvokeWindowTaskAsync(
                            window,
                            "PausePlaybackAsync",
                            true,
                            (SynchronizedOperationScope?)SynchronizedOperationScope.AllPanes)
                        .WaitAsync(TimeSpan.FromSeconds(10));
                    _fixture.Run(() => window.Close());
                }
            }
        }

        [Fact]
        [Trait("Category", "ReleaseCandidate")]
        public async Task CompareWindow_AllPaneLoopPlaybackStaysInsideBothHarnessRanges()
        {
            ConfigureRuntime();
            var files = FindCorpusFiles();
            var file = files.FirstOrDefault(path =>
                string.Equals(Path.GetFileName(path), "sample-test.mp4", StringComparison.OrdinalIgnoreCase)) ?? files[0];

            MainWindow? window = null;
            try
            {
                window = CreateWindow();
                SetCompareMode(window!, true);
                await InvokeWindowTaskAsync(window!, "OpenMediaAsync", file, "pane-primary")
                    .WaitAsync(TimeSpan.FromSeconds(10));
                await InvokeWindowTaskAsync(window!, "OpenMediaAsync", file, "pane-compare")
                    .WaitAsync(TimeSpan.FromSeconds(10));

                var primaryEngine = GetPrimaryEngine(window!);
                var compareEngine = GetCompareEngine(window!);
                await WaitForIndexAsync(primaryEngine);
                await WaitForIndexAsync(compareEngine);
                var frameStep = primaryEngine.MediaInfo.PositionStep > TimeSpan.Zero
                    ? primaryEngine.MediaInfo.PositionStep
                    : TimeSpan.FromSeconds(1d / Math.Max(primaryEngine.MediaInfo.FramesPerSecond, 24d));
                var loopStart = TimeSpan.FromTicks(Math.Max(frameStep.Ticks * 6, TimeSpan.FromMilliseconds(250).Ticks));
                var loopEnd = loopStart + TimeSpan.FromTicks(frameStep.Ticks * 8);

                foreach (var paneId in new[] { "pane-primary", "pane-compare" })
                {
                    Assert.True(await InvokeWindowTaskAsync<bool>(
                            window!,
                            "SetTimelineLoopMarkerAtAsync",
                            paneId,
                            LoopPlaybackMarkerEndpoint.In,
                            loopStart)
                        .WaitAsync(TimeSpan.FromSeconds(10)));
                    Assert.True(await InvokeWindowTaskAsync<bool>(
                            window!,
                            "SetTimelineLoopMarkerAtAsync",
                            paneId,
                            LoopPlaybackMarkerEndpoint.Out,
                            loopEnd)
                        .WaitAsync(TimeSpan.FromSeconds(10)));
                }

                SetAllPanesTransport(window!, true);
                SetUnifiedLoopPlaybackEnabled(window!, true);
                await InvokeWindowTaskAsync(window!, "CommitSliderSeekAsync", "test", loopStart)
                    .WaitAsync(TimeSpan.FromSeconds(10));
                await InvokeWindowTaskAsync(
                        window!,
                        "StartPlaybackAsync",
                        (SynchronizedOperationScope?)SynchronizedOperationScope.AllPanes,
                        "pane-primary")
                    .WaitAsync(TimeSpan.FromSeconds(10));

                var primaryObservations = new List<TimeSpan>();
                var compareObservations = new List<TimeSpan>();
                for (var index = 0; index < 10; index++)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(180));
                    primaryObservations.Add(primaryEngine.Position.PresentationTime);
                    compareObservations.Add(compareEngine.Position.PresentationTime);
                }

                await InvokeWindowTaskAsync(
                        window!,
                        "PausePlaybackAsync",
                        true,
                        (SynchronizedOperationScope?)SynchronizedOperationScope.AllPanes)
                    .WaitAsync(TimeSpan.FromSeconds(10));

                var maximumAllowed = loopEnd + frameStep + frameStep;
                Assert.All(
                    primaryObservations,
                    position => Assert.True(
                        position <= maximumAllowed,
                        "Left pane escaped the configured loop range. Position=" + position + " range=" + loopStart + ".." + loopEnd));
                Assert.All(
                    compareObservations,
                    position => Assert.True(
                        position <= maximumAllowed,
                        "Right pane escaped the configured loop range. Position=" + position + " range=" + loopStart + ".." + loopEnd));
                for (var index = 0; index < primaryObservations.Count; index++)
                {
                    var paneDelta = (primaryObservations[index] - compareObservations[index]).Duration();
                    Assert.True(
                        paneDelta <= frameStep + frameStep + frameStep,
                        "All-pane loop playback lost synchronization. Left=" +
                        primaryObservations[index] +
                        " right=" +
                        compareObservations[index] +
                        " delta=" +
                        paneDelta);
                }
            }
            finally
            {
                if (window != null)
                {
                    _fixture.Run(() => window.Close());
                }
            }
        }

        [Fact]
        [Trait("Category", "ReleaseCandidate")]
        public async Task CompareWindow_AllPanePlaybackKeepsIdenticalAudioMediaSynchronized()
        {
            ConfigureRuntime();
            var files = FindCorpusFiles();
            var file = files.FirstOrDefault(path =>
                Path.GetFileName(path).StartsWith("Audio_Video_Sync_", StringComparison.OrdinalIgnoreCase)) ?? files[0];

            MainWindow? window = null;
            try
            {
                window = CreateWindow();
                SetCompareMode(window!, true);
                await InvokeWindowTaskAsync(window!, "OpenMediaAsync", file, "pane-primary")
                    .WaitAsync(TimeSpan.FromSeconds(10));
                await InvokeWindowTaskAsync(window!, "OpenMediaAsync", file, "pane-compare")
                    .WaitAsync(TimeSpan.FromSeconds(10));

                var primaryEngine = GetPrimaryEngine(window!);
                var compareEngine = GetCompareEngine(window!);
                await WaitForIndexAsync(primaryEngine);
                await WaitForIndexAsync(compareEngine);
                Assert.True(primaryEngine.MediaInfo.HasAudioStream, file + " did not report audio in the left pane.");
                Assert.True(compareEngine.MediaInfo.HasAudioStream, file + " did not report audio in the right pane.");
                var frameStep = primaryEngine.MediaInfo.PositionStep > TimeSpan.Zero
                    ? primaryEngine.MediaInfo.PositionStep
                    : TimeSpan.FromSeconds(1d / Math.Max(primaryEngine.MediaInfo.FramesPerSecond, 24d));

                SetAllPanesTransport(window!, true);
                SetUnifiedLoopPlaybackEnabled(window!, true);
                await InvokeWindowTaskAsync(window!, "CommitSliderSeekAsync", "test", TimeSpan.Zero)
                    .WaitAsync(TimeSpan.FromSeconds(10));
                await InvokeWindowTaskAsync(
                        window!,
                        "StartPlaybackAsync",
                        (SynchronizedOperationScope?)SynchronizedOperationScope.AllPanes,
                        "pane-primary")
                    .WaitAsync(TimeSpan.FromSeconds(10));

                var maximumEngineDelta = TimeSpan.Zero;
                var maximumRawEngineDelta = TimeSpan.Zero;
                var maximumPresentedDelta = TimeSpan.Zero;
                var primaryPresentedTimes = new HashSet<TimeSpan>();
                var comparePresentedTimes = new HashSet<TimeSpan>();
                var observationStartedAt = DateTime.UtcNow;
                do
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(50));
                    var rawEngineDelta = (primaryEngine.Position.PresentationTime - compareEngine.Position.PresentationTime).Duration();
                    if (rawEngineDelta > maximumRawEngineDelta)
                    {
                        maximumRawEngineDelta = rawEngineDelta;
                    }

                    var engineDelta = rawEngineDelta;
                    var loopDuration = primaryEngine.MediaInfo.Duration;
                    if (loopDuration > TimeSpan.Zero)
                    {
                        engineDelta = TimeSpan.FromTicks(Math.Min(
                            rawEngineDelta.Ticks,
                            (loopDuration - rawEngineDelta).Duration().Ticks));
                    }

                    if (engineDelta > maximumEngineDelta)
                    {
                        maximumEngineDelta = engineDelta;
                    }

                    var presentedTimes = GetPresentedFrameTimesAndValidateReadouts(window!);
                    primaryPresentedTimes.Add(presentedTimes.Primary);
                    comparePresentedTimes.Add(presentedTimes.Compare);
                    var presentedDelta = (presentedTimes.Primary - presentedTimes.Compare).Duration();
                    if (presentedDelta > maximumPresentedDelta)
                    {
                        maximumPresentedDelta = presentedDelta;
                    }
                }
                while (DateTime.UtcNow - observationStartedAt < TimeSpan.FromSeconds(6) ||
                    ((primaryPresentedTimes.Count < 10 || comparePresentedTimes.Count < 10) &&
                        DateTime.UtcNow - observationStartedAt < TimeSpan.FromSeconds(10)));

                await InvokeWindowTaskAsync(
                        window!,
                        "PausePlaybackAsync",
                        true,
                        (SynchronizedOperationScope?)SynchronizedOperationScope.AllPanes)
                    .WaitAsync(TimeSpan.FromSeconds(10));
                GetPresentedFrameTimesAndValidateReadouts(window!);

                Assert.True(primaryEngine.LastAudioSubmittedBytes > 0, "Left pane did not submit audio bytes.");
                Assert.True(compareEngine.LastAudioSubmittedBytes > 0, "Right pane did not submit audio bytes.");
                Assert.True(primaryEngine.LastPlaybackUsedAudioClock, "Left pane did not use its audio clock.");
                Assert.True(compareEngine.LastPlaybackUsedAudioClock, "Right pane did not use its audio clock.");
                Console.WriteLine(
                    "Synchronized presentation samples: left=" +
                    primaryPresentedTimes.Count +
                    " right=" +
                    comparePresentedTimes.Count +
                    " left-span=" +
                    (primaryPresentedTimes.Max() - primaryPresentedTimes.Min()) +
                    " right-span=" +
                    (comparePresentedTimes.Max() - comparePresentedTimes.Min()) +
                    " engine-delta=" +
                    maximumEngineDelta +
                    " raw-engine-delta=" +
                    maximumRawEngineDelta);
                Assert.True(
                    primaryPresentedTimes.Count >= 10 && comparePresentedTimes.Count >= 10,
                    "Synchronized presentation did not advance through enough distinct frames. Left=" +
                    primaryPresentedTimes.Count +
                    " right=" +
                    comparePresentedTimes.Count);
                Assert.True(
                    primaryPresentedTimes.Max() - primaryPresentedTimes.Min() >= frameStep + frameStep + frameStep,
                    "Left synchronized presentation remained frozen.");
                Assert.True(
                    comparePresentedTimes.Max() - comparePresentedTimes.Min() >= frameStep + frameStep + frameStep,
                    "Right synchronized presentation remained frozen.");
                Assert.True(
                    maximumEngineDelta <= frameStep + frameStep + frameStep,
                    "All-pane playback engines drifted too far apart. Maximum engine delta=" +
                    maximumEngineDelta +
                    " raw engine delta=" +
                    maximumRawEngineDelta +
                    " frame step=" +
                    frameStep);
                Assert.True(
                    maximumPresentedDelta < frameStep,
                    "Sustained all-pane playback visibly lost synchronization. Maximum presented delta=" +
                    maximumPresentedDelta +
                    " maximum engine delta=" +
                    maximumEngineDelta +
                    " frame step=" +
                    frameStep);

                var loopStart = TimeSpan.FromTicks(Math.Max(frameStep.Ticks * 6, TimeSpan.FromMilliseconds(250).Ticks));
                var loopEnd = loopStart + TimeSpan.FromTicks(frameStep.Ticks * 8);
                foreach (var paneId in new[] { "pane-primary", "pane-compare" })
                {
                    Assert.True(await InvokeWindowTaskAsync<bool>(
                            window!,
                            "SetTimelineLoopMarkerAtAsync",
                            paneId,
                            LoopPlaybackMarkerEndpoint.In,
                            loopStart)
                        .WaitAsync(TimeSpan.FromSeconds(10)));
                    Assert.True(await InvokeWindowTaskAsync<bool>(
                            window!,
                            "SetTimelineLoopMarkerAtAsync",
                            paneId,
                            LoopPlaybackMarkerEndpoint.Out,
                            loopEnd)
                        .WaitAsync(TimeSpan.FromSeconds(10)));
                }

                await InvokeWindowTaskAsync(window!, "CommitSliderSeekAsync", "test-loop-restart", loopStart)
                    .WaitAsync(TimeSpan.FromSeconds(10));
                await InvokeWindowTaskAsync(
                        window!,
                        "StartPlaybackAsync",
                        (SynchronizedOperationScope?)SynchronizedOperationScope.AllPanes,
                        "pane-primary")
                    .WaitAsync(TimeSpan.FromSeconds(10));

                var loopMaximumPresentedDelta = TimeSpan.Zero;
                var loopPresentedTimes = new HashSet<TimeSpan>();
                var loopWrapObserved = false;
                TimeSpan? previousLoopPrimary = null;
                for (var index = 0; index < 60; index++)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(50));
                    var presentedTimes = GetPresentedFrameTimesAndValidateReadouts(window!);
                    loopPresentedTimes.Add(presentedTimes.Primary);
                    var presentedDelta = (presentedTimes.Primary - presentedTimes.Compare).Duration();
                    if (presentedDelta > loopMaximumPresentedDelta)
                    {
                        loopMaximumPresentedDelta = presentedDelta;
                    }

                    if (previousLoopPrimary.HasValue &&
                        presentedTimes.Primary + frameStep < previousLoopPrimary.Value)
                    {
                        loopWrapObserved = true;
                    }

                    previousLoopPrimary = presentedTimes.Primary;
                }

                await InvokeWindowTaskAsync(
                        window!,
                        "PausePlaybackAsync",
                        true,
                        (SynchronizedOperationScope?)SynchronizedOperationScope.AllPanes)
                    .WaitAsync(TimeSpan.FromSeconds(10));
                GetPresentedFrameTimesAndValidateReadouts(window!);

                Console.WriteLine(
                    "Short-loop presentation samples=" +
                    loopPresentedTimes.Count +
                    " span=" +
                    (loopPresentedTimes.Count > 0
                        ? loopPresentedTimes.Max() - loopPresentedTimes.Min()
                        : TimeSpan.Zero) +
                    " wrapped=" +
                    loopWrapObserved +
                    " left-position=" +
                    primaryEngine.Position.PresentationTime +
                    " right-position=" +
                    compareEngine.Position.PresentationTime +
                    " synchronized=" +
                    GetPrivateField<bool>(window!, "_isSynchronizedFramePresentationActive"));
                Assert.True(loopPresentedTimes.Count >= 4, "Short-loop presentation did not advance.");
                Assert.True(loopWrapObserved, "Short-loop playback did not visibly restart.");
                Assert.True(
                    loopMaximumPresentedDelta < frameStep,
                    "Short-loop restart presented mismatched panes. Maximum delta=" +
                    loopMaximumPresentedDelta +
                    " frame step=" +
                    frameStep);
            }
            finally
            {
                if (window != null)
                {
                    await InvokeWindowTaskAsync(
                            window,
                            "PausePlaybackAsync",
                            true,
                            (SynchronizedOperationScope?)SynchronizedOperationScope.AllPanes)
                        .WaitAsync(TimeSpan.FromSeconds(10));
                    _fixture.Run(() => window.Close());
                }
            }
        }

        [Fact]
        [Trait("Category", "ReleaseCandidate")]
        public async Task MacWindow_OpenRecentUsesFocusedComparePane()
        {
            ConfigureRuntime();
            var files = FindCorpusFiles().Take(3).ToArray();
            Assert.True(files.Length >= 3, "The focused recent-file test needs at least three corpus videos.");

            MainWindow? window = null;
            try
            {
                window = CreateWindow();
                SetCompareMode(window!, true);

                await InvokeWindowTaskAsync(window!, "OpenMediaAsync", files[0], "pane-primary");
                await InvokeWindowTaskAsync(window!, "OpenMediaAsync", files[1], "pane-compare");
                SelectPane(window!, "Compare");
                await InvokeWindowTaskAsync(window!, "OpenRecentPathAsync", files[2]);

                Assert.Equal(files[0], GetPrimaryEngine(window!).CurrentFilePath);
                Assert.Equal(files[2], GetCompareEngine(window!).CurrentFilePath);
            }
            finally
            {
                if (window != null)
                {
                    _fixture.Run(() => window.Close());
                }
            }
        }

        private static void ConfigureRuntime()
        {
            FfmpegRuntimeBootstrap.ConfigureForCurrentPlatform(ResolveRuntimeBaseDirectory());
        }

        private static string ResolveRuntimeBaseDirectory()
        {
            var appBundle = Environment.GetEnvironmentVariable("FRAMEPLAYER_MAC_APP_BUNDLE");
            if (!string.IsNullOrWhiteSpace(appBundle))
            {
                return Path.Combine(appBundle, "Contents", "MacOS");
            }

            var runtimeBase = Environment.GetEnvironmentVariable("FRAMEPLAYER_AVALONIA_RUNTIME_BASE");
            if (!string.IsNullOrWhiteSpace(runtimeBase))
            {
                return runtimeBase;
            }

            return FindRepositoryRoot();
        }

        private static string FindRepositoryRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                if (Directory.Exists(Path.Combine(directory.FullName, "Runtime", "macos")) &&
                    Directory.Exists(Path.Combine(directory.FullName, "src", "FramePlayer.Avalonia")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException("Could not find frame-player repository root from " + AppContext.BaseDirectory);
        }

        private static string[] FindCorpusFiles()
        {
            var corpus = Environment.GetEnvironmentVariable("FRAMEPLAYER_MAC_CORPUS");
            if (string.IsNullOrWhiteSpace(corpus))
            {
                corpus = Path.Combine(FindRepositoryRoot(), "Video Test Files");
            }

            Assert.True(Directory.Exists(corpus), "Corpus folder not found: " + corpus);
            var files = Directory.EnumerateFiles(corpus, "*", SearchOption.AllDirectories)
                .Where(path => SupportedExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
            Assert.NotEmpty(files);
            return files;
        }

        private static async Task WaitForIndexAsync(FfmpegReviewEngine engine)
        {
            var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(5);
            while (DateTimeOffset.UtcNow < deadline)
            {
                if (engine.IsGlobalFrameIndexAvailable)
                {
                    return;
                }

                await Task.Delay(50);
            }
        }

        private static FfmpegReviewEngine GetPrimaryEngine(MainWindow window)
        {
            var field = typeof(MainWindow).GetField("_primaryEngine", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("Missing _primaryEngine field.");
            return (FfmpegReviewEngine)field.GetValue(window)!;
        }

        private static FfmpegReviewEngine GetCompareEngine(MainWindow window)
        {
            var field = typeof(MainWindow).GetField("_compareEngine", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("Missing _compareEngine field.");
            return (FfmpegReviewEngine)field.GetValue(window)!;
        }

        private static T GetPrivateField<T>(MainWindow window, string fieldName)
        {
            var field = typeof(MainWindow).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("Missing " + fieldName + " field.");
            return (T)field.GetValue(window)!;
        }

        private (TimeSpan Primary, TimeSpan Compare) GetPresentedFrameTimesAndValidateReadouts(
            MainWindow window)
        {
            var primaryField = typeof(MainWindow).GetField("_primaryFrameBuffer", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("Missing _primaryFrameBuffer field.");
            var compareField = typeof(MainWindow).GetField("_compareFrameBuffer", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("Missing _compareFrameBuffer field.");
            var primaryTime = TimeSpan.Zero;
            var compareTime = TimeSpan.Zero;
            _fixture.Run(() =>
            {
                var primaryFrame = (DecodedFrameBuffer?)primaryField.GetValue(window);
                var compareFrame = (DecodedFrameBuffer?)compareField.GetValue(window);
                Assert.NotNull(primaryFrame);
                Assert.NotNull(compareFrame);
                primaryTime = primaryFrame?.Descriptor.PresentationTime ?? TimeSpan.Zero;
                compareTime = compareFrame?.Descriptor.PresentationTime ?? TimeSpan.Zero;
                var primaryTimeText = FormatTime(primaryTime);
                var compareTimeText = FormatTime(compareTime);
                var primaryFrameText = FormatFrameNumber(primaryFrame?.Descriptor.FrameIndex);
                var compareFrameText = FormatFrameNumber(compareFrame?.Descriptor.FrameIndex);

                Assert.Equal(
                    primaryTimeText,
                    window.FindControl<TextBlock>("CurrentPositionTextBlock")!.Text);
                Assert.Equal(
                    primaryTimeText,
                    window.FindControl<TextBlock>("PrimaryPaneCurrentPositionTextBlock")!.Text);
                Assert.Equal(
                    compareTimeText,
                    window.FindControl<TextBlock>("ComparePaneCurrentPositionTextBlock")!.Text);
                Assert.Equal(
                    primaryFrameText,
                    window.FindControl<TextBox>("FrameNumberTextBox")!.Text);
                Assert.Equal(
                    primaryFrameText,
                    window.FindControl<TextBox>("PrimaryPaneFrameNumberTextBox")!.Text);
                Assert.Equal(
                    compareFrameText,
                    window.FindControl<TextBox>("ComparePaneFrameNumberTextBox")!.Text);
                Assert.Equal(
                    primaryTime.TotalSeconds,
                    window.FindControl<Slider>("PositionSlider")!.Value,
                    precision: 3);
                Assert.Equal(
                    primaryTime.TotalSeconds,
                    window.FindControl<Slider>("PrimaryPanePositionSlider")!.Value,
                    precision: 3);
                Assert.Equal(
                    compareTime.TotalSeconds,
                    window.FindControl<Slider>("ComparePanePositionSlider")!.Value,
                    precision: 3);
            });

            return (primaryTime, compareTime);
        }

        private static string FormatFrameNumber(long? frameIndex)
        {
            return frameIndex.HasValue
                ? (frameIndex.Value + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)
                : string.Empty;
        }

        private static string FormatTime(TimeSpan time)
        {
            if (time < TimeSpan.Zero)
            {
                time = TimeSpan.Zero;
            }

            return string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "{0:00}:{1:00}:{2:00}.{3:000}",
                (int)time.TotalHours,
                time.Minutes,
                time.Seconds,
                time.Milliseconds);
        }

        private MainWindow CreateWindow()
        {
            MainWindow? window = null;
            var created = new ManualResetEventSlim(false);
            _fixture.Run(() =>
            {
                window = new MainWindow();
                created.Set();
            });

            Assert.True(created.Wait(TimeSpan.FromSeconds(5)), "Timed out creating Mac test window.");
            return window ?? throw new InvalidOperationException("Mac test window was not created.");
        }

        private static MethodInfo RequireMethod(Type type, string name, params Type[] parameters)
        {
            return type.GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic, null, parameters, null)
                ?? throw new MissingMethodException(type.FullName, name);
        }

        private async Task InvokeWindowTaskAsync(MainWindow window, string methodName, params object?[] args)
        {
            var method = FindMethod(window.GetType(), methodName, args.Length);
            Task? task = null;
            var invoked = new ManualResetEventSlim(false);
            Exception? invokeException = null;
            _fixture.Run(() =>
            {
                try
                {
                    task = (Task?)method.Invoke(window, args);
                }
                catch (Exception ex)
                {
                    invokeException = ex;
                }
                finally
                {
                    invoked.Set();
                }
            });
            Assert.True(invoked.Wait(TimeSpan.FromSeconds(5)), "Timed out invoking " + methodName + ".");
            if (invokeException != null)
            {
                throw invokeException;
            }

            if (task != null)
            {
                await task;
            }
        }

        private async Task<T> InvokeWindowTaskAsync<T>(MainWindow window, string methodName, params object?[] args)
        {
            var method = FindMethod(window.GetType(), methodName, args.Length);
            Task<T>? task = null;
            var invoked = new ManualResetEventSlim(false);
            Exception? invokeException = null;
            _fixture.Run(() =>
            {
                try
                {
                    task = (Task<T>?)method.Invoke(window, args);
                }
                catch (Exception ex)
                {
                    invokeException = ex;
                }
                finally
                {
                    invoked.Set();
                }
            });
            Assert.True(invoked.Wait(TimeSpan.FromSeconds(5)), "Timed out invoking " + methodName + ".");
            if (invokeException != null)
            {
                throw invokeException;
            }

            if (task == null)
            {
                throw new InvalidOperationException(methodName + " did not return a task.");
            }

            return await task;
        }

        private static MethodInfo FindMethod(Type type, string name, int parameterCount)
        {
            return type.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
                .FirstOrDefault(method => string.Equals(method.Name, name, StringComparison.Ordinal) &&
                    method.GetParameters().Length == parameterCount)
                ?? throw new MissingMethodException(type.FullName, name);
        }

        private void SetUnifiedLoopPlaybackEnabled(MainWindow window, bool enabled)
        {
            var method = RequireMethod(window.GetType(), "SetUnifiedLoopPlaybackEnabled", typeof(bool));
            _fixture.Run(() => method.Invoke(window, new object?[] { enabled }));
        }

        private void SetCompareMode(MainWindow window, bool enabled)
        {
            _fixture.Run(() =>
            {
                var compareMode = window.FindControl<CheckBox>("CompareModeCheckBox")
                    ?? throw new InvalidOperationException("Missing CompareModeCheckBox.");
                compareMode.IsChecked = enabled;
            });
        }

        private void SetAllPanesTransport(MainWindow window, bool enabled)
        {
            _fixture.Run(() =>
            {
                var allPanes = window.FindControl<CheckBox>("AllPanesCheckBox")
                    ?? throw new InvalidOperationException("Missing AllPanesCheckBox.");
                allPanes.IsChecked = enabled;
            });
        }

        private void SelectPane(MainWindow window, string paneName)
        {
            var paneType = window.GetType().GetNestedType("Pane", BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("Missing MainWindow.Pane enum.");
            var pane = Enum.Parse(paneType, paneName);
            var method = RequireMethod(window.GetType(), "SelectPane", paneType);
            _fixture.Run(() => method.Invoke(window, new[] { pane }));
        }

        private string GetButtonText(MainWindow window, string name)
        {
            var completed = new ManualResetEventSlim(false);
            var text = string.Empty;
            _fixture.Run(() =>
            {
                text = (window.FindControl<Button>(name)?.Content as TextBlock)?.Text ?? string.Empty;
                completed.Set();
            });

            Assert.True(completed.Wait(TimeSpan.FromSeconds(5)), "Timed out reading " + name + ".");
            return text;
        }
    }
}
