using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FramePlayer.Core.Models;

namespace FramePlayer.Services
{
    internal sealed class AudioInsertionService
    {
        private readonly ExportHostClient _hostClient;

        public AudioInsertionService()
        {
            _hostClient = new ExportHostClient();
        }

        public static bool IsBundledRuntimeAvailable
        {
            get { return ExportHostClient.IsBundledRuntimeAvailable; }
        }

        public static string GetRuntimeAvailabilityMessage()
        {
            return ExportHostClient.GetRuntimeAvailabilityMessage();
        }

        public static AudioInsertionPlan CreatePlan(AudioInsertionRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);

            if (string.IsNullOrWhiteSpace(request.SourceFilePath))
            {
                throw new InvalidOperationException("No reviewed source file is available for audio insertion.");
            }

            if (!File.Exists(request.SourceFilePath))
            {
                throw new FileNotFoundException("The reviewed source file could not be found.", request.SourceFilePath);
            }

            if (!IsSupportedSourceExtension(Path.GetExtension(request.SourceFilePath)))
            {
                throw new InvalidOperationException("Audio insertion requires an MP4 or M4V source file.");
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
                string.Empty,
                string.Empty,
                string.Empty);
        }

        private static bool IsSupportedSourceExtension(string extension)
        {
            return string.Equals(extension, ".mp4", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(extension, ".m4v", StringComparison.OrdinalIgnoreCase);
        }

        public async Task<AudioInsertionResult> InsertAsync(
            AudioInsertionRequest request,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var plan = CreatePlan(request);
            return await _hostClient.InsertAudioAsync(plan, cancellationToken).ConfigureAwait(false);
        }

        public static async Task<AudioInsertionResult> InsertPlanAsync(
            AudioInsertionPlan plan,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            ArgumentNullException.ThrowIfNull(plan);
            return await new ExportHostClient().InsertAudioAsync(plan, cancellationToken).ConfigureAwait(false);
        }

    }
}
