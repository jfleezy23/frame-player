using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FramePlayer.Core.Models;

namespace FramePlayer.Services
{
    internal sealed class AudioInsertionService
    {
        private readonly FfmpegCliTooling _tooling;

        public AudioInsertionService()
        {
            _tooling = new FfmpegCliTooling();
        }

        public bool IsBundledToolingAvailable
        {
            get { return _tooling.IsBundledToolingAvailable; }
        }

        public string GetToolAvailabilityMessage()
        {
            return _tooling.GetToolAvailabilityMessage();
        }

        public AudioInsertionPlan CreatePlan(AudioInsertionRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);

            var toolPaths = _tooling.GetRequiredToolPaths();

            if (string.IsNullOrWhiteSpace(request.SourceFilePath))
            {
                throw new InvalidOperationException("No reviewed source file is available for audio insertion.");
            }

            if (!File.Exists(request.SourceFilePath))
            {
                throw new FileNotFoundException("The reviewed source file could not be found.", request.SourceFilePath);
            }

            if (!string.Equals(Path.GetExtension(request.SourceFilePath), ".mp4", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Audio insertion requires an MP4 source file.");
            }

            if (string.IsNullOrWhiteSpace(request.ReplacementAudioFilePath))
            {
                throw new InvalidOperationException("A replacement audio file is required for audio insertion.");
            }

            if (!File.Exists(request.ReplacementAudioFilePath))
            {
                throw new FileNotFoundException("The replacement audio file could not be found.", request.ReplacementAudioFilePath);
            }

            var replacementAudioExtension = Path.GetExtension(request.ReplacementAudioFilePath);
            if (!string.Equals(replacementAudioExtension, ".wav", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(replacementAudioExtension, ".mp3", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Audio insertion only supports WAV and MP3 replacement audio.");
            }

            if (string.IsNullOrWhiteSpace(request.OutputFilePath))
            {
                throw new InvalidOperationException("A destination path is required for audio insertion.");
            }

            var sourceFullPath = Path.GetFullPath(request.SourceFilePath);
            var replacementAudioFullPath = Path.GetFullPath(request.ReplacementAudioFilePath);
            var outputFullPath = Path.GetFullPath(request.OutputFilePath);
            if (string.Equals(sourceFullPath, outputFullPath, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Audio insertion cannot overwrite the reviewed source file.");
            }

            if (!string.Equals(Path.GetExtension(outputFullPath), ".mp4", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Audio insertion output must be an MP4 file.");
            }

            var outputDirectory = Path.GetDirectoryName(outputFullPath);
            if (string.IsNullOrWhiteSpace(outputDirectory))
            {
                throw new InvalidOperationException("Audio insertion requires a valid destination folder.");
            }

            var sessionSnapshot = request.SessionSnapshot ?? ReviewSessionSnapshot.Empty;
            var mediaInfo = sessionSnapshot.MediaInfo ?? VideoMediaInfo.Empty;
            if (!IsH264Codec(mediaInfo.VideoCodecName))
            {
                throw new InvalidOperationException("Audio insertion requires a loaded H.264 MP4 source.");
            }

            var videoDuration = mediaInfo.Duration;
            if (videoDuration <= TimeSpan.Zero)
            {
                throw new InvalidOperationException("Audio insertion requires a known video duration.");
            }

            Directory.CreateDirectory(outputDirectory);

            return new AudioInsertionPlan(
                sourceFullPath,
                replacementAudioFullPath,
                outputFullPath,
                request.DisplayLabel,
                videoDuration,
                BuildFfmpegArguments(sourceFullPath, replacementAudioFullPath, outputFullPath, videoDuration),
                toolPaths.FfmpegPath,
                toolPaths.FfprobePath);
        }

        public async Task<AudioInsertionResult> InsertAsync(
            AudioInsertionRequest request,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var plan = CreatePlan(request);
            return await InsertPlanAsync(plan, cancellationToken).ConfigureAwait(false);
        }

        public static async Task<AudioInsertionResult> InsertPlanAsync(
            AudioInsertionPlan plan,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            ArgumentNullException.ThrowIfNull(plan);

            return await Task.Run(
                () =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                    var processResult = FfmpegCliTooling.RunProcess(
                        plan.FfmpegPath,
                        plan.FfmpegArguments,
                        Path.GetDirectoryName(plan.OutputFilePath));
                    stopwatch.Stop();

                    if (processResult.ExitCode != 0)
                    {
                        return new AudioInsertionResult(
                            false,
                            plan,
                            FfmpegCliTooling.BuildFailureMessage(processResult, "FFmpeg audio insertion failed."),
                            processResult.ExitCode,
                            stopwatch.Elapsed,
                            null,
                            null,
                            processResult.StandardOutput,
                            processResult.StandardError);
                    }

                    TimeSpan? probedDuration = null;
                    bool? probedHasAudioStream = null;
                    FfmpegMediaProbe probe;
                    if (FfmpegCliTooling.TryProbeMediaFile(plan.FfprobePath, plan.OutputFilePath, out probe) &&
                        probe != null)
                    {
                        probedDuration = probe.Duration;
                        probedHasAudioStream = probe.HasAudioStream;
                    }

                    return new AudioInsertionResult(
                        true,
                        plan,
                        "Audio insertion completed.",
                        0,
                        stopwatch.Elapsed,
                        probedDuration,
                        probedHasAudioStream,
                        processResult.StandardOutput,
                        processResult.StandardError);
                },
                cancellationToken).ConfigureAwait(false);
        }

        private static string BuildFfmpegArguments(
            string sourceFilePath,
            string replacementAudioFilePath,
            string outputFilePath,
            TimeSpan videoDuration)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "-v error -y -i \"{0}\" -i \"{1}\" -filter_complex \"[1:a]apad=whole_dur={2},atrim=duration={2}[aout]\" -map 0:v:0 -map \"[aout]\" -sn -dn -c:v copy -c:a aac -b:a 192k -movflags +faststart \"{3}\"",
                sourceFilePath,
                replacementAudioFilePath,
                FfmpegExportTiming.FormatFfmpegTime(videoDuration),
                outputFilePath);
        }

        private static bool IsH264Codec(string codecName)
        {
            if (string.IsNullOrWhiteSpace(codecName))
            {
                return false;
            }

            var normalizedCodec = codecName.Replace(".", string.Empty).Trim();
            return string.Equals(normalizedCodec, "h264", StringComparison.OrdinalIgnoreCase);
        }
    }
}
