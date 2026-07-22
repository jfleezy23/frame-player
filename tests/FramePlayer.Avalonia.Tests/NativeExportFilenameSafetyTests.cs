using System;
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
            var compareSourcePath = "/tmp/compare-" + fileName;
            var clipPlan = CreateClipPlan(sourcePath, "/tmp/clip-output.mp4", TimeSpan.FromSeconds(1));
            AssertOptionBoundSources(
                NativeClipExportService.BuildFilterGraph(clipPlan, includeAudio: true),
                NativeClipExportService.BuildFileSources(clipPlan, includeAudio: true),
                sourcePath,
                expectedSourceCount: 2);

            var comparePlan = CreateComparePlan(
                sourcePath,
                compareSourcePath,
                "/tmp/compare-output.mp4",
                TimeSpan.FromSeconds(1),
                selectedAudioHasStream: true);
            var compareGraph = NativeCompareSideBySideExportService.BuildFilterGraph(comparePlan);
            var compareSources = NativeCompareSideBySideExportService.BuildFileSources(comparePlan);
            Assert.DoesNotContain(sourcePath, compareGraph, StringComparison.Ordinal);
            Assert.DoesNotContain(compareSourcePath, compareGraph, StringComparison.Ordinal);
            Assert.DoesNotContain("filename=", compareGraph, StringComparison.Ordinal);
            Assert.Collection(
                compareSources,
                source => AssertFileSource(source, "primaryvsrc", "movie", sourcePath),
                source => AssertFileSource(source, "comparevsrc", "movie", compareSourcePath),
                source => AssertFileSource(source, "audiosrc", "amovie", sourcePath));
            Assert.All(compareSources, source => Assert.Contains(source.ContextName, compareGraph, StringComparison.Ordinal));

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

        [Theory]
        [InlineData(CompareSideBySideExportAudioSource.Primary, true, 3)]
        [InlineData(CompareSideBySideExportAudioSource.Compare, true, 3)]
        [InlineData(CompareSideBySideExportAudioSource.Primary, false, 2)]
        public void NativeCompareFileSources_BindTheSelectedAudioPath(
            CompareSideBySideExportAudioSource audioSource,
            bool selectedAudioHasStream,
            int expectedSourceCount)
        {
            const string primaryPath = "/tmp/primary.mp4";
            const string comparePath = "/tmp/compare.mp4";
            var plan = CreateComparePlan(
                primaryPath,
                comparePath,
                "/tmp/output.mp4",
                TimeSpan.FromSeconds(1),
                selectedAudioHasStream,
                audioSource: audioSource);

            var sources = NativeCompareSideBySideExportService.BuildFileSources(plan);

            Assert.Equal(expectedSourceCount, sources.Length);
            AssertFileSource(sources[0], "primaryvsrc", "movie", primaryPath);
            AssertFileSource(sources[1], "comparevsrc", "movie", comparePath);
            if (selectedAudioHasStream)
            {
                var expectedAudioPath = audioSource == CompareSideBySideExportAudioSource.Compare
                    ? comparePath
                    : primaryPath;
                AssertFileSource(sources[2], "audiosrc", "amovie", expectedAudioPath);
            }
        }

        [Fact]
        public async Task UnifiedExportHost_HandlesWindowsUnicodeAndPunctuationFilenamesAcrossGraphs()
        {
            if (!OperatingSystem.IsWindows())
            {
                return;
            }

            Assert.True(
                ExportHostClient.IsBundledRuntimeAvailable,
                ExportHostClient.GetRuntimeAvailabilityMessage());

            using var workspace = new TemporaryDirectory();
            var sourcePath = Path.Combine(workspace.Path, "source's,semicolon;[brackets]-雪.avi");
            var clipOutputPath = Path.Combine(workspace.Path, "clip's,semicolon;[brackets]-雪.mp4");
            var replacementAudioPath = Path.Combine(workspace.Path, "audio's,semicolon;[brackets]-雪.wav");
            var audioOutputPath = Path.Combine(workspace.Path, "inserted's,semicolon;[brackets]-雪.mp4");
            var compareOutputPath = Path.Combine(workspace.Path, "compare's,semicolon;[brackets]-雪.mp4");
            WriteAvi(sourcePath, width: 16, height: 16, frameCount: 25, framesPerSecond: 25);
            WriteSineWave(replacementAudioPath, TimeSpan.FromSeconds(1));

            var client = new ExportHostClient();
            var exportDuration = TimeSpan.FromSeconds(1);
            var clipResult = await client.ExportClipAsync(
                CreateClipPlan(
                    sourcePath,
                    clipOutputPath,
                    exportDuration,
                    sourceWidth: 16,
                    sourceHeight: 16));
            Assert.True(clipResult.Succeeded, clipResult.Message + Environment.NewLine + clipResult.StandardError);
            var clipInfo = await client.ProbeAsync(clipOutputPath);
            Assert.Equal(16, clipInfo.PixelWidth);
            Assert.Equal(16, clipInfo.PixelHeight);

            var audioResult = await client.InsertAudioAsync(
                new AudioInsertionPlan(
                    clipOutputPath,
                    replacementAudioPath,
                    audioOutputPath,
                    "filename-safety-windows-audio",
                    exportDuration,
                    string.Empty,
                    string.Empty,
                    string.Empty));
            Assert.True(audioResult.Succeeded, audioResult.Message + Environment.NewLine + audioResult.StandardError);
            var audioInfo = await client.ProbeAsync(audioOutputPath);
            Assert.True(audioInfo.HasAudioStream, "The Windows audio-insertion output did not contain audio.");

            var compareResult = await client.ExportCompareAsync(
                CreateComparePlan(
                    audioOutputPath,
                    clipOutputPath,
                    compareOutputPath,
                    exportDuration,
                    selectedAudioHasStream: true,
                    sourceWidth: clipInfo.PixelWidth,
                    sourceHeight: clipInfo.PixelHeight));
            Assert.True(compareResult.Succeeded, compareResult.Message + Environment.NewLine + compareResult.StandardError);
            var compareInfo = await client.ProbeAsync(compareOutputPath);
            Assert.True(compareInfo.HasAudioStream, "The Windows compare output did not preserve selected audio.");
            Assert.Equal(32, compareInfo.PixelWidth);
            Assert.Equal(16, compareInfo.PixelHeight);
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
            Assert.True(audioResult.ProbedHasAudioStream, "Audio insertion output did not contain audio.");

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
            Assert.True(compareResult.ProbedHasAudioStream, "Compare export did not preserve selected audio.");
        }

        private static void AssertOptionBoundSources(
            string filterGraph,
            NativeExportSupport.FilterFileSource[] sources,
            string expectedPath,
            int expectedSourceCount)
        {
            Assert.DoesNotContain(expectedPath, filterGraph, StringComparison.Ordinal);
            Assert.DoesNotContain("filename=", filterGraph, StringComparison.Ordinal);
            Assert.Equal(expectedSourceCount, sources.Length);
            Assert.All(sources, source => Assert.Equal(expectedPath, source.FilePath));
            Assert.All(sources, source => Assert.Contains(source.ContextName, filterGraph, StringComparison.Ordinal));
        }

        private static void AssertFileSource(
            NativeExportSupport.FilterFileSource source,
            string expectedContextName,
            string expectedFilterName,
            string expectedPath)
        {
            Assert.Equal(expectedContextName, source.InstanceName);
            Assert.Equal(expectedFilterName, source.FilterName);
            Assert.Equal(expectedPath, source.FilePath);
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
            int sourceHeight = 72,
            CompareSideBySideExportAudioSource audioSource = CompareSideBySideExportAudioSource.Primary)
        {
            return new CompareSideBySideExportPlan
            {
                OutputFilePath = outputFilePath,
                Mode = CompareSideBySideExportMode.Loop,
                AudioSource = audioSource,
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
                if (File.Exists(Path.Combine(
                    directory.FullName,
                    "src",
                    "FramePlayer.Avalonia",
                    "FramePlayer.Avalonia.csproj")))
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

        private static void WriteAvi(
            string filePath,
            int width,
            int height,
            int frameCount,
            int framesPerSecond)
        {
            const short bitsPerPixel = 24;
            var rowStride = ((width * bitsPerPixel / 8) + 3) & ~3;
            var imageSize = rowStride * height;

            using var stream = File.Create(filePath);
            using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: false);
            var riffSizePosition = BeginContainer(writer, "RIFF", "AVI ");
            var headerListSizePosition = BeginContainer(writer, "LIST", "hdrl");

            var mainHeaderSizePosition = BeginChunk(writer, "avih");
            writer.Write(1_000_000 / framesPerSecond);
            writer.Write(imageSize * framesPerSecond);
            writer.Write(0);
            writer.Write(0);
            writer.Write(frameCount);
            writer.Write(0);
            writer.Write(1);
            writer.Write(imageSize);
            writer.Write(width);
            writer.Write(height);
            writer.Write(0);
            writer.Write(0);
            writer.Write(0);
            writer.Write(0);
            EndChunk(writer, mainHeaderSizePosition);

            var streamListSizePosition = BeginContainer(writer, "LIST", "strl");
            var streamHeaderSizePosition = BeginChunk(writer, "strh");
            WriteFourCc(writer, "vids");
            WriteFourCc(writer, "DIB ");
            writer.Write(0);
            writer.Write((short)0);
            writer.Write((short)0);
            writer.Write(0);
            writer.Write(1);
            writer.Write(framesPerSecond);
            writer.Write(0);
            writer.Write(frameCount);
            writer.Write(imageSize);
            writer.Write(-1);
            writer.Write(0);
            writer.Write((short)0);
            writer.Write((short)0);
            writer.Write((short)width);
            writer.Write((short)height);
            EndChunk(writer, streamHeaderSizePosition);

            var formatSizePosition = BeginChunk(writer, "strf");
            writer.Write(40);
            writer.Write(width);
            writer.Write(height);
            writer.Write((short)1);
            writer.Write(bitsPerPixel);
            writer.Write(0);
            writer.Write(imageSize);
            writer.Write(0);
            writer.Write(0);
            writer.Write(0);
            writer.Write(0);
            EndChunk(writer, formatSizePosition);
            EndChunk(writer, streamListSizePosition);
            EndChunk(writer, headerListSizePosition);

            var movieListSizePosition = BeginContainer(writer, "LIST", "movi");
            var paddingLength = rowStride - (width * bitsPerPixel / 8);
            for (var frameIndex = 0; frameIndex < frameCount; frameIndex++)
            {
                var frameSizePosition = BeginChunk(writer, "00db");
                for (var y = 0; y < height; y++)
                {
                    for (var x = 0; x < width; x++)
                    {
                        writer.Write((byte)((x * 255 / Math.Max(1, width - 1) + frameIndex * 3) & 0xff));
                        writer.Write((byte)((y * 255 / Math.Max(1, height - 1) + frameIndex * 5) & 0xff));
                        writer.Write((byte)(128 + (frameIndex % 64)));
                    }

                    for (var paddingIndex = 0; paddingIndex < paddingLength; paddingIndex++)
                    {
                        writer.Write((byte)0);
                    }
                }

                EndChunk(writer, frameSizePosition);
            }

            EndChunk(writer, movieListSizePosition);
            EndChunk(writer, riffSizePosition);
        }

        private static long BeginContainer(BinaryWriter writer, string containerType, string contentsType)
        {
            WriteFourCc(writer, containerType);
            var sizePosition = writer.BaseStream.Position;
            writer.Write(0);
            WriteFourCc(writer, contentsType);
            return sizePosition;
        }

        private static long BeginChunk(BinaryWriter writer, string chunkType)
        {
            WriteFourCc(writer, chunkType);
            var sizePosition = writer.BaseStream.Position;
            writer.Write(0);
            return sizePosition;
        }

        private static void EndChunk(BinaryWriter writer, long sizePosition)
        {
            var endPosition = writer.BaseStream.Position;
            var chunkSize = checked((int)(endPosition - sizePosition - sizeof(int)));
            writer.BaseStream.Position = sizePosition;
            writer.Write(chunkSize);
            writer.BaseStream.Position = endPosition;
            if ((chunkSize & 1) != 0)
            {
                writer.Write((byte)0);
            }
        }

        private static void WriteFourCc(BinaryWriter writer, string value)
        {
            Assert.Equal(4, value.Length);
            writer.Write(Encoding.ASCII.GetBytes(value));
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
