using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FFmpeg.AutoGen;
using FramePlayer.Core.Models;
using FramePlayer.Engines.FFmpeg;
using FramePlayer.Services;
using Xunit;

namespace FramePlayer.NativeExport.Tests
{
    public sealed class NativeExportFilenameSafetyTests
    {
        [Theory]
        [InlineData("apostrophe's.mp4")]
        [InlineData("comma,name.mp4")]
        [InlineData("semicolon;name.mp4")]
        [InlineData("brackets[name].mp4")]
        [InlineData("colon:name.mp4")]
        [InlineData("back\\slash.mp4")]
        public void NativeExportGraphs_KeepFilenameOutOfFiltergraphSyntax(string fileName)
        {
            var sourcePath = "/tmp/" + fileName;
            var clipPlan = CreateClipPlan(sourcePath, "/tmp/clip-output.mp4", TimeSpan.FromSeconds(1));
            AssertOptionBoundSources(
                NativeClipExportService.BuildFilterGraph(clipPlan, includeAudio: true),
                NativeClipExportService.BuildFileSources(clipPlan, includeAudio: true),
                sourcePath,
                expectedSourceCount: 2);

            var comparePlan = CreateComparePlan(
                sourcePath,
                sourcePath,
                "/tmp/compare-output.mp4",
                TimeSpan.FromSeconds(1),
                selectedAudioHasStream: true);
            AssertOptionBoundSources(
                NativeCompareSideBySideExportService.BuildFilterGraph(comparePlan),
                NativeCompareSideBySideExportService.BuildFileSources(comparePlan),
                sourcePath,
                expectedSourceCount: 3);

            var audioPlan = new AudioInsertionPlan(
                sourcePath,
                sourcePath,
                "/tmp/audio-output.mp4",
                "filename-safety",
                TimeSpan.FromSeconds(1),
                string.Empty,
                string.Empty,
                string.Empty);
            AssertOptionBoundSources(
                NativeAudioInsertionService.BuildAudioFilterGraph(audioPlan),
                NativeAudioInsertionService.BuildFileSources(audioPlan),
                sourcePath,
                expectedSourceCount: 1);
        }

        [Fact]
        public async Task NativeExports_HandleCombinedFiltergraphPunctuationInRealFilenames()
        {
            if (!OperatingSystem.IsMacOS())
            {
                return;
            }

            var requireCorpus = string.Equals(
                Environment.GetEnvironmentVariable("FRAMEPLAYER_MAC_REQUIRE_CORPUS"),
                "1",
                StringComparison.Ordinal);
            var repositoryRoot = FindRepositoryRoot();
            var runtimeLibrary = Path.Combine(
                repositoryRoot,
                "Runtime",
                "macos",
                "osx-arm64",
                "ffmpeg",
                "libavcodec.62.dylib");
            var corpusRoot = Environment.GetEnvironmentVariable("FRAMEPLAYER_MAC_CORPUS");
            if (string.IsNullOrWhiteSpace(corpusRoot))
            {
                corpusRoot = Path.Combine(repositoryRoot, "Video Test Files");
            }

            if (!File.Exists(runtimeLibrary) || !Directory.Exists(corpusRoot))
            {
                Assert.False(
                    requireCorpus,
                    "Required native export runtime or corpus is unavailable. Runtime=" + runtimeLibrary +
                    " Corpus=" + corpusRoot);
                return;
            }

            FfmpegRuntimeBootstrap.ConfigureForCurrentPlatform(repositoryRoot);
            var sourceFilePath = FindH264Mp4(corpusRoot);
            Assert.False(
                string.IsNullOrWhiteSpace(sourceFilePath),
                "The native export filename regression requires an H.264 MP4 corpus source.");

            using var workspace = new TemporaryDirectory();
            var punctuationSourcePath = Path.Combine(
                workspace.Path,
                "source's,semicolon;[brackets]:colon\\backslash.mp4");
            var replacementAudioPath = Path.Combine(
                workspace.Path,
                "audio's,semicolon;[brackets]:colon\\backslash.wav");
            var audioOutputPath = Path.Combine(
                workspace.Path,
                "inserted's,semicolon;[brackets]:colon\\backslash.mp4");
            File.Copy(sourceFilePath!, punctuationSourcePath);
            WriteSineWave(replacementAudioPath, TimeSpan.FromSeconds(1));
            AssertInlineFileSourceOptionsAreRejected(replacementAudioPath);

            Assert.True(
                MediaProbeService.TryProbeVideoMediaInfo(punctuationSourcePath, out var sourceInfo, out var sourceError),
                sourceError);
            Assert.True(sourceInfo.Duration > TimeSpan.Zero);

            var audioResult = await NativeAudioInsertionService.InsertAsync(
                new AudioInsertionPlan(
                    punctuationSourcePath,
                    replacementAudioPath,
                    audioOutputPath,
                    "filename-safety-audio",
                    sourceInfo.Duration,
                    string.Empty,
                    string.Empty,
                    string.Empty));
            Assert.True(audioResult.Succeeded, audioResult.Message + Environment.NewLine + audioResult.StandardError);
            Assert.True(File.Exists(audioOutputPath), "Audio insertion did not produce an output file.");
            Assert.True(audioResult.ProbedHasAudioStream == true, "Audio insertion output did not contain audio.");

            var exportDuration = TimeSpan.FromMilliseconds(
                Math.Max(1d, Math.Min(750d, sourceInfo.Duration.TotalMilliseconds / 2d)));
            var clipOutputPath = Path.Combine(workspace.Path, "clip-output.mp4");
            var clipResult = await NativeClipExportService.ExportAsync(
                CreateClipPlan(
                    audioOutputPath,
                    clipOutputPath,
                    exportDuration,
                    sourceInfo.PixelWidth,
                    sourceInfo.PixelHeight));
            Assert.True(clipResult.Succeeded, clipResult.Message + Environment.NewLine + clipResult.StandardError);
            Assert.True(File.Exists(clipOutputPath), "Clip export did not produce an output file.");

            var compareOutputPath = Path.Combine(workspace.Path, "compare-output.mp4");
            var compareResult = await NativeCompareSideBySideExportService.ExportAsync(
                CreateComparePlan(
                    audioOutputPath,
                    punctuationSourcePath,
                    compareOutputPath,
                    exportDuration,
                    selectedAudioHasStream: true,
                    sourceWidth: sourceInfo.PixelWidth,
                    sourceHeight: sourceInfo.PixelHeight));
            Assert.True(compareResult.Succeeded, compareResult.Message + Environment.NewLine + compareResult.StandardError);
            Assert.True(File.Exists(compareOutputPath), "Compare export did not produce an output file.");
            Assert.True(compareResult.ProbedHasAudioStream == true, "Compare export did not preserve selected audio.");
        }

        private static void AssertOptionBoundSources(
            string filterGraph,
            IReadOnlyList<NativeExportSupport.FilterFileSource> sources,
            string expectedPath,
            int expectedSourceCount)
        {
            Assert.DoesNotContain(expectedPath, filterGraph, StringComparison.Ordinal);
            Assert.DoesNotContain("filename=", filterGraph, StringComparison.Ordinal);
            Assert.Equal(expectedSourceCount, sources.Count);
            Assert.All(sources, source => Assert.Equal(expectedPath, source.FilePath));
            Assert.All(sources, source => Assert.Contains(source.ContextName, filterGraph, StringComparison.Ordinal));
        }

        private static ClipExportPlan CreateClipPlan(
            string sourceFilePath,
            string outputFilePath,
            TimeSpan duration,
            int sourceWidth = 128,
            int sourceHeight = 72)
        {
            return new ClipExportPlan(
                sourceFilePath,
                outputFilePath,
                "filename-safety-clip",
                "primary",
                true,
                TimeSpan.Zero,
                duration,
                0,
                null,
                "filename-safety",
                PaneViewportSnapshot.CreateFullFrame(sourceWidth, sourceHeight),
                string.Empty,
                string.Empty,
                string.Empty);
        }

        private static CompareSideBySideExportPlan CreateComparePlan(
            string primarySourceFilePath,
            string compareSourceFilePath,
            string outputFilePath,
            TimeSpan duration,
            bool selectedAudioHasStream,
            int sourceWidth = 128,
            int sourceHeight = 72)
        {
            return new CompareSideBySideExportPlan
            {
                OutputFilePath = outputFilePath,
                Mode = CompareSideBySideExportMode.Loop,
                AudioSource = CompareSideBySideExportAudioSource.Primary,
                PrimarySourceFilePath = primarySourceFilePath,
                CompareSourceFilePath = compareSourceFilePath,
                PrimaryStartTime = TimeSpan.Zero,
                PrimaryContentDuration = duration,
                PrimaryLeadingPad = TimeSpan.Zero,
                PrimaryTrailingPad = TimeSpan.Zero,
                CompareStartTime = TimeSpan.Zero,
                CompareContentDuration = duration,
                CompareLeadingPad = TimeSpan.Zero,
                CompareTrailingPad = TimeSpan.Zero,
                PrimaryEndBoundaryStrategy = "filename-safety-primary",
                CompareEndBoundaryStrategy = "filename-safety-compare",
                OutputDuration = duration,
                PrimaryRenderWidth = sourceWidth,
                PrimaryRenderHeight = sourceHeight,
                CompareRenderWidth = sourceWidth,
                CompareRenderHeight = sourceHeight,
                OutputWidth = sourceWidth * 2,
                OutputHeight = sourceHeight,
                PrimaryViewportSnapshot = PaneViewportSnapshot.CreateFullFrame(sourceWidth, sourceHeight),
                CompareViewportSnapshot = PaneViewportSnapshot.CreateFullFrame(sourceWidth, sourceHeight),
                SelectedAudioHasStream = selectedAudioHasStream,
                FfmpegArguments = string.Empty,
                FfmpegPath = string.Empty,
                FfprobePath = string.Empty
            };
        }

        private static string? FindH264Mp4(string corpusRoot)
        {
            foreach (var filePath in Directory.EnumerateFiles(corpusRoot, "*.mp4", SearchOption.AllDirectories)
                         .OrderBy(path => path, StringComparer.Ordinal))
            {
                if (!MediaProbeService.TryProbeVideoMediaInfo(filePath, out var mediaInfo, out _))
                {
                    continue;
                }

                var codecName = mediaInfo.VideoCodecName.Replace(".", string.Empty, StringComparison.Ordinal).Trim();
                if (string.Equals(codecName, "h264", StringComparison.OrdinalIgnoreCase))
                {
                    return filePath;
                }
            }

            return null;
        }

        private static unsafe void AssertInlineFileSourceOptionsAreRejected(string filePath)
        {
            AVFilterGraph* filterGraph = ffmpeg.avfilter_graph_alloc();
            Assert.True(filterGraph != null, "Could not allocate the inline-option regression filter graph.");

            var rejected = false;
            try
            {
                try
                {
                    NativeExportSupport.ConfigureFilterGraph(
                        filterGraph,
                        "amovie@source-name=filename='/tmp/must-not-be-parsed.wav',anullsink",
                        new[]
                        {
                            new NativeExportSupport.FilterFileSource(
                                "source-name",
                                "amovie",
                                filePath)
                        });
                }
                catch (ArgumentException)
                {
                    rejected = true;
                }

                Assert.True(rejected, "Instance-qualified inline amovie options were not rejected.");
            }
            finally
            {
                NativeExportSupport.FreeFilterGraph(ref filterGraph);
            }
        }

        private static string FindRepositoryRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "FramePlayer.csproj")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException(
                "Could not locate the frame-player repository from " + AppContext.BaseDirectory);
        }

        private static void WriteSineWave(string filePath, TimeSpan duration)
        {
            const int sampleRate = 48000;
            const short bitsPerSample = 16;
            const short channelCount = 1;
            const double toneFrequency = 440d;
            const short amplitude = 8192;

            var sampleCount = Math.Max(
                1,
                (int)Math.Round(duration.TotalSeconds * sampleRate, MidpointRounding.AwayFromZero));
            var bytesPerSample = bitsPerSample / 8;
            var dataLength = sampleCount * channelCount * bytesPerSample;

            using var stream = File.Create(filePath);
            using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: false);
            writer.Write(Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(36 + dataLength);
            writer.Write(Encoding.ASCII.GetBytes("WAVE"));
            writer.Write(Encoding.ASCII.GetBytes("fmt "));
            writer.Write(16);
            writer.Write((short)1);
            writer.Write(channelCount);
            writer.Write(sampleRate);
            writer.Write(sampleRate * channelCount * bytesPerSample);
            writer.Write((short)(channelCount * bytesPerSample));
            writer.Write(bitsPerSample);
            writer.Write(Encoding.ASCII.GetBytes("data"));
            writer.Write(dataLength);

            for (var sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
            {
                var sample = (short)Math.Round(
                    amplitude * Math.Sin(2d * Math.PI * toneFrequency * sampleIndex / sampleRate),
                    MidpointRounding.AwayFromZero);
                writer.Write(sample);
            }
        }

        private sealed class TemporaryDirectory : IDisposable
        {
            internal TemporaryDirectory()
            {
                Path = Directory.CreateDirectory(
                    System.IO.Path.Combine(
                        System.IO.Path.GetTempPath(),
                        "frame-player-native-export-filename-" + Guid.NewGuid().ToString("N"))).FullName;
            }

            internal string Path { get; }

            public void Dispose()
            {
                try
                {
                    Directory.Delete(Path, recursive: true);
                }
                catch
                {
                    // Best-effort cleanup for native handles that may outlive a failed assertion.
                }
            }
        }
    }
}
