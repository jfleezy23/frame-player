using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FramePlayer.Core.Models;
using FramePlayer.Engines.FFmpeg;
using FramePlayer.Services;
using Xunit;

namespace FramePlayer.Core.Tests
{
    public sealed class ExportHostIntegrationTests
    {
        [Fact]
        public async Task ExportHost_Probe_ReturnsVideoMetadata_ForGeneratedSourceClip()
        {
            var ffmpegToolPath = TryGetLocalFfmpegToolPath();
            if (string.IsNullOrWhiteSpace(ffmpegToolPath))
            {
                return;
            }

            using var workspace = new TemporaryWorkspace();
            var sourceFilePath = Path.Combine(workspace.DirectoryPath, "source.mp4");
            await GenerateSyntheticSourceClipAsync(ffmpegToolPath, sourceFilePath);

            var result = await RunHostRequestAsync(
                new ExportHostRequest
                {
                    Operation = ExportHostClient.ProbeOperation,
                    ProbeFilePath = sourceFilePath
                });

            AssertHostSucceeded(result);
            var response = result.Response;
            Assert.NotNull(response);
            Assert.NotNull(response!.MediaInfo);
            Assert.True(response.MediaInfo.Duration > TimeSpan.Zero);
            Assert.Equal(128, response.MediaInfo.PixelWidth);
            Assert.Equal(72, response.MediaInfo.PixelHeight);
            Assert.False(string.IsNullOrWhiteSpace(response.MediaInfo.VideoCodecName));
        }

        [Fact]
        public async Task ExportHost_AudioInsertion_ProducesAudioBearingH264Mp4()
        {
            var ffmpegToolPath = TryGetLocalFfmpegToolPath();
            if (string.IsNullOrWhiteSpace(ffmpegToolPath))
            {
                return;
            }

            using var workspace = new TemporaryWorkspace();
            var sourceFilePath = Path.Combine(workspace.DirectoryPath, "source.mp4");
            var audioFilePath = Path.Combine(workspace.DirectoryPath, "replacement.wav");
            var outputFilePath = Path.Combine(workspace.DirectoryPath, "with-audio.mp4");

            await GenerateSyntheticSourceClipAsync(ffmpegToolPath, sourceFilePath);
            WriteSineWaveFile(audioFilePath, TimeSpan.FromMilliseconds(420));

            var sourceProbe = await ProbeAsync(sourceFilePath);
            Assert.Equal("h264", NormalizeCodecName(sourceProbe.VideoCodecName));
            var insertionResult = await RunHostRequestAsync(
                new ExportHostRequest
                {
                    Operation = ExportHostClient.AudioInsertionOperation,
                    AudioInsertionPlan = new AudioInsertionPlan(
                        sourceFilePath,
                        audioFilePath,
                        outputFilePath,
                        "integration-audio-insertion",
                        sourceProbe.Duration,
                        string.Empty,
                        string.Empty,
                        string.Empty)
                });

            AssertHostSucceeded(insertionResult);
            Assert.True(File.Exists(outputFilePath), "Audio insertion did not produce an output file.");

            var outputProbe = await ProbeAsync(outputFilePath);
            Assert.True(outputProbe.HasAudioStream, "Audio insertion output did not report an audio stream.");
            Assert.True(outputProbe.Duration > TimeSpan.Zero);
            Assert.Equal(sourceProbe.PixelWidth, outputProbe.PixelWidth);
            Assert.Equal(sourceProbe.PixelHeight, outputProbe.PixelHeight);
            Assert.InRange(
                Math.Abs((outputProbe.Duration - sourceProbe.Duration).TotalMilliseconds),
                0,
                120);
        }

        [Theory]
        [InlineData("h264-mp4", ".mp4", "h264")]
        [InlineData("mjpeg-avi", ".avi", "mjpeg")]
        public async Task ReviewEngine_OpenAudioBearingClip_ReportsPlayableAudio(
            string sampleName,
            string fileExtension,
            string expectedVideoCodec)
        {
            var ffmpegToolPath = TryGetLocalFfmpegToolPath();
            if (string.IsNullOrWhiteSpace(ffmpegToolPath))
            {
                return;
            }

            using var workspace = new TemporaryWorkspace();
            var sourceFilePath = Path.Combine(workspace.DirectoryPath, sampleName + fileExtension);
            await GenerateSyntheticAudioBearingSourceClipAsync(ffmpegToolPath, sourceFilePath, expectedVideoCodec);

            using var engine = new FfmpegReviewEngine();
            await engine.OpenAsync(sourceFilePath);

            var mediaInfo = engine.MediaInfo;
            Assert.Equal(expectedVideoCodec, NormalizeCodecName(mediaInfo.VideoCodecName));
            Assert.True(mediaInfo.HasAudioStream, sampleName + " did not report an audio stream.");
            Assert.True(mediaInfo.IsAudioPlaybackAvailable, sampleName + " did not expose a decodable audio stream.");
            Assert.True(mediaInfo.AudioStreamIndex >= 0, sampleName + " did not expose an audio stream index.");
            Assert.True(mediaInfo.AudioSampleRate > 0, sampleName + " did not report a valid audio sample rate.");
            Assert.True(mediaInfo.AudioChannelCount > 0, sampleName + " did not report a valid audio channel count.");
        }

        [Fact]
        public async Task ExportHost_ClipExport_ProducesTrimmedMp4()
        {
            var ffmpegToolPath = TryGetLocalFfmpegToolPath();
            if (string.IsNullOrWhiteSpace(ffmpegToolPath))
            {
                return;
            }

            using var workspace = new TemporaryWorkspace();
            var sourceFilePath = Path.Combine(workspace.DirectoryPath, "source.mp4");
            var outputFilePath = Path.Combine(workspace.DirectoryPath, "clip.mp4");

            await GenerateSyntheticSourceClipAsync(ffmpegToolPath, sourceFilePath);
            var sourceProbe = await ProbeAsync(sourceFilePath);
            var clipDuration = GetShortClipDuration(sourceProbe.Duration);

            var exportResult = await RunHostRequestAsync(
                new ExportHostRequest
                {
                    Operation = ExportHostClient.ClipExportOperation,
                    ClipExportPlan = new ClipExportPlan(
                        sourceFilePath,
                        outputFilePath,
                        "integration-clip-export",
                        "left",
                        true,
                        TimeSpan.Zero,
                        clipDuration,
                        0,
                        null,
                        "frame-exact-test",
                        PaneViewportSnapshot.CreateFullFrame(sourceProbe.PixelWidth, sourceProbe.PixelHeight),
                        string.Empty,
                        string.Empty,
                        string.Empty)
                });

            AssertHostSucceeded(exportResult);
            Assert.True(File.Exists(outputFilePath), "Clip export did not produce an output file.");

            var outputProbe = await ProbeAsync(outputFilePath);
            Assert.Equal(sourceProbe.PixelWidth, outputProbe.PixelWidth);
            Assert.Equal(sourceProbe.PixelHeight, outputProbe.PixelHeight);
            Assert.InRange(
                outputProbe.Duration.TotalMilliseconds,
                Math.Max(1d, clipDuration.TotalMilliseconds - 120d),
                clipDuration.TotalMilliseconds + 120d);
        }

        [Fact]
        public async Task ExportHost_CompareExport_ProducesSideBySideMp4()
        {
            var ffmpegToolPath = TryGetLocalFfmpegToolPath();
            if (string.IsNullOrWhiteSpace(ffmpegToolPath))
            {
                return;
            }

            using var workspace = new TemporaryWorkspace();
            var sourceFilePath = Path.Combine(workspace.DirectoryPath, "source.mp4");
            var audioFilePath = Path.Combine(workspace.DirectoryPath, "replacement.wav");
            var audioBearingSourceFilePath = Path.Combine(workspace.DirectoryPath, "with-audio.mp4");
            var outputFilePath = Path.Combine(workspace.DirectoryPath, "compare.mp4");

            await GenerateSyntheticSourceClipAsync(ffmpegToolPath, sourceFilePath);
            WriteSineWaveFile(audioFilePath, TimeSpan.FromMilliseconds(420));

            var sourceProbe = await ProbeAsync(sourceFilePath);
            var insertionResult = await RunHostRequestAsync(
                new ExportHostRequest
                {
                    Operation = ExportHostClient.AudioInsertionOperation,
                    AudioInsertionPlan = new AudioInsertionPlan(
                        sourceFilePath,
                        audioFilePath,
                        audioBearingSourceFilePath,
                        "integration-compare-audio-source",
                        sourceProbe.Duration,
                        string.Empty,
                        string.Empty,
                        string.Empty)
                });

            AssertHostSucceeded(insertionResult);

            var compareSourceProbe = await ProbeAsync(audioBearingSourceFilePath);
            var compareDuration = GetShortClipDuration(compareSourceProbe.Duration);

            var compareResult = await RunHostRequestAsync(
                new ExportHostRequest
                {
                    Operation = ExportHostClient.CompareExportOperation,
                    CompareSideBySideExportPlan = new CompareSideBySideExportPlan
                    {
                        OutputFilePath = outputFilePath,
                        Mode = CompareSideBySideExportMode.Loop,
                        AudioSource = CompareSideBySideExportAudioSource.Primary,
                        PrimarySourceFilePath = audioBearingSourceFilePath,
                        CompareSourceFilePath = audioBearingSourceFilePath,
                        PrimaryStartTime = TimeSpan.Zero,
                        PrimaryContentDuration = compareDuration,
                        PrimaryLeadingPad = TimeSpan.Zero,
                        PrimaryTrailingPad = TimeSpan.Zero,
                        CompareStartTime = TimeSpan.Zero,
                        CompareContentDuration = compareDuration,
                        CompareLeadingPad = TimeSpan.Zero,
                        CompareTrailingPad = TimeSpan.Zero,
                        PrimaryEndBoundaryStrategy = "integration-loop-primary",
                        CompareEndBoundaryStrategy = "integration-loop-compare",
                        OutputDuration = compareDuration,
                        PrimaryRenderWidth = compareSourceProbe.PixelWidth,
                        PrimaryRenderHeight = compareSourceProbe.PixelHeight,
                        CompareRenderWidth = compareSourceProbe.PixelWidth,
                        CompareRenderHeight = compareSourceProbe.PixelHeight,
                        OutputWidth = compareSourceProbe.PixelWidth * 2,
                        OutputHeight = compareSourceProbe.PixelHeight,
                        PrimaryViewportSnapshot = PaneViewportSnapshot.CreateFullFrame(compareSourceProbe.PixelWidth, compareSourceProbe.PixelHeight),
                        CompareViewportSnapshot = PaneViewportSnapshot.CreateFullFrame(compareSourceProbe.PixelWidth, compareSourceProbe.PixelHeight),
                        SelectedAudioHasStream = true,
                        FfmpegArguments = string.Empty,
                        FfmpegPath = string.Empty,
                        FfprobePath = string.Empty
                    }
                });

            AssertHostSucceeded(compareResult);
            Assert.True(File.Exists(outputFilePath), "Compare export did not produce an output file.");

            var outputProbe = await ProbeAsync(outputFilePath);
            Assert.Equal(compareSourceProbe.PixelWidth * 2, outputProbe.PixelWidth);
            Assert.Equal(compareSourceProbe.PixelHeight, outputProbe.PixelHeight);
            Assert.True(outputProbe.HasAudioStream, "Compare export output did not preserve the selected audio source.");
            Assert.InRange(
                outputProbe.Duration.TotalMilliseconds,
                Math.Max(1d, compareDuration.TotalMilliseconds - 120d),
                compareDuration.TotalMilliseconds + 120d);
        }

        [Fact]
        public async Task ExportHost_WholeCompareExport_WithLeadingPad_ProducesSideBySideMp4()
        {
            var ffmpegToolPath = TryGetLocalFfmpegToolPath();
            if (string.IsNullOrWhiteSpace(ffmpegToolPath))
            {
                return;
            }

            using var workspace = new TemporaryWorkspace();
            var sourceFilePath = Path.Combine(workspace.DirectoryPath, "source.mp4");
            var outputFilePath = Path.Combine(workspace.DirectoryPath, "compare-whole.mp4");

            await GenerateSyntheticSourceClipAsync(ffmpegToolPath, sourceFilePath);

            var sourceProbe = await ProbeAsync(sourceFilePath);
            var leadingPad = TimeSpan.FromMilliseconds(500d);
            var expectedDuration = sourceProbe.Duration + leadingPad;

            var compareResult = await RunHostRequestAsync(
                new ExportHostRequest
                {
                    Operation = ExportHostClient.CompareExportOperation,
                    CompareSideBySideExportPlan = new CompareSideBySideExportPlan
                    {
                        OutputFilePath = outputFilePath,
                        Mode = CompareSideBySideExportMode.WholeVideo,
                        AudioSource = CompareSideBySideExportAudioSource.Compare,
                        PrimarySourceFilePath = sourceFilePath,
                        CompareSourceFilePath = sourceFilePath,
                        PrimaryStartTime = TimeSpan.Zero,
                        PrimaryContentDuration = sourceProbe.Duration,
                        PrimaryLeadingPad = leadingPad,
                        PrimaryTrailingPad = TimeSpan.Zero,
                        CompareStartTime = TimeSpan.Zero,
                        CompareContentDuration = sourceProbe.Duration,
                        CompareLeadingPad = TimeSpan.Zero,
                        CompareTrailingPad = leadingPad,
                        PrimaryEndBoundaryStrategy = "integration-whole-primary",
                        CompareEndBoundaryStrategy = "integration-whole-compare",
                        OutputDuration = expectedDuration,
                        PrimaryRenderWidth = sourceProbe.PixelWidth,
                        PrimaryRenderHeight = sourceProbe.PixelHeight,
                        CompareRenderWidth = sourceProbe.PixelWidth,
                        CompareRenderHeight = sourceProbe.PixelHeight,
                        OutputWidth = sourceProbe.PixelWidth * 2,
                        OutputHeight = sourceProbe.PixelHeight,
                        PrimaryViewportSnapshot = PaneViewportSnapshot.CreateFullFrame(sourceProbe.PixelWidth, sourceProbe.PixelHeight),
                        CompareViewportSnapshot = PaneViewportSnapshot.CreateFullFrame(sourceProbe.PixelWidth, sourceProbe.PixelHeight),
                        SelectedAudioHasStream = false,
                        FfmpegArguments = string.Empty,
                        FfmpegPath = string.Empty,
                        FfprobePath = string.Empty
                    }
                });

            AssertHostSucceeded(compareResult);
            Assert.True(File.Exists(outputFilePath), "Whole-video compare export did not produce an output file.");

            var outputProbe = await ProbeAsync(outputFilePath);
            Assert.Equal(sourceProbe.PixelWidth * 2, outputProbe.PixelWidth);
            Assert.Equal(sourceProbe.PixelHeight, outputProbe.PixelHeight);
            Assert.False(outputProbe.HasAudioStream, "Whole-video compare export unexpectedly produced an audio stream.");
            Assert.InRange(
                outputProbe.Duration.TotalMilliseconds,
                Math.Max(1d, expectedDuration.TotalMilliseconds - 150d),
                expectedDuration.TotalMilliseconds + 150d);
        }

        private static async Task<VideoMediaInfo> ProbeAsync(string filePath)
        {
            var result = await RunHostRequestAsync(
                new ExportHostRequest
                {
                    Operation = ExportHostClient.ProbeOperation,
                    ProbeFilePath = filePath
                });

            AssertHostSucceeded(result);
            var response = result.Response;
            Assert.NotNull(response);
            Assert.NotNull(response!.MediaInfo);
            return response.MediaInfo;
        }

        private static async Task<HostInvocationResult> RunHostRequestAsync(ExportHostRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);

            using var workspace = new TemporaryWorkspace();
            var requestPath = Path.Combine(workspace.DirectoryPath, "request.json");
            var responsePath = Path.Combine(workspace.DirectoryPath, "response.json");
            var errorPath = Path.Combine(workspace.DirectoryPath, "response.error.txt");

            request.ResponseJsonPath = responsePath;
            request.ErrorPath = errorPath;

            File.WriteAllText(
                requestPath,
                JsonSerializer.Serialize(request, ExportHostClient.JsonOptions),
                new UTF8Encoding(false));

            var executablePath = Path.Combine(AppContext.BaseDirectory, "FramePlayer.exe");
            Assert.True(File.Exists(executablePath), "The built FramePlayer.exe host was not found in the test output.");

            var startInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                Arguments = "--run-export-request \"" + requestPath + "\"",
                WorkingDirectory = AppContext.BaseDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = startInfo };
            process.Start();

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();

            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(true);
                    }
                }
                catch
                {
                    // Best-effort timeout cleanup for the host process.
                }

                throw new TimeoutException("The export host process did not finish within the integration-test timeout.");
            }

            var standardOutput = await stdoutTask;
            var standardError = await stderrTask;
            var errorText = File.Exists(errorPath)
                ? File.ReadAllText(errorPath, Encoding.UTF8)
                : string.Empty;
            ExportHostResponse? response = File.Exists(responsePath)
                ? JsonSerializer.Deserialize<ExportHostResponse>(
                    File.ReadAllText(responsePath, Encoding.UTF8),
                    ExportHostClient.JsonOptions)
                : null;

            return new HostInvocationResult(
                process.ExitCode,
                standardOutput,
                standardError,
                errorText,
                response);
        }

        private static void AssertHostSucceeded(HostInvocationResult result)
        {
            var failureMessage = BuildFailureMessage(result);
            Assert.True(result != null, failureMessage);
            Assert.True(result.ExitCode == 0, failureMessage);
            Assert.True(string.IsNullOrWhiteSpace(result.ErrorText), failureMessage);
            var response = result.Response;
            Assert.True(response != null, failureMessage);
            Assert.True(string.IsNullOrWhiteSpace(response!.FailureMessage), failureMessage);

            if (response.ClipExportResult != null)
            {
                Assert.True(response.ClipExportResult.Succeeded, failureMessage);
            }

            if (response.AudioInsertionResult != null)
            {
                Assert.True(response.AudioInsertionResult.Succeeded, failureMessage);
            }

            if (response.CompareSideBySideExportResult != null)
            {
                Assert.True(response.CompareSideBySideExportResult.Succeeded, failureMessage);
            }
        }

        private static string BuildFailureMessage(HostInvocationResult result)
        {
            if (result == null)
            {
                return "The export host result was null.";
            }

            var builder = new StringBuilder();
            builder.Append("Export host failed.");
            builder.Append(" ExitCode=");
            builder.Append(result.ExitCode.ToString(CultureInfo.InvariantCulture));

            if (!string.IsNullOrWhiteSpace(result.ErrorText))
            {
                builder.Append(" ErrorFile=");
                builder.Append(result.ErrorText.Trim());
            }

            if (result.Response != null && !string.IsNullOrWhiteSpace(result.Response.FailureMessage))
            {
                builder.Append(" ResponseFailure=");
                builder.Append(result.Response.FailureMessage.Trim());
            }

            if (!string.IsNullOrWhiteSpace(result.StandardError))
            {
                builder.Append(" Stderr=");
                builder.Append(result.StandardError.Trim());
            }

            if (!string.IsNullOrWhiteSpace(result.StandardOutput))
            {
                builder.Append(" Stdout=");
                builder.Append(result.StandardOutput.Trim());
            }

            return builder.ToString();
        }

        private static async Task GenerateSyntheticSourceClipAsync(string ffmpegToolPath, string outputFilePath)
        {
            var arguments = string.Format(
                CultureInfo.InvariantCulture,
                "-y -filter_complex \"testsrc=size=128x72:rate=30,format=yuv420p[v]\" -map \"[v]\" -t 1.2 -an -c:v libx264 -movflags +faststart \"{0}\"",
                outputFilePath);

            var result = await RunProcessAsync(
                ffmpegToolPath,
                arguments,
                Path.GetDirectoryName(outputFilePath) ?? AppContext.BaseDirectory);

            Assert.True(
                result.ExitCode == 0,
                "Synthetic source clip generation failed. Stdout: " + result.StandardOutput + " Stderr: " + result.StandardError);
            Assert.True(File.Exists(outputFilePath), "Synthetic source clip generation did not produce the expected output file.");
        }

        private static async Task GenerateSyntheticAudioBearingSourceClipAsync(
            string ffmpegToolPath,
            string outputFilePath,
            string videoCodec)
        {
            var normalizedCodec = NormalizeCodecName(videoCodec);
            string arguments;
            if (string.Equals(normalizedCodec, "mjpeg", StringComparison.OrdinalIgnoreCase))
            {
                arguments = string.Format(
                    CultureInfo.InvariantCulture,
                    "-y -filter_complex \"testsrc=size=128x72:rate=30,format=yuvj420p[v];sine=frequency=660:sample_rate=48000,atrim=duration=1.2[a]\" -map \"[v]\" -map \"[a]\" -t 1.2 -c:v mjpeg -q:v 3 -c:a pcm_s16le \"{0}\"",
                    outputFilePath);
            }
            else if (string.Equals(normalizedCodec, "h264", StringComparison.OrdinalIgnoreCase))
            {
                arguments = string.Format(
                    CultureInfo.InvariantCulture,
                    "-y -filter_complex \"testsrc=size=128x72:rate=30,format=yuv420p[v];sine=frequency=440:sample_rate=48000,atrim=duration=1.2[a]\" -map \"[v]\" -map \"[a]\" -t 1.2 -c:v libx264 -c:a aac -b:a 128k -movflags +faststart \"{0}\"",
                    outputFilePath);
            }
            else
            {
                throw new ArgumentOutOfRangeException(nameof(videoCodec), "Unsupported synthetic test video codec: " + videoCodec);
            }

            var result = await RunProcessAsync(
                ffmpegToolPath,
                arguments,
                Path.GetDirectoryName(outputFilePath) ?? AppContext.BaseDirectory);

            Assert.True(
                result.ExitCode == 0,
                "Synthetic audio-bearing clip generation failed. Stdout: " + result.StandardOutput + " Stderr: " + result.StandardError);
            Assert.True(File.Exists(outputFilePath), "Synthetic audio-bearing clip generation did not produce the expected output file.");
        }

        private static string NormalizeCodecName(string codecName)
        {
            return string.IsNullOrWhiteSpace(codecName)
                ? string.Empty
                : codecName.Replace(".", string.Empty).Trim().ToLowerInvariant();
        }

        private static async Task<ProcessInvocationResult> RunProcessAsync(string fileName, string arguments, string workingDirectory)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = startInfo };
            process.Start();
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            return new ProcessInvocationResult(
                process.ExitCode,
                await stdoutTask,
                await stderrTask);
        }

        private static string TryGetLocalFfmpegToolPath()
        {
            var toolPath = Path.Combine(GetRepositoryRoot(), "Runtime", "ffmpeg-tools", "ffmpeg.exe");
            return File.Exists(toolPath) ? toolPath : string.Empty;
        }

        private static string GetRepositoryRoot()
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

            throw new InvalidOperationException("Could not locate the repository root from the test output directory.");
        }

        private static TimeSpan GetShortClipDuration(TimeSpan sourceDuration)
        {
            if (sourceDuration <= TimeSpan.Zero)
            {
                return TimeSpan.FromMilliseconds(250);
            }

            var duration = Math.Min(sourceDuration.TotalMilliseconds / 2d, 650d);
            return TimeSpan.FromMilliseconds(Math.Max(250d, duration));
        }

        private static void WriteSineWaveFile(string filePath, TimeSpan duration)
        {
            const int sampleRate = 48000;
            const short bitsPerSample = 16;
            const short channelCount = 1;
            const double toneFrequency = 440d;
            const short amplitude = 8192;

            var sampleCount = Math.Max(1, (int)Math.Round(duration.TotalSeconds * sampleRate, MidpointRounding.AwayFromZero));
            var blockAlign = (short)(channelCount * (bitsPerSample / 8));
            var byteRate = sampleRate * blockAlign;
            var dataLength = sampleCount * blockAlign;

            using var stream = File.Create(filePath);
            using var writer = new BinaryWriter(stream, Encoding.ASCII, false);
            writer.Write(Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(36 + dataLength);
            writer.Write(Encoding.ASCII.GetBytes("WAVE"));
            writer.Write(Encoding.ASCII.GetBytes("fmt "));
            writer.Write(16);
            writer.Write((short)1);
            writer.Write(channelCount);
            writer.Write(sampleRate);
            writer.Write(byteRate);
            writer.Write(blockAlign);
            writer.Write(bitsPerSample);
            writer.Write(Encoding.ASCII.GetBytes("data"));
            writer.Write(dataLength);

            for (var index = 0; index < sampleCount; index++)
            {
                var sample = (short)(Math.Sin(2d * Math.PI * toneFrequency * index / sampleRate) * amplitude);
                writer.Write(sample);
            }
        }

        private sealed class TemporaryWorkspace : IDisposable
        {
            public TemporaryWorkspace()
            {
                DirectoryPath = Path.Combine(
                    Path.GetTempPath(),
                    "frameplayer-export-host-tests",
                    Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(DirectoryPath);
            }

            public string DirectoryPath { get; }

            public void Dispose()
            {
                try
                {
                    if (Directory.Exists(DirectoryPath))
                    {
                        Directory.Delete(DirectoryPath, true);
                    }
                }
                catch
                {
                    // Best-effort test workspace cleanup should never hide the actual assertion failure.
                }
            }
        }

        private sealed class HostInvocationResult
        {
            public HostInvocationResult(
                int exitCode,
                string standardOutput,
                string standardError,
                string errorText,
                ExportHostResponse? response)
            {
                ExitCode = exitCode;
                StandardOutput = standardOutput ?? string.Empty;
                StandardError = standardError ?? string.Empty;
                ErrorText = errorText ?? string.Empty;
                Response = response;
            }

            public int ExitCode { get; }

            public string StandardOutput { get; }

            public string StandardError { get; }

            public string ErrorText { get; }

            public ExportHostResponse? Response { get; }
        }

        private sealed class ProcessInvocationResult
        {
            public ProcessInvocationResult(int exitCode, string standardOutput, string standardError)
            {
                ExitCode = exitCode;
                StandardOutput = standardOutput ?? string.Empty;
                StandardError = standardError ?? string.Empty;
            }

            public int ExitCode { get; }

            public string StandardOutput { get; }

            public string StandardError { get; }
        }
    }
}
