using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
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
        private static readonly string[] SupportedExtensions = { ".avi", ".m4v", ".mkv", ".mov", ".mp4", ".wmv" };
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
                .FirstOrDefault(path => Path.GetFileName(path).StartsWith(
                    "Audio_Video_Sync_",
                    StringComparison.OrdinalIgnoreCase));
            Assert.False(
                string.IsNullOrWhiteSpace(audioFile),
                "The audio/video playback test requires the Audio_Video_Sync corpus clip.");

            MainWindow? window = null;
            try
            {
                window = CreateWindow();
                await InvokeWindowTaskAsync(window!, "OpenMediaAsync", audioFile!);

                var engine = GetPrimaryEngine(window!);
                Assert.True(engine.MediaInfo.HasAudioStream, audioFile + " did not report an audio stream.");
                Assert.True(engine.MediaInfo.IsAudioPlaybackAvailable, audioFile + " did not report playable audio.");
                var playbackFrameEvents = 0;
                engine.FramePresented += (_, _) =>
                    Interlocked.Increment(ref playbackFrameEvents);

                await InvokeWindowTaskAsync(
                    window!,
                    "StartPlaybackAsync",
                    (SynchronizedOperationScope?)SynchronizedOperationScope.FocusedPane,
                    "pane-primary");

                var frameStep = engine.MediaInfo.PositionStep > TimeSpan.Zero
                    ? engine.MediaInfo.PositionStep
                    : TimeSpan.FromSeconds(1d / Math.Max(
                        engine.MediaInfo.FramesPerSecond,
                        24d));
                var presentedTimes = new HashSet<TimeSpan>();
                var observationDeadline = DateTimeOffset.UtcNow +
                    TimeSpan.FromSeconds(2d);
                while (DateTimeOffset.UtcNow < observationDeadline)
                {
                    _fixture.Run(() =>
                    {
                        var presentedFrame =
                            GetPrivateField<DecodedFrameBuffer?>(
                                window!,
                                "_primaryFrameBuffer");
                        if (presentedFrame != null)
                        {
                            presentedTimes.Add(
                                presentedFrame.Descriptor.PresentationTime);
                        }
                    });
                    await Task.Delay(PlaybackObservationDelay / 12);
                }

                await InvokeWindowTaskAsync(window!, "PausePlaybackAsync", true);

                Assert.True(
                    engine.LastAudioSubmittedBytes > 0,
                    "Timed playback did not submit any audio bytes. Error: " + engine.LastAudioErrorMessage);
                Assert.True(engine.LastPlaybackUsedAudioClock, "Timed playback did not use the audio clock.");
                Assert.True(
                    presentedTimes.Count >= 5,
                    "Audio advanced while the primary video surface remained frozen. Presented times=" +
                    string.Join(",", presentedTimes.OrderBy(time => time)) +
                    " engine-frame-events=" + playbackFrameEvents +
                    " engine-position=" + engine.Position.PresentationTime +
                    " engine-error=" + engine.LastErrorMessage);
                Assert.True(
                    presentedTimes.Max() - presentedTimes.Min() >=
                        frameStep + frameStep + frameStep,
                    "Primary video presentation advanced by too little while audio played. Span=" +
                    (presentedTimes.Max() - presentedTimes.Min()) +
                    " frame step=" + frameStep);
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
                await InvokeWindowTaskAsync(window!, "OpenMediaAsync", file!, "pane-compare")
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
        public async Task CompareWindow_ShortEofWithUnifiedLoopDisabledDoesNotFreezeLongPane()
        {
            ConfigureRuntime();
            var files = FindCorpusFiles();
            var shortFile = files.FirstOrDefault(path =>
                Path.GetFileName(path).StartsWith(
                    "Audio_Video_Sync_",
                    StringComparison.OrdinalIgnoreCase));
            var longFile = files.FirstOrDefault(path =>
                string.Equals(
                    Path.GetFileName(path),
                    "hevc-2398-20s.mp4",
                    StringComparison.OrdinalIgnoreCase));
            Assert.False(
                string.IsNullOrWhiteSpace(shortFile),
                "The asymmetric EOF test requires the Audio_Video_Sync MP4.");
            Assert.False(
                string.IsNullOrWhiteSpace(longFile),
                "The asymmetric EOF test requires the 20-second HEVC MP4.");

            MainWindow? window = null;
            try
            {
                window = CreateWindow();
                SetCompareMode(window!, true);
                await InvokeWindowTaskAsync(
                        window!,
                        "OpenMediaAsync",
                        shortFile!,
                        "pane-primary")
                    .WaitAsync(TimeSpan.FromSeconds(10));
                await InvokeWindowTaskAsync(
                        window!,
                        "OpenMediaAsync",
                        longFile!,
                        "pane-compare")
                    .WaitAsync(TimeSpan.FromSeconds(10));

                var primaryEngine = GetPrimaryEngine(window!);
                var compareEngine = GetCompareEngine(window!);
                await WaitForIndexAsync(primaryEngine);
                await WaitForIndexAsync(compareEngine);
                var primaryIsShorter =
                    primaryEngine.MediaInfo.Duration <=
                    compareEngine.MediaInfo.Duration;
                var stoppedEngine = primaryIsShorter
                    ? primaryEngine
                    : compareEngine;
                var playingEngine = primaryIsShorter
                    ? compareEngine
                    : primaryEngine;
                var playingFrameEventCount = 0;
                playingEngine.FramePresented += (_, _) =>
                    Interlocked.Increment(
                        ref playingFrameEventCount);
                Assert.True(
                    playingEngine.MediaInfo.Duration -
                        stoppedEngine.MediaInfo.Duration >=
                        TimeSpan.FromSeconds(1),
                    "The corpus videos need at least one second of duration difference. Short=" +
                    stoppedEngine.MediaInfo.Duration +
                    " long=" +
                    playingEngine.MediaInfo.Duration);

                var frameStep =
                    stoppedEngine.MediaInfo.PositionStep > TimeSpan.Zero
                        ? stoppedEngine.MediaInfo.PositionStep
                        : TimeSpan.FromSeconds(
                            1d /
                            Math.Max(
                                stoppedEngine.MediaInfo.FramesPerSecond,
                                24d));
                var startTime = stoppedEngine.MediaInfo.Duration -
                    TimeSpan.FromTicks(
                        Math.Max(
                            frameStep.Ticks * 12L,
                            TimeSpan.FromMilliseconds(500).Ticks));
                Assert.True(startTime > TimeSpan.Zero);

                SetUnifiedLoopPlaybackEnabled(window!, false);
                await InvokeWindowTaskAsync(
                        window!,
                        "CommitSliderSeekAsync",
                        "test-asymmetric-eof",
                        startTime)
                    .WaitAsync(TimeSpan.FromSeconds(10));
                await InvokeWindowTaskAsync(
                        window!,
                        "StartPlaybackAsync",
                        (SynchronizedOperationScope?)
                            SynchronizedOperationScope.AllPanes,
                        "pane-primary")
                    .WaitAsync(TimeSpan.FromSeconds(10));

                var asymmetricPlaybackObserved = false;
                var deadline = DateTimeOffset.UtcNow +
                    TimeSpan.FromSeconds(5);
                while (!asymmetricPlaybackObserved &&
                    DateTimeOffset.UtcNow < deadline)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(25));
                    var synchronizationActive = true;
                    _fixture.Run(() =>
                    {
                        synchronizationActive =
                            GetPrivateField<bool>(
                                window!,
                                "_isSynchronizedFramePresentationActive");
                    });
                    asymmetricPlaybackObserved =
                        !stoppedEngine.IsPlaying &&
                        playingEngine.IsPlaying &&
                        !synchronizationActive;
                }

                Assert.True(
                    asymmetricPlaybackObserved,
                    "Short-pane EOF did not release paired presentation while the longer pane kept playing. Short=" +
                    stoppedEngine.Position.PresentationTime +
                    " long=" +
                    playingEngine.Position.PresentationTime +
                    " short-playing=" +
                    stoppedEngine.IsPlaying +
                    " long-playing=" +
                    playingEngine.IsPlaying);

                var stoppedPresentedTime = TimeSpan.Zero;
                var firstPlayingPresentedTime = TimeSpan.Zero;
                var firstPlayingFrameEventCount =
                    Volatile.Read(ref playingFrameEventCount);
                _fixture.Run(() =>
                {
                    stoppedPresentedTime =
                        GetPrivateField<DecodedFrameBuffer>(
                            window!,
                            primaryIsShorter
                                ? "_primaryFrameBuffer"
                                : "_compareFrameBuffer")
                            .Descriptor.PresentationTime;
                    firstPlayingPresentedTime =
                        GetPrivateField<DecodedFrameBuffer>(
                            window!,
                            primaryIsShorter
                                ? "_compareFrameBuffer"
                                : "_primaryFrameBuffer")
                            .Descriptor.PresentationTime;
                });

                for (var index = 0; index < 50; index++)
                {
                    _fixture.Run(() => { });
                    await Task.Delay(TimeSpan.FromMilliseconds(10));
                }

                var finalStoppedPresentedTime = TimeSpan.Zero;
                var finalPlayingPresentedTime = TimeSpan.Zero;
                var synchronizationActiveAtEnd = true;
                var pendingPlayingFrame = false;
                var playingFramePresentationQueued = false;
                _fixture.Run(() =>
                {
                    finalStoppedPresentedTime =
                        GetPrivateField<DecodedFrameBuffer>(
                            window!,
                            primaryIsShorter
                                ? "_primaryFrameBuffer"
                                : "_compareFrameBuffer")
                            .Descriptor.PresentationTime;
                    finalPlayingPresentedTime =
                        GetPrivateField<DecodedFrameBuffer>(
                            window!,
                            primaryIsShorter
                                ? "_compareFrameBuffer"
                                : "_primaryFrameBuffer")
                            .Descriptor.PresentationTime;
                    synchronizationActiveAtEnd =
                        GetPrivateField<bool>(
                            window!,
                            "_isSynchronizedFramePresentationActive");
                    pendingPlayingFrame =
                        GetPrivateField<DecodedFrameBuffer?>(
                            window!,
                            primaryIsShorter
                                ? "_pendingCompareFrameBuffer"
                                : "_pendingPrimaryFrameBuffer") != null;
                    playingFramePresentationQueued =
                        GetPrivateField<bool>(
                            window!,
                            primaryIsShorter
                                ? "_compareFramePresentationQueued"
                                : "_primaryFramePresentationQueued");
                });

                Assert.Equal(
                    stoppedPresentedTime,
                    finalStoppedPresentedTime);
                Assert.True(
                    finalPlayingPresentedTime >=
                        firstPlayingPresentedTime +
                        frameStep + frameStep,
                    "The longer pane's video did not advance after the short pane reached EOF. First=" +
                    firstPlayingPresentedTime +
                    " final=" +
                    finalPlayingPresentedTime +
                    " frame-step=" +
                    frameStep +
                    " engine-position=" +
                    playingEngine.Position.PresentationTime +
                    " engine-duration=" +
                    playingEngine.MediaInfo.Duration +
                    " engine-file=" +
                    playingEngine.CurrentFilePath +
                    " engine-playing=" +
                    playingEngine.IsPlaying +
                    " frame-events-after-eof=" +
                    (Volatile.Read(ref playingFrameEventCount) -
                        firstPlayingFrameEventCount) +
                    " sync-active=" +
                    synchronizationActiveAtEnd +
                    " pending-individual-frame=" +
                    pendingPlayingFrame +
                    " individual-frame-queued=" +
                    playingFramePresentationQueued);
            }
            finally
            {
                if (window != null)
                {
                    SetUnifiedLoopPlaybackEnabled(window, false);
                    await TryPauseAllPanePlaybackForCleanupAsync(window);
                    _fixture.Run(() => window.Close());
                }
            }
        }

        [Fact]
        [Trait("Category", "ReleaseCandidate")]
        public async Task CompareWindow_MainAndPanePlayPauseKeepSharedPlaybackLive()
        {
            ConfigureRuntime();
            var file = FindCorpusFiles().FirstOrDefault(path =>
                Path.GetFileName(path).StartsWith(
                    "Audio_Video_Sync_",
                    StringComparison.OrdinalIgnoreCase));
            Assert.False(
                string.IsNullOrWhiteSpace(file),
                "The shared-control test requires the Audio_Video_Sync corpus clip.");

            MainWindow? window = null;
            Task? panePauseTask = null;
            Task? masterResumeTask = null;
            Task? masterPauseTask = null;
            try
            {
                window = CreateWindow();
                SetCompareMode(window!, true);
                await InvokeWindowTaskAsync(window!, "OpenMediaAsync", file!, "pane-primary")
                    .WaitAsync(TimeSpan.FromSeconds(10));
                await InvokeWindowTaskAsync(window!, "OpenMediaAsync", file, "pane-compare")
                    .WaitAsync(TimeSpan.FromSeconds(10));

                var primaryEngine = GetPrimaryEngine(window!);
                var compareEngine = GetCompareEngine(window!);
                await WaitForIndexAsync(primaryEngine);
                await WaitForIndexAsync(compareEngine);
                var frameStep = primaryEngine.MediaInfo.PositionStep > TimeSpan.Zero
                    ? primaryEngine.MediaInfo.PositionStep
                    : TimeSpan.FromSeconds(
                        1d / Math.Max(primaryEngine.MediaInfo.FramesPerSecond, 24d));

                SetUnifiedLoopPlaybackEnabled(window!, false);

                await InvokeWindowTaskAsync(
                        window!,
                        "StartPlaybackAsync",
                        (SynchronizedOperationScope?)SynchronizedOperationScope.AllPanes,
                        "pane-primary")
                    .WaitAsync(TimeSpan.FromSeconds(10));
                Assert.True(primaryEngine.IsPlaying, "Shared play did not start the left pane.");
                Assert.True(compareEngine.IsPlaying, "Shared play did not start the right pane.");
                Assert.True(
                    GetPrivateField<bool>(window!, "_isSynchronizedFramePresentationActive"),
                    BuildAlignmentDiagnostics(window!, primaryEngine, compareEngine));

                await Task.Delay(TimeSpan.FromMilliseconds(500));
                GetPresentedFrameTimesAndValidateReadouts(window!);

                panePauseTask = InvokeWindowTaskAsync(
                        window!,
                        "TogglePanePlaybackAsync",
                        ParsePane(window!, "Primary"));
                var panePauseDeadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(2);
                while (!panePauseTask.IsCompleted &&
                    DateTimeOffset.UtcNow < panePauseDeadline)
                {
                    _fixture.Run(() => { });
                    await Task.Delay(TimeSpan.FromMilliseconds(20));
                }

                Assert.True(
                    panePauseTask.IsCompleted,
                    "Left-pane pause did not complete." +
                    Environment.NewLine +
                    BuildAlignmentDiagnostics(window!, primaryEngine, compareEngine));
                await panePauseTask;
                Assert.False(
                    primaryEngine.IsPlaying,
                    "A left-pane pause did not stop the left engine.");
                Assert.True(
                    compareEngine.IsPlaying,
                    "A left-pane pause incorrectly stopped the right engine.");
                Assert.False(
                    GetPrivateField<bool>(window!, "_isSynchronizedFramePresentationActive"),
                    "Shared pause left paired presentation active.");

                await Task.Delay(TimeSpan.FromMilliseconds(250));
                _fixture.Run(() => { });
                var expectedPresentedOffset =
                    primaryEngine.Position.PresentationTime -
                    compareEngine.Position.PresentationTime;
                Assert.True(
                    expectedPresentedOffset.Duration() >= frameStep,
                    "A left-pane pause did not create a local offset before master resume." +
                    Environment.NewLine +
                    BuildAlignmentDiagnostics(
                        window!,
                        primaryEngine,
                        compareEngine));

                masterResumeTask = InvokeWindowTaskAsync(window!, "TogglePlaybackAsync");
                var masterResumeDeadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(2);
                while (!masterResumeTask.IsCompleted &&
                    DateTimeOffset.UtcNow < masterResumeDeadline)
                {
                    _fixture.Run(() => { });
                    await Task.Delay(TimeSpan.FromMilliseconds(20));
                }

                Assert.True(
                    masterResumeTask.IsCompleted,
                    "Master resume did not complete after a left-pane pause." +
                    Environment.NewLine +
                    BuildAlignmentDiagnostics(window!, primaryEngine, compareEngine));
                await masterResumeTask;
                Assert.True(primaryEngine.IsPlaying, "Shared resume did not restart the left pane.");
                Assert.True(compareEngine.IsPlaying, "Shared resume did not restart the right pane.");
                Assert.True(
                    GetPrivateField<bool>(window!, "_isSynchronizedFramePresentationActive"),
                    BuildAlignmentDiagnostics(window!, primaryEngine, compareEngine));

                var primaryPresentedTimes = new HashSet<TimeSpan>();
                var comparePresentedTimes = new HashSet<TimeSpan>();
                const int minimumDistinctPresentedFrameSamples = 3;
                var observationStartedAt = DateTimeOffset.UtcNow;
                var minimumObservationDeadline =
                    observationStartedAt + TimeSpan.FromSeconds(1.5);
                var maximumObservationDeadline =
                    observationStartedAt + TimeSpan.FromSeconds(2.5);
                while (DateTimeOffset.UtcNow < minimumObservationDeadline ||
                    ((primaryPresentedTimes.Count <
                            minimumDistinctPresentedFrameSamples ||
                        comparePresentedTimes.Count <
                            minimumDistinctPresentedFrameSamples) &&
                        DateTimeOffset.UtcNow < maximumObservationDeadline))
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(50));
                    if (TryGetPresentedFrameTimesPreservingOffset(
                            window!,
                            expectedPresentedOffset,
                            frameStep + frameStep + frameStep,
                            out var presentedTimes))
                    {
                        primaryPresentedTimes.Add(presentedTimes.Primary);
                        comparePresentedTimes.Add(presentedTimes.Compare);
                    }
                }

                masterPauseTask = InvokeWindowTaskAsync(window!, "TogglePlaybackAsync");
                var masterPauseDeadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(2);
                while (!masterPauseTask.IsCompleted &&
                    DateTimeOffset.UtcNow < masterPauseDeadline)
                {
                    _fixture.Run(() => { });
                    await Task.Delay(TimeSpan.FromMilliseconds(20));
                }

                Assert.True(
                    masterPauseTask.IsCompleted,
                    "Master pause did not complete after shared playback resumed." +
                    Environment.NewLine +
                    BuildAlignmentDiagnostics(window!, primaryEngine, compareEngine));
                await masterPauseTask;
                Assert.False(primaryEngine.IsPlaying, "Master pause left the left engine playing.");
                Assert.False(compareEngine.IsPlaying, "Master pause left the right engine playing.");
                Assert.True(
                    primaryPresentedTimes.Count >= minimumDistinctPresentedFrameSamples &&
                        comparePresentedTimes.Count >= minimumDistinctPresentedFrameSamples,
                    "Main -> pane pause -> main resume did not produce enough offset-preserving paired frames. Left=" +
                    primaryPresentedTimes.Count +
                    " right=" +
                    comparePresentedTimes.Count +
                    Environment.NewLine +
                    BuildAlignmentDiagnostics(window!, primaryEngine, compareEngine));
                Assert.True(
                    primaryPresentedTimes.Max() - primaryPresentedTimes.Min() >=
                        frameStep + frameStep + frameStep,
                    "The left pane froze after switching between shared and pane play/pause controls.");
                Assert.True(
                    comparePresentedTimes.Max() - comparePresentedTimes.Min() >=
                        frameStep + frameStep + frameStep,
                    "The right pane froze after switching between shared and pane play/pause controls.");
            }
            finally
            {
                if (window != null)
                {
                    if ((panePauseTask != null && !panePauseTask.IsCompleted) ||
                        (masterResumeTask != null && !masterResumeTask.IsCompleted) ||
                        (masterPauseTask != null && !masterPauseTask.IsCompleted))
                    {
                        // Preserve the diagnostic failure rather than masking it with another
                        // transport request that must wait on the same blocked operation.
                    }
                    else
                    {
                        SetUnifiedLoopPlaybackEnabled(window, false);
                        await TryPauseAllPanePlaybackForCleanupAsync(window);
                        _fixture.Run(() => window.Close());
                    }
                }
            }
        }

        [Fact]
        [Trait("Category", "ReleaseCandidate")]
        public async Task CompareWindow_MasterPlayPreservesPausedLocalDivergence()
        {
            ConfigureRuntime();
            var file = FindCorpusFiles().FirstOrDefault(path =>
                Path.GetFileName(path).StartsWith(
                    "Audio_Video_Sync_",
                    StringComparison.OrdinalIgnoreCase));
            Assert.False(
                string.IsNullOrWhiteSpace(file),
                "The paused-divergence test requires the Audio_Video_Sync corpus clip.");

            MainWindow? window = null;
            try
            {
                window = CreateWindow();
                SetCompareMode(window!, true);
                await InvokeWindowTaskAsync(window!, "OpenMediaAsync", file!, "pane-primary")
                    .WaitAsync(TimeSpan.FromSeconds(10));
                await InvokeWindowTaskAsync(window!, "OpenMediaAsync", file, "pane-compare")
                    .WaitAsync(TimeSpan.FromSeconds(10));

                var primaryEngine = GetPrimaryEngine(window!);
                var compareEngine = GetCompareEngine(window!);
                await WaitForIndexAsync(primaryEngine);
                await WaitForIndexAsync(compareEngine);
                var frameStep = primaryEngine.MediaInfo.PositionStep > TimeSpan.Zero
                    ? primaryEngine.MediaInfo.PositionStep
                    : TimeSpan.FromSeconds(
                        1d / Math.Max(primaryEngine.MediaInfo.FramesPerSecond, 24d));

                SetUnifiedLoopPlaybackEnabled(window!, true);
                await InvokeWindowTaskAsync(window!, "CommitSliderSeekAsync", "test", TimeSpan.FromSeconds(1))
                    .WaitAsync(TimeSpan.FromSeconds(10));
                await InvokeWindowTaskAsync(
                        window!,
                        "SeekPaneToTimePreservingPlaybackAsync",
                        ParsePane(window!, "Primary"),
                        TimeSpan.FromSeconds(1.5),
                        CancellationToken.None)
                    .WaitAsync(TimeSpan.FromSeconds(10));
                await InvokeWindowTaskAsync(
                        window!,
                        "SeekPaneToTimePreservingPlaybackAsync",
                        ParsePane(window!, "Compare"),
                        TimeSpan.FromSeconds(2),
                        CancellationToken.None)
                    .WaitAsync(TimeSpan.FromSeconds(10));
                await Task.Delay(TimeSpan.FromMilliseconds(200));
                _fixture.Run(() => { });

                Assert.False(
                    primaryEngine.IsPlaying,
                    "The left pane was still playing before master paused-divergence resume.");
                Assert.False(
                    compareEngine.IsPlaying,
                    "The right pane was still playing before master paused-divergence resume.");

                var expectedPresentedOffset =
                    primaryEngine.Position.PresentationTime -
                    compareEngine.Position.PresentationTime;
                Assert.True(
                    expectedPresentedOffset.Duration() >= frameStep,
                    "The local controls did not leave an engine offset before master play." +
                    Environment.NewLine +
                    BuildAlignmentDiagnostics(
                        window!,
                        primaryEngine,
                        compareEngine));

                var masterResumeTask =
                    InvokeWindowTaskAsync(window!, "TogglePlaybackAsync");
                var masterResumeDeadline =
                    DateTimeOffset.UtcNow + TimeSpan.FromSeconds(2);
                while (!masterResumeTask.IsCompleted &&
                    DateTimeOffset.UtcNow < masterResumeDeadline)
                {
                    _fixture.Run(() => { });
                    await Task.Delay(TimeSpan.FromMilliseconds(20));
                }

                Assert.True(
                    masterResumeTask.IsCompleted,
                    "Master play did not complete after paused local divergence." +
                    Environment.NewLine +
                    BuildAlignmentDiagnostics(
                        window!,
                        primaryEngine,
                        compareEngine));
                await masterResumeTask;
                Assert.True(
                    primaryEngine.IsPlaying,
                    "Master play did not restart the left pane after paused local divergence.");
                Assert.True(
                    compareEngine.IsPlaying,
                    "Master play did not restart the right pane after paused local divergence.");

                var livePresentedTimes = new HashSet<TimeSpan>();
                var liveObservationDeadline =
                    DateTimeOffset.UtcNow + TimeSpan.FromSeconds(3);
                while (DateTimeOffset.UtcNow < liveObservationDeadline &&
                    livePresentedTimes.Count < 3)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(50));
                    _fixture.Run(() => { });
                    if (TryGetPresentedFrameTimesPreservingOffset(
                            window!,
                            expectedPresentedOffset,
                            frameStep + frameStep,
                            out var presentedTimes) &&
                        presentedTimes.Primary > TimeSpan.Zero &&
                        presentedTimes.Compare > TimeSpan.Zero)
                    {
                        livePresentedTimes.Add(presentedTimes.Primary);
                    }
                }

                Assert.True(
                    livePresentedTimes.Count >= 3,
                    "Master play either re-synced an intentional local offset or did not present live offset-preserving paired frames." +
                    Environment.NewLine +
                    BuildAlignmentDiagnostics(
                        window!,
                        primaryEngine,
                        compareEngine));

                var masterPauseTask =
                    InvokeWindowTaskAsync(window!, "TogglePlaybackAsync");
                var masterPauseDeadline =
                    DateTimeOffset.UtcNow + TimeSpan.FromSeconds(2);
                while (!masterPauseTask.IsCompleted &&
                    DateTimeOffset.UtcNow < masterPauseDeadline)
                {
                    _fixture.Run(() => { });
                    await Task.Delay(TimeSpan.FromMilliseconds(20));
                }

                Assert.True(
                    masterPauseTask.IsCompleted,
                    "Master pause did not complete after offset-preserving shared playback." +
                    Environment.NewLine +
                    BuildAlignmentDiagnostics(
                        window!,
                        primaryEngine,
                        compareEngine));
                await masterPauseTask;
                Assert.False(
                    primaryEngine.IsPlaying,
                    "Master pause left the left pane playing.");
                Assert.False(
                    compareEngine.IsPlaying,
                    "Master pause left the right pane playing.");
                Assert.True(
                    TryGetPresentedFrameTimesPreservingOffset(
                        window!,
                        expectedPresentedOffset,
                        frameStep + frameStep,
                        out _),
                    "Master pause re-synced an intentional live offset." +
                    Environment.NewLine +
                    BuildAlignmentDiagnostics(
                        window!,
                        primaryEngine,
                        compareEngine));
            }
            finally
            {
                if (window != null)
                {
                    await TryPauseAllPanePlaybackForCleanupAsync(window);
                    SetUnifiedLoopPlaybackEnabled(window, false);
                    _fixture.Run(() => window.Close());
                }
            }
        }

        [Fact]
        [Trait("Category", "ReleaseCandidate")]
        public async Task CompareWindow_MasterPausePreservesLocalPlaybackDivergence()
        {
            ConfigureRuntime();
            var file = FindCorpusFiles().FirstOrDefault(path =>
                Path.GetFileName(path).StartsWith(
                    "Audio_Video_Sync_",
                    StringComparison.OrdinalIgnoreCase));
            Assert.False(
                string.IsNullOrWhiteSpace(file),
                "The local-playback divergence test requires the Audio_Video_Sync corpus clip.");

            MainWindow? window = null;
            try
            {
                window = CreateWindow();
                SetCompareMode(window!, true);
                await InvokeWindowTaskAsync(window!, "OpenMediaAsync", file!, "pane-primary")
                    .WaitAsync(TimeSpan.FromSeconds(10));
                await InvokeWindowTaskAsync(window!, "OpenMediaAsync", file, "pane-compare")
                    .WaitAsync(TimeSpan.FromSeconds(10));

                var primaryEngine = GetPrimaryEngine(window!);
                var compareEngine = GetCompareEngine(window!);
                await WaitForIndexAsync(primaryEngine);
                await WaitForIndexAsync(compareEngine);
                var frameStep = primaryEngine.MediaInfo.PositionStep > TimeSpan.Zero
                    ? primaryEngine.MediaInfo.PositionStep
                    : TimeSpan.FromSeconds(
                        1d / Math.Max(primaryEngine.MediaInfo.FramesPerSecond, 24d));

                SetUnifiedLoopPlaybackEnabled(window!, true);
                await InvokeWindowTaskAsync(window!, "CommitSliderSeekAsync", "test", TimeSpan.FromSeconds(1))
                    .WaitAsync(TimeSpan.FromSeconds(10));
                await Task.Delay(TimeSpan.FromMilliseconds(200));
                _fixture.Run(() => { });

                await InvokeWindowTaskAsync(
                        window!,
                        "TogglePanePlaybackAsync",
                        ParsePane(window!, "Primary"))
                    .WaitAsync(TimeSpan.FromSeconds(2));
                await Task.Delay(TimeSpan.FromMilliseconds(450));
                await InvokeWindowTaskAsync(
                        window!,
                        "TogglePanePlaybackAsync",
                        ParsePane(window!, "Compare"))
                    .WaitAsync(TimeSpan.FromSeconds(2));
                await Task.Delay(TimeSpan.FromMilliseconds(450));
                _fixture.Run(() => { });

                Assert.True(primaryEngine.IsPlaying, "The left local play command did not leave the left pane playing.");
                Assert.True(compareEngine.IsPlaying, "The right local play command did not leave the right pane playing.");

                Assert.True(
                    TryGetPresentedFrameTimesPreservingOffsetWithoutReadoutValidation(
                        window!,
                        primaryEngine.Position.PresentationTime -
                            compareEngine.Position.PresentationTime,
                        frameStep + frameStep + frameStep,
                        out var presentedTimesBeforePause),
                    "The local controls did not present visible offset frames before master pause." +
                    Environment.NewLine +
                    BuildAlignmentDiagnostics(window!, primaryEngine, compareEngine));
                var observedPresentedOffset = presentedTimesBeforePause.Primary -
                    presentedTimesBeforePause.Compare;
                Assert.True(
                    observedPresentedOffset.Duration() >= frameStep,
                    "The local controls did not create an offset before master pause." +
                    Environment.NewLine +
                    BuildAlignmentDiagnostics(window!, primaryEngine, compareEngine));

                var masterPauseTask =
                    InvokeWindowTaskAsync(window!, "TogglePlaybackAsync");
                var masterPauseDeadline =
                    DateTimeOffset.UtcNow + TimeSpan.FromSeconds(2);
                while (!masterPauseTask.IsCompleted &&
                    DateTimeOffset.UtcNow < masterPauseDeadline)
                {
                    _fixture.Run(() => { });
                    await Task.Delay(TimeSpan.FromMilliseconds(20));
                }

                Assert.True(
                    masterPauseTask.IsCompleted,
                    "Master pause did not complete after local pane playback." +
                    Environment.NewLine +
                    BuildAlignmentDiagnostics(window!, primaryEngine, compareEngine));
                await masterPauseTask;
                Assert.False(primaryEngine.IsPlaying, "Master pause left the left pane playing.");
                Assert.False(compareEngine.IsPlaying, "Master pause left the right pane playing.");
                Assert.True(
                    TryGetPresentedFrameTimesPreservingOffsetWithoutReadoutValidation(
                        window!,
                        observedPresentedOffset,
                        frameStep + frameStep + frameStep,
                        out _),
                    "Master pause re-synced independently playing panes." +
                    Environment.NewLine +
                    BuildAlignmentDiagnostics(window!, primaryEngine, compareEngine));
            }
            finally
            {
                if (window != null)
                {
                    await TryPauseAllPanePlaybackForCleanupAsync(window);
                    SetUnifiedLoopPlaybackEnabled(window, false);
                    _fixture.Run(() => window.Close());
                }
            }
        }

        [Fact]
        [Trait("Category", "ReleaseCandidate")]
        public async Task CompareWindow_LiveSyncInBothDirectionsKeepsIdenticalMediaSynchronized()
        {
            ConfigureRuntime();
            var files = FindCorpusFiles();
            var file = files.FirstOrDefault(path =>
                Path.GetFileName(path).StartsWith(
                    "Audio_Video_Sync_",
                    StringComparison.OrdinalIgnoreCase));
            Assert.False(
                string.IsNullOrWhiteSpace(file),
                "The compare synchronization test requires the Audio_Video_Sync corpus clip.");

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
                Assert.True(
                    primaryEngine.MediaInfo.HasAudioStream,
                    file + " did not report audio in the left pane.");
                Assert.True(
                    compareEngine.MediaInfo.HasAudioStream,
                    file + " did not report audio in the right pane.");
                var frameStep = primaryEngine.MediaInfo.PositionStep > TimeSpan.Zero
                    ? primaryEngine.MediaInfo.PositionStep
                    : TimeSpan.FromSeconds(1d / Math.Max(primaryEngine.MediaInfo.FramesPerSecond, 24d));
                Console.WriteLine(
                    "Compare sync corpus metadata: duration=" +
                    primaryEngine.MediaInfo.Duration +
                    " fps=" +
                    primaryEngine.MediaInfo.FramesPerSecond +
                    " step=" +
                    frameStep +
                    " time-base=" +
                    primaryEngine.MediaInfo.StreamTimeBaseNumerator +
                    "/" +
                    primaryEngine.MediaInfo.StreamTimeBaseDenominator);

                SetUnifiedLoopPlaybackEnabled(window!, true);
                await InvokeWindowTaskAsync(window!, "CommitSliderSeekAsync", "test", TimeSpan.Zero)
                    .WaitAsync(TimeSpan.FromSeconds(10));
                await InvokeWindowTaskAsync(
                        window!,
                        "StartPlaybackAsync",
                        (SynchronizedOperationScope?)SynchronizedOperationScope.AllPanes,
                        "pane-primary")
                    .WaitAsync(TimeSpan.FromSeconds(10));

                var primaryPane = ParsePane(window!, "Primary");
                var comparePane = ParsePane(window!, "Compare");
                for (var syncCycle = 0; syncCycle < 3; syncCycle++)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(150));
                    var primaryToCompareAligned = await InvokeAlignmentAsync(
                            window!,
                            primaryEngine,
                            compareEngine,
                            primaryPane,
                            comparePane);
                    Assert.True(
                        primaryToCompareAligned,
                        BuildAlignmentDiagnostics(window!, primaryEngine, compareEngine));
                    GetPresentedFrameTimesAndValidateReadouts(window!);
                    Assert.True(primaryEngine.IsPlaying);
                    Assert.True(compareEngine.IsPlaying);

                    await Task.Delay(TimeSpan.FromMilliseconds(150));
                    var compareToPrimaryAligned = await InvokeAlignmentAsync(
                            window!,
                            primaryEngine,
                            compareEngine,
                            comparePane,
                            primaryPane);
                    Assert.True(
                        compareToPrimaryAligned,
                        BuildAlignmentDiagnostics(window!, primaryEngine, compareEngine));
                    GetPresentedFrameTimesAndValidateReadouts(window!);
                    Assert.True(primaryEngine.IsPlaying);
                    Assert.True(compareEngine.IsPlaying);
                }

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

                await PauseAllPanePlaybackWithDispatcherPumpAsync(
                    window!,
                    required: true,
                    "Timed out pausing all panes after live synchronization.");
                Assert.False(
                    GetPrivateField<bool>(
                        window!,
                        "_isSynchronizedFramePresentationActive"),
                    "Shared pause left paired frame presentation active after both panes stopped.");
                GetPresentedFrameTimesAndValidateReadouts(window!);

                Assert.True(
                    primaryEngine.LastAudioSubmittedBytes > 0,
                    "Left pane did not submit audio bytes.");
                Assert.True(
                    compareEngine.LastAudioSubmittedBytes > 0,
                    "Right pane did not submit audio bytes.");
                Assert.True(
                    primaryEngine.LastPlaybackUsedAudioClock,
                    "Left pane did not use its audio clock.");
                Assert.True(
                    compareEngine.LastPlaybackUsedAudioClock,
                    "Right pane did not use its audio clock.");

                var pausedPrimaryFrame = GetPrivateField<DecodedFrameBuffer>(
                    window!,
                    "_primaryFrameBuffer");
                var pausedCompareFrame = GetPrivateField<DecodedFrameBuffer>(
                    window!,
                    "_compareFrameBuffer");
                Assert.NotNull(pausedPrimaryFrame);
                Assert.NotNull(pausedCompareFrame);
                Assert.True(pausedPrimaryFrame!.Descriptor.IsFrameIndexAbsolute);
                Assert.True(pausedCompareFrame!.Descriptor.IsFrameIndexAbsolute);
                Assert.Equal(
                    pausedPrimaryFrame.Descriptor.FrameIndex,
                    pausedCompareFrame.Descriptor.FrameIndex);
                var expectedPausedFrame =
                    pausedPrimaryFrame.Descriptor.FrameIndex!.Value;
                var pausedStepDeltas = expectedPausedFrame >= 3L
                    ? new[] { -1, -1, -1, 1, 1, 1 }
                    : new[] { 1, 1, 1, -1, -1, -1 };
                foreach (var delta in pausedStepDeltas)
                {
                    expectedPausedFrame += delta;
                    await InvokeMasterStepAsync(
                        window!,
                        primaryEngine,
                        compareEngine,
                        delta);
                    await AssertPresentedFrameIdentityAndPixelsMatchAsync(
                        window!,
                        expectedPausedFrame);
                    Assert.Equal(
                        expectedPausedFrame,
                        primaryEngine.Position.FrameIndex);
                    Assert.Equal(
                        expectedPausedFrame,
                        compareEngine.Position.FrameIndex);
                    Assert.False(primaryEngine.IsPlaying);
                    Assert.False(compareEngine.IsPlaying);
                }

                await InvokeWindowTaskAsync(
                        window!,
                        "SeekAllPaneToFramePreservingPlaybackAsync",
                        24L)
                    .WaitAsync(TimeSpan.FromSeconds(10));
                Assert.True(primaryEngine.Position.IsFrameIndexAbsolute);
                Assert.True(compareEngine.Position.IsFrameIndexAbsolute);
                Assert.Equal(24L, primaryEngine.Position.FrameIndex);
                Assert.Equal(24L, compareEngine.Position.FrameIndex);
                Assert.Equal(
                    TimeSpan.FromMilliseconds(1001),
                    primaryEngine.Position.PresentationTime);
                Assert.Equal(
                    TimeSpan.FromMilliseconds(1001),
                    compareEngine.Position.PresentationTime);
                await AssertPresentedMarkerFrameAndPixelsMatchAsync(
                    window!,
                    expectedFrameIndex: 24,
                    expectedPresentationTime: TimeSpan.FromMilliseconds(1001));

                var pausedAlignment = await InvokeAlignmentAsync(
                    window!,
                    primaryEngine,
                    compareEngine,
                    comparePane,
                    primaryPane);
                Assert.True(
                    pausedAlignment,
                    BuildAlignmentDiagnostics(
                        window!,
                        primaryEngine,
                        compareEngine));
                Assert.False(primaryEngine.IsPlaying);
                Assert.False(compareEngine.IsPlaying);
                await AssertPresentedMarkerFrameAndPixelsMatchAsync(
                    window!,
                    expectedFrameIndex: 24,
                    expectedPresentationTime: TimeSpan.FromMilliseconds(1001));
                var expectedMarkerStepFrame = 24L;
                foreach (var delta in new[] { 1, 1, 1, -1, -1, -1 })
                {
                    expectedMarkerStepFrame += delta;
                    await InvokeMasterStepAsync(
                        window!,
                        primaryEngine,
                        compareEngine,
                        delta);
                    await AssertPresentedFrameIdentityAndPixelsMatchAsync(
                        window!,
                        expectedMarkerStepFrame);
                }

                await AssertPresentedMarkerFrameAndPixelsMatchAsync(
                    window!,
                    expectedFrameIndex: 24,
                    expectedPresentationTime: TimeSpan.FromMilliseconds(1001));

                await InvokeWindowTaskAsync(
                        window!,
                        "StartPlaybackAsync",
                        (SynchronizedOperationScope?)
                            SynchronizedOperationScope.AllPanes,
                        "pane-primary")
                    .WaitAsync(TimeSpan.FromSeconds(10));
                var postPausePrimaryTimes = new HashSet<TimeSpan>();
                var postPauseCompareTimes = new HashSet<TimeSpan>();
                var postPauseObservationStartedAt = DateTime.UtcNow;
                do
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(50));
                    var presentedTimes =
                        GetPresentedFrameTimesAndValidateReadouts(window!);
                    postPausePrimaryTimes.Add(presentedTimes.Primary);
                    postPauseCompareTimes.Add(presentedTimes.Compare);
                }
                while (DateTime.UtcNow - postPauseObservationStartedAt <
                        TimeSpan.FromSeconds(3) ||
                    ((postPausePrimaryTimes.Count < 6 ||
                            postPauseCompareTimes.Count < 6) &&
                        DateTime.UtcNow - postPauseObservationStartedAt <
                            TimeSpan.FromSeconds(8)));

                await PauseAllPanePlaybackWithDispatcherPumpAsync(
                    window!,
                    required: true,
                    "Timed out pausing all panes after paused synchronization resume.");
                GetPresentedFrameTimesAndValidateReadouts(window!);
                Assert.True(
                    primaryEngine.LastAudioSubmittedBytes > 0,
                    "Left pane did not resume audio after paused synchronization.");
                Assert.True(
                    compareEngine.LastAudioSubmittedBytes > 0,
                    "Right pane did not resume audio after paused synchronization.");
                Assert.True(
                    primaryEngine.LastPlaybackUsedAudioClock,
                    "Left pane did not resume on its audio clock after paused synchronization.");
                Assert.True(
                    compareEngine.LastPlaybackUsedAudioClock,
                    "Right pane did not resume on its audio clock after paused synchronization.");
                Assert.True(
                    postPausePrimaryTimes.Count >= 6 &&
                        postPauseCompareTimes.Count >= 6,
                    "Shared pause -> paused sync -> shared resume did not remain live. Left=" +
                    postPausePrimaryTimes.Count +
                    " right=" +
                    postPauseCompareTimes.Count +
                    " left-times=" +
                    string.Join(",", postPausePrimaryTimes.OrderBy(value => value)) +
                    " right-times=" +
                    string.Join(",", postPauseCompareTimes.OrderBy(value => value)) +
                    Environment.NewLine +
                    BuildAlignmentDiagnostics(
                        window!,
                        primaryEngine,
                        compareEngine));
                Assert.True(
                    postPausePrimaryTimes.Max() -
                        postPausePrimaryTimes.Min() >=
                        frameStep + frameStep + frameStep,
                    "Left pane froze after shared pause and paused synchronization.");
                Assert.True(
                    postPauseCompareTimes.Max() -
                        postPauseCompareTimes.Min() >=
                        frameStep + frameStep + frameStep,
                    "Right pane froze after shared pause and paused synchronization.");
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

                await PauseAllPanePlaybackWithDispatcherPumpAsync(
                    window!,
                    required: true,
                    "Timed out pausing all panes after short loop synchronization.");
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
                    await TryPauseAllPanePlaybackForCleanupAsync(window);
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

        private string BuildAlignmentDiagnostics(
            MainWindow window,
            FfmpegReviewEngine primaryEngine,
            FfmpegReviewEngine compareEngine)
        {
            var primaryPresented = "missing";
            var comparePresented = "missing";
            var status = string.Empty;
            _fixture.Run(() =>
            {
                var primaryFrame = GetPrivateField<DecodedFrameBuffer?>(
                    window,
                    "_primaryFrameBuffer");
                var compareFrame = GetPrivateField<DecodedFrameBuffer?>(
                    window,
                    "_compareFrameBuffer");
                primaryPresented = FormatFrameDescriptor(primaryFrame?.Descriptor);
                comparePresented = FormatFrameDescriptor(compareFrame?.Descriptor);
                status = window.FindControl<TextBlock>("CacheStatusTextBlock")?.Text ?? string.Empty;
            });

            return "Alignment returned false. Primary engine=" +
                FormatReviewPosition(primaryEngine.Position) +
                " compare engine=" +
                FormatReviewPosition(compareEngine.Position) +
                " primary presented=" +
                primaryPresented +
                " compare presented=" +
                comparePresented +
                " sync-active=" +
                GetPrivateField<bool>(
                    window,
                    "_isSynchronizedFramePresentationActive") +
                " all-gate=" +
                GetPrivateField<SemaphoreSlim>(
                    window,
                    "_allPaneTransportOperationGate").CurrentCount +
                " primary-gate=" +
                GetPrivateField<SemaphoreSlim>(
                    window,
                    "_primaryPaneTransportOperationGate").CurrentCount +
                " compare-gate=" +
                GetPrivateField<SemaphoreSlim>(
                    window,
                    "_comparePaneTransportOperationGate").CurrentCount +
                " playback-gate=" +
                GetPrivateField<SemaphoreSlim>(
                    window,
                    "_playbackStartGate").CurrentCount +
                " status=" +
                status;
        }

        private async Task<bool> InvokeAlignmentAsync(
            MainWindow window,
            FfmpegReviewEngine primaryEngine,
            FfmpegReviewEngine compareEngine,
            object sourcePane,
            object targetPane)
        {
            var alignmentTask = InvokeWindowTaskAsync<bool>(
                window,
                "AlignPaneToPaneAsync",
                sourcePane,
                targetPane);
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
            while (!alignmentTask.IsCompleted && DateTime.UtcNow < deadline)
            {
                _fixture.Run(() => { });
                await Task.Delay(TimeSpan.FromMilliseconds(10));
            }

            Assert.True(
                alignmentTask.IsCompleted,
                BuildAlignmentDiagnostics(window, primaryEngine, compareEngine));
            return await alignmentTask;
        }

        private async Task InvokeMasterStepAsync(
            MainWindow window,
            FfmpegReviewEngine primaryEngine,
            FfmpegReviewEngine compareEngine,
            int delta)
        {
            var stepTask = InvokeWindowTaskAsync(
                window,
                "StepFrameAsync",
                delta);
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
            while (!stepTask.IsCompleted && DateTime.UtcNow < deadline)
            {
                _fixture.Run(() => { });
                await Task.Delay(TimeSpan.FromMilliseconds(10));
            }

            Assert.True(
                stepTask.IsCompleted,
                "Master frame step timed out." +
                Environment.NewLine +
                BuildAlignmentDiagnostics(
                    window,
                    primaryEngine,
                    compareEngine));
            await stepTask;
        }

        private static string FormatFrameDescriptor(FrameDescriptor? descriptor)
        {
            return descriptor == null
                ? "missing"
                : "frame=" + descriptor.FrameIndex +
                    " time=" + descriptor.PresentationTime +
                    " pts=" + descriptor.PresentationTimestamp;
        }

        private static string FormatReviewPosition(ReviewPosition position)
        {
            return "frame=" + position.FrameIndex +
                " time=" + position.PresentationTime +
                " pts=" + position.PresentationTimestamp;
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
                Assert.Equal(
                    primaryFrame?.Descriptor.FrameIndex,
                    compareFrame?.Descriptor.FrameIndex);
                Assert.Equal(primaryTime, compareTime);
                Assert.Equal(
                    primaryFrame?.Descriptor.PresentationTimestamp,
                    compareFrame?.Descriptor.PresentationTimestamp);
                Assert.Equal(
                    primaryFrame?.Descriptor.DecodeTimestamp,
                    compareFrame?.Descriptor.DecodeTimestamp);
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

        private bool TryGetPresentedFrameTimesAndValidateReadouts(
            MainWindow window,
            out (TimeSpan Primary, TimeSpan Compare) presentedTimes)
        {
            var primaryField = typeof(MainWindow).GetField("_primaryFrameBuffer", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("Missing _primaryFrameBuffer field.");
            var compareField = typeof(MainWindow).GetField("_compareFrameBuffer", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("Missing _compareFrameBuffer field.");
            var primaryTime = TimeSpan.Zero;
            var compareTime = TimeSpan.Zero;
            var foundPairedFrame = false;
            _fixture.Run(() =>
            {
                var primaryFrame = (DecodedFrameBuffer?)primaryField.GetValue(window);
                var compareFrame = (DecodedFrameBuffer?)compareField.GetValue(window);
                if (primaryFrame == null ||
                    compareFrame == null ||
                    !HaveMatchingPresentedFrameIdentity(
                        primaryFrame.Descriptor,
                        compareFrame.Descriptor))
                {
                    return;
                }

                primaryTime = primaryFrame.Descriptor.PresentationTime;
                compareTime = compareFrame.Descriptor.PresentationTime;
                ValidatePresentedFrameReadouts(
                    window,
                    primaryFrame.Descriptor,
                    compareFrame.Descriptor);
                foundPairedFrame = true;
            });

            presentedTimes = (primaryTime, compareTime);
            return foundPairedFrame;
        }

        private bool TryGetPresentedFrameTimesPreservingOffset(
            MainWindow window,
            TimeSpan expectedOffset,
            TimeSpan tolerance,
            out (TimeSpan Primary, TimeSpan Compare) presentedTimes)
        {
            var primaryField = typeof(MainWindow).GetField("_primaryFrameBuffer", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("Missing _primaryFrameBuffer field.");
            var compareField = typeof(MainWindow).GetField("_compareFrameBuffer", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("Missing _compareFrameBuffer field.");
            var primaryTime = TimeSpan.Zero;
            var compareTime = TimeSpan.Zero;
            var foundOffsetFrame = false;
            _fixture.Run(() =>
            {
                var primaryFrame = (DecodedFrameBuffer?)primaryField.GetValue(window);
                var compareFrame = (DecodedFrameBuffer?)compareField.GetValue(window);
                if (primaryFrame == null || compareFrame == null)
                {
                    return;
                }

                var offset = primaryFrame.Descriptor.PresentationTime -
                    compareFrame.Descriptor.PresentationTime;
                if ((offset - expectedOffset).Duration() > tolerance)
                {
                    return;
                }

                primaryTime = primaryFrame.Descriptor.PresentationTime;
                compareTime = compareFrame.Descriptor.PresentationTime;
                ValidatePresentedFrameReadouts(
                    window,
                    primaryFrame.Descriptor,
                    compareFrame.Descriptor);
                foundOffsetFrame = true;
            });

            presentedTimes = (primaryTime, compareTime);
            return foundOffsetFrame;
        }

        private bool TryGetPresentedFrameTimesPreservingOffsetWithoutReadoutValidation(
            MainWindow window,
            TimeSpan expectedOffset,
            TimeSpan tolerance,
            out (TimeSpan Primary, TimeSpan Compare) presentedTimes)
        {
            var primaryField = typeof(MainWindow).GetField("_primaryFrameBuffer", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("Missing _primaryFrameBuffer field.");
            var compareField = typeof(MainWindow).GetField("_compareFrameBuffer", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("Missing _compareFrameBuffer field.");
            var primaryTime = TimeSpan.Zero;
            var compareTime = TimeSpan.Zero;
            var foundOffsetFrame = false;
            _fixture.Run(() =>
            {
                var primaryFrame = (DecodedFrameBuffer?)primaryField.GetValue(window);
                var compareFrame = (DecodedFrameBuffer?)compareField.GetValue(window);
                if (primaryFrame == null || compareFrame == null)
                {
                    return;
                }

                var offset = primaryFrame.Descriptor.PresentationTime -
                    compareFrame.Descriptor.PresentationTime;
                if ((offset - expectedOffset).Duration() > tolerance)
                {
                    return;
                }

                primaryTime = primaryFrame.Descriptor.PresentationTime;
                compareTime = compareFrame.Descriptor.PresentationTime;
                foundOffsetFrame = true;
            });

            presentedTimes = (primaryTime, compareTime);
            return foundOffsetFrame;
        }

        private static bool HaveMatchingPresentedFrameIdentity(
            FrameDescriptor primary,
            FrameDescriptor compare)
        {
            return primary.FrameIndex == compare.FrameIndex &&
                primary.PresentationTime == compare.PresentationTime &&
                primary.PresentationTimestamp == compare.PresentationTimestamp &&
                primary.DecodeTimestamp == compare.DecodeTimestamp;
        }

        private static void ValidatePresentedFrameReadouts(
            MainWindow window,
            FrameDescriptor primary,
            FrameDescriptor compare)
        {
            var primaryTimeText = FormatTime(primary.PresentationTime);
            var compareTimeText = FormatTime(compare.PresentationTime);
            var primaryFrameText = FormatFrameNumber(primary.FrameIndex);
            var compareFrameText = FormatFrameNumber(compare.FrameIndex);

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
                primary.PresentationTime.TotalSeconds,
                window.FindControl<Slider>("PositionSlider")!.Value,
                precision: 3);
            Assert.Equal(
                primary.PresentationTime.TotalSeconds,
                window.FindControl<Slider>("PrimaryPanePositionSlider")!.Value,
                precision: 3);
            Assert.Equal(
                compare.PresentationTime.TotalSeconds,
                window.FindControl<Slider>("ComparePanePositionSlider")!.Value,
                precision: 3);
        }

        private async Task AssertPresentedMarkerFrameAndPixelsMatchAsync(
            MainWindow window,
            long expectedFrameIndex,
            TimeSpan expectedPresentationTime)
        {
            var markerPresented = false;
            var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(2);
            while (!markerPresented && DateTimeOffset.UtcNow < deadline)
            {
                _fixture.Run(() =>
                {
                    var primaryFrame = GetPrivateField<DecodedFrameBuffer?>(
                        window,
                        "_primaryFrameBuffer");
                    var compareFrame = GetPrivateField<DecodedFrameBuffer?>(
                        window,
                        "_compareFrameBuffer");
                    markerPresented =
                        primaryFrame?.Descriptor.FrameIndex == expectedFrameIndex &&
                        compareFrame?.Descriptor.FrameIndex == expectedFrameIndex &&
                        primaryFrame?.Descriptor.PresentationTime == expectedPresentationTime &&
                        compareFrame?.Descriptor.PresentationTime == expectedPresentationTime;
                });
                if (!markerPresented)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(10));
                }
            }

            var presentedTimes = GetPresentedFrameTimesAndValidateReadouts(window);
            Assert.Equal(expectedPresentationTime, presentedTimes.Primary);
            Assert.Equal(expectedPresentationTime, presentedTimes.Compare);

            _fixture.Run(() =>
            {
                var primaryFrame = GetPrivateField<DecodedFrameBuffer>(
                    window,
                    "_primaryFrameBuffer");
                var compareFrame = GetPrivateField<DecodedFrameBuffer>(
                    window,
                    "_compareFrameBuffer");
                Assert.NotNull(primaryFrame);
                Assert.NotNull(compareFrame);
                Assert.Equal(expectedFrameIndex, primaryFrame!.Descriptor.FrameIndex);
                Assert.Equal(expectedFrameIndex, compareFrame!.Descriptor.FrameIndex);

                var primaryBitmap = Assert.IsType<WriteableBitmap>(
                    window.FindControl<Image>("CustomVideoSurface")!.Source);
                var compareBitmap = Assert.IsType<WriteableBitmap>(
                    window.FindControl<Image>("CompareVideoSurface")!.Source);
                Assert.Equal(primaryBitmap.PixelSize, compareBitmap.PixelSize);

                using var primaryLocked = primaryBitmap.Lock();
                using var compareLocked = compareBitmap.Lock();
                var bytesPerRow = checked(primaryBitmap.PixelSize.Width * 4);
                Assert.True(primaryLocked.RowBytes >= bytesPerRow);
                Assert.True(compareLocked.RowBytes >= bytesPerRow);

                var primaryRow = new byte[bytesPerRow];
                var compareRow = new byte[bytesPerRow];
                var greenDominantPixelCount = 0;
                for (var row = 0; row < primaryBitmap.PixelSize.Height; row++)
                {
                    Marshal.Copy(
                        IntPtr.Add(primaryLocked.Address, row * primaryLocked.RowBytes),
                        primaryRow,
                        0,
                        bytesPerRow);
                    Marshal.Copy(
                        IntPtr.Add(compareLocked.Address, row * compareLocked.RowBytes),
                        compareRow,
                        0,
                        bytesPerRow);
                    Assert.Equal(primaryRow, compareRow);
                    for (var offset = 0; offset < bytesPerRow; offset += 4)
                    {
                        var blue = primaryRow[offset];
                        var green = primaryRow[offset + 1];
                        var red = primaryRow[offset + 2];
                        if (green >= 128 &&
                            green >= red + 40 &&
                            green >= blue + 40)
                        {
                            greenDominantPixelCount++;
                        }
                    }
                }

                Assert.True(
                    greenDominantPixelCount >=
                        (primaryBitmap.PixelSize.Width *
                            primaryBitmap.PixelSize.Height) / 100,
                    "The known synchronization marker frame did not contain the expected green visual cue. Green-dominant pixels=" +
                    greenDominantPixelCount);
            });
        }

        private async Task AssertPresentedFrameIdentityAndPixelsMatchAsync(
            MainWindow window,
            long expectedFrameIndex)
        {
            var expectedPairPresented = false;
            var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(2);
            while (!expectedPairPresented &&
                DateTimeOffset.UtcNow < deadline)
            {
                _fixture.Run(() =>
                {
                    var primaryFrame = GetPrivateField<DecodedFrameBuffer?>(
                        window,
                        "_primaryFrameBuffer");
                    var compareFrame = GetPrivateField<DecodedFrameBuffer?>(
                        window,
                        "_compareFrameBuffer");
                    expectedPairPresented =
                        primaryFrame?.Descriptor.FrameIndex ==
                            expectedFrameIndex &&
                        compareFrame?.Descriptor.FrameIndex ==
                            expectedFrameIndex;
                });
                if (!expectedPairPresented)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(10));
                }
            }

            GetPresentedFrameTimesAndValidateReadouts(window);
            _fixture.Run(() =>
            {
                var primaryFrame = GetPrivateField<DecodedFrameBuffer>(
                    window,
                    "_primaryFrameBuffer");
                var compareFrame = GetPrivateField<DecodedFrameBuffer>(
                    window,
                    "_compareFrameBuffer");
                Assert.NotNull(primaryFrame);
                Assert.NotNull(compareFrame);
                Assert.Equal(
                    expectedFrameIndex,
                    primaryFrame!.Descriptor.FrameIndex);
                Assert.Equal(
                    expectedFrameIndex,
                    compareFrame!.Descriptor.FrameIndex);
                Assert.Equal(
                    primaryFrame.Descriptor.PresentationTime,
                    compareFrame.Descriptor.PresentationTime);
                Assert.Equal(
                    primaryFrame.Descriptor.PresentationTimestamp,
                    compareFrame.Descriptor.PresentationTimestamp);
                Assert.Equal(
                    primaryFrame.Descriptor.DecodeTimestamp,
                    compareFrame.Descriptor.DecodeTimestamp);

                var primaryBitmap = Assert.IsType<WriteableBitmap>(
                    window.FindControl<Image>("CustomVideoSurface")!.Source);
                var compareBitmap = Assert.IsType<WriteableBitmap>(
                    window.FindControl<Image>("CompareVideoSurface")!.Source);
                Assert.Equal(primaryBitmap.PixelSize, compareBitmap.PixelSize);

                using var primaryLocked = primaryBitmap.Lock();
                using var compareLocked = compareBitmap.Lock();
                var bytesPerRow = checked(
                    primaryBitmap.PixelSize.Width * 4);
                Assert.True(primaryLocked.RowBytes >= bytesPerRow);
                Assert.True(compareLocked.RowBytes >= bytesPerRow);

                var primaryRow = new byte[bytesPerRow];
                var compareRow = new byte[bytesPerRow];
                for (var row = 0;
                    row < primaryBitmap.PixelSize.Height;
                    row++)
                {
                    Marshal.Copy(
                        IntPtr.Add(
                            primaryLocked.Address,
                            row * primaryLocked.RowBytes),
                        primaryRow,
                        0,
                        bytesPerRow);
                    Marshal.Copy(
                        IntPtr.Add(
                            compareLocked.Address,
                            row * compareLocked.RowBytes),
                        compareRow,
                        0,
                        bytesPerRow);
                    Assert.Equal(primaryRow, compareRow);
                }
            });
        }

        private static object ParsePane(MainWindow window, string paneName)
        {
            var paneType = window.GetType().GetNestedType("Pane", BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("Missing MainWindow.Pane enum.");
            return Enum.Parse(paneType, paneName);
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

        private async Task TryPauseAllPanePlaybackForCleanupAsync(MainWindow window)
        {
            _ = await PauseAllPanePlaybackWithDispatcherPumpAsync(
                window,
                required: false,
                "Timed out pausing all panes during cleanup.");
        }

        private async Task<bool> PauseAllPanePlaybackWithDispatcherPumpAsync(
            MainWindow window,
            bool required,
            string timeoutMessage)
        {
            var pauseTask = InvokeWindowTaskAsync(
                window,
                "PausePlaybackAsync",
                true,
                (SynchronizedOperationScope?)SynchronizedOperationScope.AllPanes);
            var pauseDeadline =
                DateTimeOffset.UtcNow + TimeSpan.FromSeconds(10);
            while (!pauseTask.IsCompleted &&
                DateTimeOffset.UtcNow < pauseDeadline)
            {
                _fixture.Run(() => { });
                await Task.Delay(TimeSpan.FromMilliseconds(20));
            }

            if (!pauseTask.IsCompleted)
            {
                Assert.False(required, timeoutMessage);
                return false;
            }

            await pauseTask;
            return true;
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
