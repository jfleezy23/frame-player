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
using FramePlayer.Mac.Views;
using Xunit;

namespace FramePlayer.Mac.Tests
{
    public sealed class MacTransportParityHarnessTests : IClassFixture<AvaloniaHeadlessFixture>
    {
        private static readonly TimeSpan PlaybackObservationDelay = TimeSpan.FromMilliseconds(900);
        private readonly AvaloniaHeadlessFixture _fixture;

        public MacTransportParityHarnessTests(AvaloniaHeadlessFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public void MacWindow_ExposesWindowsHarnessTransportContract()
        {
            var windowType = typeof(MainWindow);

            RequireMethod(windowType, "LaunchNewWindow");
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
                    _fixture.Run(() => window.Close());
                }
            }
        }

        [Fact]
        [Trait("Category", "ReleaseCandidate")]
        public async Task MacWindow_LoopPlaybackStaysInsideHarnessRange()
        {
            ConfigureRuntime();
            var file = FindCorpusFiles().First();

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

                SetLoopPlaybackEnabled(window!, true);
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

                var loopStatus = GetTextBlockText(window!, "LoopStatusTextBlock");
                Assert.StartsWith("Loop: ", loopStatus, StringComparison.Ordinal);
                Assert.DoesNotContain("off", loopStatus, StringComparison.OrdinalIgnoreCase);
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
            FfmpegRuntimeBootstrap.ConfigureForCurrentPlatform(FindRepositoryRoot());
        }

        private static string FindRepositoryRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                if (Directory.Exists(Path.Combine(directory.FullName, "Runtime", "macos")) &&
                    Directory.Exists(Path.Combine(directory.FullName, "src", "FramePlayer.Mac")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException("Could not find frame-player repository root from " + AppContext.BaseDirectory);
        }

        private static IReadOnlyList<string> FindCorpusFiles()
        {
            var corpus = Environment.GetEnvironmentVariable("FRAMEPLAYER_MAC_CORPUS");
            if (string.IsNullOrWhiteSpace(corpus))
            {
                corpus = Path.Combine(FindRepositoryRoot(), "Video Test Files");
            }

            Assert.True(Directory.Exists(corpus), "Corpus folder not found: " + corpus);
            var files = Directory.EnumerateFiles(corpus, "*", SearchOption.AllDirectories)
                .Where(path => new[] { ".avi", ".m4v", ".mkv", ".mov", ".mp4", ".ts", ".wmv" }
                    .Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
            Assert.NotEmpty(files);
            return files;
        }

        private async Task WaitForIndexAsync(FfmpegReviewEngine engine)
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

        private FfmpegReviewEngine GetPrimaryEngine(MainWindow window)
        {
            var field = typeof(MainWindow).GetField("_primaryEngine", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("Missing _primaryEngine field.");
            return (FfmpegReviewEngine)field.GetValue(window)!;
        }

        private FfmpegReviewEngine GetCompareEngine(MainWindow window)
        {
            var field = typeof(MainWindow).GetField("_compareEngine", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("Missing _compareEngine field.");
            return (FfmpegReviewEngine)field.GetValue(window)!;
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

        private void SetLoopPlaybackEnabled(MainWindow window, bool enabled)
        {
            var method = RequireMethod(window.GetType(), "SetLoopPlaybackEnabled", typeof(bool));
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

        private void SelectPane(MainWindow window, string paneName)
        {
            var paneType = window.GetType().GetNestedType("Pane", BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("Missing MainWindow.Pane enum.");
            var pane = Enum.Parse(paneType, paneName);
            var method = RequireMethod(window.GetType(), "SelectPane", paneType);
            _fixture.Run(() => method.Invoke(window, new[] { pane }));
        }

        private string GetTextBlockText(MainWindow window, string name)
        {
            var completed = new ManualResetEventSlim(false);
            var text = string.Empty;
            _fixture.Run(() =>
            {
                text = window.FindControl<TextBlock>(name)?.Text ?? string.Empty;
                completed.Set();
            });

            Assert.True(completed.Wait(TimeSpan.FromSeconds(5)), "Timed out reading " + name + ".");
            return text;
        }
    }
}
