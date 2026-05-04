using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using FramePlayer.Core.Models;
using FramePlayer.Engines.FFmpeg;
using FramePlayer.Mac.Views;
using FramePlayer.Services;
using Xunit;

namespace FramePlayer.Mac.Tests
{
    public sealed class MacExportReleaseCandidateTests : IClassFixture<AvaloniaHeadlessFixture>
    {
        private readonly AvaloniaHeadlessFixture _fixture;

        public MacExportReleaseCandidateTests(AvaloniaHeadlessFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        [Trait("Category", "ReleaseCandidate")]
        public async Task MacWindow_ExportsLoopClipCompareDiagnosticsAndAudioInsertion()
        {
            var previousRuntimeBase = Environment.GetEnvironmentVariable(ExportHostClient.MacRuntimeBaseEnvironmentVariable);
            var previousExportHostExecutable = Environment.GetEnvironmentVariable(ExportHostClient.MacExportHostExecutableEnvironmentVariable);
            using var temp = new TemporaryDirectory();
            MainWindow? window = null;
            try
            {
                ConfigureRuntime();
                var files = FindCorpusFiles();
                var h264Mp4 = await FindH264Mp4Async(files);
                Assert.False(string.IsNullOrWhiteSpace(h264Mp4), "The corpus does not contain an H.264 MP4 needed to validate audio insertion.");
                var primaryFile = h264Mp4!;
                var compareFile = files.FirstOrDefault(path => string.Equals(Path.GetFileName(path), "sample-test.m4v", StringComparison.OrdinalIgnoreCase))
                    ?? files.FirstOrDefault(path => !string.Equals(path, primaryFile, StringComparison.OrdinalIgnoreCase))
                    ?? primaryFile;

                window = CreateWindow();
                SetCompareMode(window!, true);
                await InvokeWindowTaskAsync(window!, "OpenMediaAsync", primaryFile, "pane-primary");
                await InvokeWindowTaskAsync(window!, "OpenMediaAsync", compareFile, "pane-compare");

                var compareOutput = Path.Combine(temp.Path, "mac-compare-export.mp4");
                var compareResult = await InvokeWindowTaskAsync<CompareSideBySideExportResult?>(
                    window!,
                    "ExportSideBySideCompareAsync",
                    compareOutput,
                    CompareSideBySideExportMode.WholeVideo,
                    CompareSideBySideExportAudioSource.Primary);
                Assert.NotNull(compareResult);
                Assert.True(compareResult!.Succeeded, compareResult.Message);
                Assert.True(File.Exists(compareOutput), "Compare export did not create an output file.");
                AssertProbeSucceeds(compareOutput, expectAudio: null);

                await SetLoopRangeAsync(window!, "pane-primary");

                var clipOutput = Path.Combine(temp.Path, "mac-loop-export.mp4");
                var clipResult = await InvokeWindowTaskAsync<ClipExportResult?>(
                    window!,
                    "ExportLoopClipAsync",
                    clipOutput,
                    "pane-primary");
                Assert.NotNull(clipResult);
                Assert.True(clipResult!.Succeeded, clipResult.Message);
                Assert.True(File.Exists(clipOutput), "Loop export did not create an output file.");
                AssertProbeSucceeds(clipOutput, expectAudio: null);

                var diagnosticsOutput = Path.Combine(temp.Path, "diagnostics.txt");
                var diagnosticsPath = await InvokeWindowTaskAsync<string?>(
                    window!,
                    "ExportDiagnosticsAsync",
                    diagnosticsOutput);
                Assert.Equal(diagnosticsOutput, diagnosticsPath);
                Assert.Contains("Frame Player macOS diagnostics", File.ReadAllText(diagnosticsOutput));

                SetCompareMode(window!, false);
                var wavPath = Path.Combine(temp.Path, "replacement.wav");
                WriteSineWaveWav(wavPath, TimeSpan.FromSeconds(1));
                var audioOutput = Path.Combine(temp.Path, "audio-inserted.mp4");
                var audioResult = await InvokeWindowTaskAsync<AudioInsertionResult?>(
                    window!,
                    "ReplaceAudioTrackAsync",
                    wavPath,
                    audioOutput);
                Assert.NotNull(audioResult);
                Assert.True(audioResult!.Succeeded, audioResult.Message);
                Assert.True(File.Exists(audioOutput), "Audio insertion did not create an output file.");
                AssertProbeSucceeds(audioOutput, expectAudio: true);
            }
            finally
            {
                if (window != null)
                {
                    _fixture.Run(() => window.Close());
                }

                Environment.SetEnvironmentVariable(ExportHostClient.MacRuntimeBaseEnvironmentVariable, previousRuntimeBase);
                Environment.SetEnvironmentVariable(ExportHostClient.MacExportHostExecutableEnvironmentVariable, previousExportHostExecutable);
            }
        }

        private static void ConfigureRuntime()
        {
            var runtimeBaseDirectory = ResolveRuntimeBaseDirectory();
            var testOutputExportHost = Path.Combine(AppContext.BaseDirectory, "FramePlayer.Mac");
            Environment.SetEnvironmentVariable(
                ExportHostClient.MacRuntimeBaseEnvironmentVariable,
                runtimeBaseDirectory);
            if (File.Exists(testOutputExportHost))
            {
                Environment.SetEnvironmentVariable(
                    ExportHostClient.MacExportHostExecutableEnvironmentVariable,
                    testOutputExportHost);
            }

            FfmpegRuntimeBootstrap.ConfigureForCurrentPlatform(runtimeBaseDirectory);
        }

        private static string ResolveRuntimeBaseDirectory()
        {
            var appBundle = Environment.GetEnvironmentVariable("FRAMEPLAYER_MAC_APP_BUNDLE");
            if (!string.IsNullOrWhiteSpace(appBundle))
            {
                return Path.Combine(appBundle, "Contents", "MacOS");
            }

            var runtimeBase = Environment.GetEnvironmentVariable("FRAMEPLAYER_MAC_RUNTIME_BASE");
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

        private static async Task<string?> FindH264Mp4Async(IReadOnlyList<string> files)
        {
            var factory = new VideoReviewEngineFactory(new FfmpegReviewEngineOptionsProvider(new AppPreferencesService()));
            var mp4Files = files
                .Where(path => string.Equals(Path.GetExtension(path), ".mp4", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(path => string.Equals(Path.GetFileName(path), "sample-test.mp4", StringComparison.OrdinalIgnoreCase))
                .ThenBy(path => path, StringComparer.Ordinal)
                .ToArray();
            foreach (var file in mp4Files)
            {
                using var engine = factory.Create("probe");
                await engine.OpenAsync(file);
                if (string.Equals(engine.MediaInfo.VideoCodecName.Replace(".", string.Empty), "h264", StringComparison.OrdinalIgnoreCase))
                {
                    return file;
                }
            }

            return null;
        }

        private async Task SetLoopRangeAsync(MainWindow window, string paneId)
        {
            var engine = GetEngine(window, paneId);
            await WaitForIndexAsync(engine);
            var step = engine.MediaInfo.PositionStep > TimeSpan.Zero
                ? engine.MediaInfo.PositionStep
                : TimeSpan.FromMilliseconds(50);
            var start = TimeSpan.FromTicks(Math.Max(step.Ticks * 3, TimeSpan.FromMilliseconds(150).Ticks));
            var end = start + TimeSpan.FromTicks(Math.Max(step.Ticks * 6, TimeSpan.FromMilliseconds(350).Ticks));
            if (end >= engine.MediaInfo.Duration && engine.MediaInfo.Duration > TimeSpan.FromMilliseconds(500))
            {
                start = TimeSpan.Zero;
                end = TimeSpan.FromTicks(engine.MediaInfo.Duration.Ticks / 2);
            }

            Assert.True(await InvokeWindowTaskAsync<bool>(window, "SetTimelineLoopMarkerAtAsync", paneId, LoopPlaybackMarkerEndpoint.In, start));
            Assert.True(await InvokeWindowTaskAsync<bool>(window, "SetTimelineLoopMarkerAtAsync", paneId, LoopPlaybackMarkerEndpoint.Out, end));
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

        private static void AssertProbeSucceeds(string filePath, bool? expectAudio)
        {
            Assert.True(MediaProbeService.TryProbeVideoMediaInfo(filePath, out var mediaInfo, out var error), error);
            Assert.True(mediaInfo.PixelWidth > 0, "Output width was not probed.");
            Assert.True(mediaInfo.PixelHeight > 0, "Output height was not probed.");
            if (expectAudio.HasValue)
            {
                Assert.Equal(expectAudio.Value, mediaInfo.HasAudioStream);
            }
        }

        private FfmpegReviewEngine GetEngine(MainWindow window, string paneId)
        {
            var fieldName = string.Equals(paneId, "pane-compare", StringComparison.Ordinal)
                ? "_compareEngine"
                : "_primaryEngine";
            var field = typeof(MainWindow).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("Missing " + fieldName + " field.");
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

        private void SetCompareMode(MainWindow window, bool enabled)
        {
            var completed = new ManualResetEventSlim(false);
            _fixture.Run(() =>
            {
                var checkBox = window.FindControl<CheckBox>("CompareModeCheckBox")
                    ?? throw new InvalidOperationException("Missing CompareModeCheckBox.");
                checkBox.IsChecked = enabled;
                completed.Set();
            });
            Assert.True(completed.Wait(TimeSpan.FromSeconds(5)), "Timed out setting compare mode.");
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

        private static void WriteSineWaveWav(string path, TimeSpan duration)
        {
            const int sampleRate = 44100;
            const short channelCount = 1;
            const short bitsPerSample = 16;
            var sampleCount = Math.Max(1, (int)(duration.TotalSeconds * sampleRate));
            var dataLength = sampleCount * channelCount * (bitsPerSample / 8);

            using var stream = File.Create(path);
            using var writer = new BinaryWriter(stream);
            writer.Write(new[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F' });
            writer.Write(36 + dataLength);
            writer.Write(new[] { (byte)'W', (byte)'A', (byte)'V', (byte)'E' });
            writer.Write(new[] { (byte)'f', (byte)'m', (byte)'t', (byte)' ' });
            writer.Write(16);
            writer.Write((short)1);
            writer.Write(channelCount);
            writer.Write(sampleRate);
            writer.Write(sampleRate * channelCount * (bitsPerSample / 8));
            writer.Write((short)(channelCount * (bitsPerSample / 8)));
            writer.Write(bitsPerSample);
            writer.Write(new[] { (byte)'d', (byte)'a', (byte)'t', (byte)'a' });
            writer.Write(dataLength);

            for (var index = 0; index < sampleCount; index++)
            {
                var sample = (short)(Math.Sin(2d * Math.PI * 440d * index / sampleRate) * short.MaxValue * 0.2d);
                writer.Write(sample);
            }
        }

        private sealed class TemporaryDirectory : IDisposable
        {
            public TemporaryDirectory()
            {
                Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "frameplayer-mac-export-tests-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(Path);
            }

            public string Path { get; }

            public void Dispose()
            {
                if (Directory.Exists(Path))
                {
                    Directory.Delete(Path, true);
                }
            }
        }
    }
}
