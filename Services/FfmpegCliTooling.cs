using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace FramePlayer.Services
{
    internal sealed class FfmpegCliTooling
    {
        private const string ToolsFolderName = "ffmpeg-tools";
        private readonly Lazy<ToolAvailability> _toolAvailability;

        public FfmpegCliTooling()
        {
            _toolAvailability = new Lazy<ToolAvailability>(DiscoverToolAvailability);
        }

        public bool IsBundledToolingAvailable
        {
            get { return _toolAvailability.Value.IsAvailable; }
        }

        public string GetToolAvailabilityMessage()
        {
            return _toolAvailability.Value.Message;
        }

        public FfmpegCliToolPaths GetRequiredToolPaths()
        {
            var availability = _toolAvailability.Value;
            if (!availability.IsAvailable)
            {
                throw new InvalidOperationException(availability.Message);
            }

            return availability.ToolPaths;
        }

        public static FfmpegProcessResult RunProcess(string filePath, string arguments, string workingDirectory)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = filePath,
                Arguments = arguments ?? string.Empty,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory)
                    ? AppDomain.CurrentDomain.BaseDirectory
                    : workingDirectory
            };

            using (var process = new Process { StartInfo = startInfo })
            {
                process.Start();
                var standardOutput = process.StandardOutput.ReadToEnd();
                var standardError = process.StandardError.ReadToEnd();
                process.WaitForExit();

                return new FfmpegProcessResult(process.ExitCode, standardOutput, standardError);
            }
        }

        public static string BuildFailureMessage(FfmpegProcessResult processResult, string defaultMessage)
        {
            ArgumentNullException.ThrowIfNull(processResult);

            var details = !string.IsNullOrWhiteSpace(processResult.StandardError)
                ? processResult.StandardError
                : processResult.StandardOutput;
            details = (details ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(details))
            {
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "{0} Exit code {1} (0x{2}).",
                    defaultMessage,
                    processResult.ExitCode,
                    unchecked((uint)processResult.ExitCode).ToString("X8", CultureInfo.InvariantCulture));
            }

            var condensed = details.Replace(Environment.NewLine, " ").Trim();
            return defaultMessage + " " + condensed;
        }

        public static bool TryProbeMediaFile(string ffprobePath, string filePath, out FfmpegMediaProbe probe)
        {
            probe = null;
            if (string.IsNullOrWhiteSpace(ffprobePath) ||
                string.IsNullOrWhiteSpace(filePath) ||
                !File.Exists(ffprobePath) ||
                !File.Exists(filePath))
            {
                return false;
            }

            var arguments = string.Format(
                CultureInfo.InvariantCulture,
                "-v error -print_format json -show_entries format=duration:stream=codec_type,width,height \"{0}\"",
                filePath);
            var processResult = RunProcess(ffprobePath, arguments, Path.GetDirectoryName(filePath));
            if (processResult.ExitCode != 0 || string.IsNullOrWhiteSpace(processResult.StandardOutput))
            {
                return false;
            }

            try
            {
                using (var document = JsonDocument.Parse(processResult.StandardOutput))
                {
                    var duration = ReadProbeDuration(document.RootElement);
                    ReadProbeStreams(document.RootElement, out var videoWidth, out var videoHeight, out var hasAudioStream);
                    probe = new FfmpegMediaProbe(duration, videoWidth, videoHeight, hasAudioStream);
                    return true;
                }
            }
            catch
            {
                probe = null;
                return false;
            }
        }

        private static TimeSpan? ReadProbeDuration(JsonElement rootElement)
        {
            if (!TryGetObjectProperty(rootElement, "format", out var formatElement))
            {
                return null;
            }

            JsonElement durationElement;
            if (!formatElement.TryGetProperty("duration", out durationElement) ||
                durationElement.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            if (!double.TryParse(durationElement.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedSeconds) ||
                parsedSeconds < 0d)
            {
                return null;
            }

            return TimeSpan.FromSeconds(parsedSeconds);
        }

        private static void ReadProbeStreams(
            JsonElement rootElement,
            out int? videoWidth,
            out int? videoHeight,
            out bool hasAudioStream)
        {
            videoWidth = null;
            videoHeight = null;
            hasAudioStream = false;

            if (!TryGetArrayProperty(rootElement, "streams", out var streamsElement))
            {
                return;
            }

            foreach (var streamElement in streamsElement.EnumerateArray())
            {
                if (!TryGetStringProperty(streamElement, "codec_type", out var codecType))
                {
                    continue;
                }

                if (string.Equals(codecType, "audio", StringComparison.OrdinalIgnoreCase))
                {
                    hasAudioStream = true;
                    continue;
                }

                if (!string.Equals(codecType, "video", StringComparison.OrdinalIgnoreCase) ||
                    videoWidth.HasValue ||
                    videoHeight.HasValue)
                {
                    continue;
                }

                videoWidth = ReadPositiveInt32Property(streamElement, "width");
                videoHeight = ReadPositiveInt32Property(streamElement, "height");
            }
        }

        private static bool TryGetObjectProperty(JsonElement element, string propertyName, out JsonElement propertyValue)
        {
            if (element.TryGetProperty(propertyName, out propertyValue) &&
                propertyValue.ValueKind == JsonValueKind.Object)
            {
                return true;
            }

            propertyValue = default(JsonElement);
            return false;
        }

        private static bool TryGetArrayProperty(JsonElement element, string propertyName, out JsonElement propertyValue)
        {
            if (element.TryGetProperty(propertyName, out propertyValue) &&
                propertyValue.ValueKind == JsonValueKind.Array)
            {
                return true;
            }

            propertyValue = default(JsonElement);
            return false;
        }

        private static bool TryGetStringProperty(JsonElement element, string propertyName, out string propertyValue)
        {
            propertyValue = string.Empty;

            JsonElement propertyElement;
            if (!element.TryGetProperty(propertyName, out propertyElement) ||
                propertyElement.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            propertyValue = propertyElement.GetString() ?? string.Empty;
            return true;
        }

        private static int? ReadPositiveInt32Property(JsonElement element, string propertyName)
        {
            JsonElement propertyElement;
            if (!element.TryGetProperty(propertyName, out propertyElement) ||
                propertyElement.ValueKind != JsonValueKind.Number ||
                !propertyElement.TryGetInt32(out var parsedValue) ||
                parsedValue <= 0)
            {
                return null;
            }

            return parsedValue;
        }

        private static ToolAvailability DiscoverToolAvailability()
        {
            var toolsDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ToolsFolderName);
            string errorMessage;
            if (!ExportToolsManifestService.TryValidateToolsDirectory(toolsDirectory, out errorMessage))
            {
                return new ToolAvailability(
                    false,
                    new FfmpegCliToolPaths(string.Empty, string.Empty),
                    string.IsNullOrWhiteSpace(errorMessage)
                        ? "The bundled FFmpeg export tools are unavailable."
                        : errorMessage);
            }

            var ffmpegPath = Path.Combine(toolsDirectory, "ffmpeg.exe");
            var ffprobePath = Path.Combine(toolsDirectory, "ffprobe.exe");
            if (!File.Exists(ffmpegPath) || !File.Exists(ffprobePath))
            {
                return new ToolAvailability(
                    false,
                    new FfmpegCliToolPaths(ffmpegPath, ffprobePath),
                    "The bundled FFmpeg export tools are incomplete.");
            }

            return new ToolAvailability(
                true,
                new FfmpegCliToolPaths(ffmpegPath, ffprobePath),
                "Bundled FFmpeg export tools are ready.");
        }

        private sealed class ToolAvailability
        {
            public ToolAvailability(bool isAvailable, FfmpegCliToolPaths toolPaths, string message)
            {
                IsAvailable = isAvailable;
                ToolPaths = toolPaths ?? new FfmpegCliToolPaths(string.Empty, string.Empty);
                Message = message ?? string.Empty;
            }

            public bool IsAvailable { get; }

            public FfmpegCliToolPaths ToolPaths { get; }

            public string Message { get; }
        }
    }

    internal sealed class FfmpegCliToolPaths
    {
        public FfmpegCliToolPaths(string ffmpegPath, string ffprobePath)
        {
            FfmpegPath = ffmpegPath ?? string.Empty;
            FfprobePath = ffprobePath ?? string.Empty;
        }

        public string FfmpegPath { get; }

        public string FfprobePath { get; }
    }

    internal sealed class FfmpegProcessResult
    {
        public FfmpegProcessResult(int exitCode, string standardOutput, string standardError)
        {
            ExitCode = exitCode;
            StandardOutput = standardOutput ?? string.Empty;
            StandardError = standardError ?? string.Empty;
        }

        public int ExitCode { get; }

        public string StandardOutput { get; }

        public string StandardError { get; }
    }

    internal sealed class FfmpegMediaProbe
    {
        public FfmpegMediaProbe(TimeSpan? duration, int? videoWidth, int? videoHeight, bool hasAudioStream)
        {
            Duration = duration;
            VideoWidth = videoWidth;
            VideoHeight = videoHeight;
            HasAudioStream = hasAudioStream;
        }

        public TimeSpan? Duration { get; }

        public int? VideoWidth { get; }

        public int? VideoHeight { get; }

        public bool HasAudioStream { get; }
    }
}
