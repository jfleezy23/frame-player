using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FramePlayer.Core.Models;

namespace FramePlayer.Services
{
    internal sealed class ExportHostClient
    {
        internal const string ProbeOperation = "probe";
        internal const string AudioInsertionOperation = "audio-insertion";
        internal const string ClipExportOperation = "clip-export";
        internal const string CompareExportOperation = "compare-export";
        internal const string ExportRuntimeFolderName = "ffmpeg-export";
        private static readonly Lazy<RuntimeAvailability> CachedRuntimeAvailability =
            new Lazy<RuntimeAvailability>(DiscoverRuntimeAvailability);

        internal static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        public bool IsBundledRuntimeAvailable
        {
            get
            {
                return CachedRuntimeAvailability.Value.IsAvailable;
            }
        }

        public string GetRuntimeAvailabilityMessage()
        {
            return CachedRuntimeAvailability.Value.Message;
        }

        public async Task<VideoMediaInfo> ProbeAsync(string filePath, CancellationToken cancellationToken = default(CancellationToken))
        {
            var response = await ExecuteAsync(
                new ExportHostRequest
                {
                    Operation = ProbeOperation,
                    ProbeFilePath = filePath ?? string.Empty
                },
                cancellationToken).ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(response.FailureMessage))
            {
                throw new InvalidOperationException(response.FailureMessage);
            }

            return response.MediaInfo ?? VideoMediaInfo.Empty;
        }

        public async Task<AudioInsertionResult> InsertAudioAsync(
            AudioInsertionPlan plan,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var response = await ExecuteAsync(
                new ExportHostRequest
                {
                    Operation = AudioInsertionOperation,
                    AudioInsertionPlan = plan
                },
                cancellationToken).ConfigureAwait(false);

            if (response.AudioInsertionResult == null)
            {
                throw new InvalidOperationException(BuildHostFailureMessage(response, "Audio insertion host response was incomplete."));
            }

            return response.AudioInsertionResult;
        }

        public async Task<ClipExportResult> ExportClipAsync(
            ClipExportPlan plan,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var response = await ExecuteAsync(
                new ExportHostRequest
                {
                    Operation = ClipExportOperation,
                    ClipExportPlan = plan
                },
                cancellationToken).ConfigureAwait(false);

            if (response.ClipExportResult == null)
            {
                throw new InvalidOperationException(BuildHostFailureMessage(response, "Clip export host response was incomplete."));
            }

            return response.ClipExportResult;
        }

        public async Task<CompareSideBySideExportResult> ExportCompareAsync(
            CompareSideBySideExportPlan plan,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var response = await ExecuteAsync(
                new ExportHostRequest
                {
                    Operation = CompareExportOperation,
                    CompareSideBySideExportPlan = plan
                },
                cancellationToken).ConfigureAwait(false);

            if (response.CompareSideBySideExportResult == null)
            {
                throw new InvalidOperationException(BuildHostFailureMessage(response, "Compare export host response was incomplete."));
            }

            return response.CompareSideBySideExportResult;
        }

        private async Task<ExportHostResponse> ExecuteAsync(ExportHostRequest request, CancellationToken cancellationToken)
        {
            var runtimeAvailability = CachedRuntimeAvailability.Value;
            if (!runtimeAvailability.IsAvailable)
            {
                throw new InvalidOperationException(runtimeAvailability.Message);
            }

            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            var executablePath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
            {
                throw new InvalidOperationException("The current Frame Player executable path could not be resolved for export hosting.");
            }

            var workingDirectory = AppDomain.CurrentDomain.BaseDirectory;
            if (string.IsNullOrWhiteSpace(workingDirectory) || !Directory.Exists(workingDirectory))
            {
                throw new DirectoryNotFoundException("The application base directory required for export hosting could not be found.");
            }

            var temporaryDirectory = Path.Combine(Path.GetTempPath(), "frameplayer-export-host", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temporaryDirectory);

            var requestPath = Path.Combine(temporaryDirectory, "request.json");
            var responsePath = Path.Combine(temporaryDirectory, "response.json");
            var errorPath = Path.Combine(temporaryDirectory, "response.error.txt");

            request.ResponseJsonPath = responsePath;
            request.ErrorPath = errorPath;

            try
            {
                var requestJson = JsonSerializer.Serialize(request, JsonOptions);
                File.WriteAllText(requestPath, requestJson, new UTF8Encoding(false));

                var startInfo = new ProcessStartInfo
                {
                    FileName = executablePath,
                    Arguments = "--run-export-request \"" + requestPath + "\"",
                    WorkingDirectory = workingDirectory,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                using (var process = new Process { StartInfo = startInfo })
                {
                    process.Start();
                    var standardOutputTask = process.StandardOutput.ReadToEndAsync();
                    var standardErrorTask = process.StandardError.ReadToEndAsync();
                    await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
                    var standardOutput = await standardOutputTask.ConfigureAwait(false);
                    var standardError = await standardErrorTask.ConfigureAwait(false);

                    if (!File.Exists(responsePath))
                    {
                        var hostFailure = BuildMissingResponseMessage(process.ExitCode, errorPath, standardOutput, standardError);
                        throw new InvalidOperationException(hostFailure);
                    }

                    var responseJson = await File.ReadAllTextAsync(responsePath, cancellationToken).ConfigureAwait(false);
                    var response = JsonSerializer.Deserialize<ExportHostResponse>(responseJson, JsonOptions) ?? new ExportHostResponse();
                    if (process.ExitCode != 0 && string.IsNullOrWhiteSpace(response.FailureMessage))
                    {
                        response.FailureMessage = BuildMissingResponseMessage(process.ExitCode, errorPath, standardOutput, standardError);
                    }

                    return response;
                }
            }
            finally
            {
                try
                {
                    if (Directory.Exists(temporaryDirectory))
                    {
                        Directory.Delete(temporaryDirectory, true);
                    }
                }
                catch
                {
                    // Best-effort temporary export-host cleanup should never hide the primary result.
                }
            }
        }

        private static RuntimeAvailability DiscoverRuntimeAvailability()
        {
            var runtimeDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ExportRuntimeFolderName);
            if (!ExportRuntimeManifestService.TryValidateRuntimeDirectory(runtimeDirectory, out var message))
            {
                return new RuntimeAvailability(
                    false,
                    string.IsNullOrWhiteSpace(message)
                        ? "The bundled FFmpeg export runtime is unavailable."
                        : message);
            }

            return new RuntimeAvailability(
                true,
                "Bundled FFmpeg export runtime is ready.");
        }

        private static string BuildHostFailureMessage(ExportHostResponse response, string fallbackMessage)
        {
            if (response != null && !string.IsNullOrWhiteSpace(response.FailureMessage))
            {
                return response.FailureMessage;
            }

            return fallbackMessage;
        }

        private static string BuildMissingResponseMessage(
            int exitCode,
            string errorPath,
            string standardOutput,
            string standardError)
        {
            if (!string.IsNullOrWhiteSpace(errorPath) && File.Exists(errorPath))
            {
                var errorText = File.ReadAllText(errorPath, Encoding.UTF8).Trim();
                if (!string.IsNullOrWhiteSpace(errorText))
                {
                    return errorText;
                }
            }

            var details = !string.IsNullOrWhiteSpace(standardError)
                ? standardError.Trim()
                : (standardOutput ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(details))
            {
                return "Export host failed with exit code " + exitCode.ToString() + ". " + details;
            }

            return "Export host failed with exit code " + exitCode.ToString() + " before it could write a response.";
        }

        private sealed class RuntimeAvailability
        {
            public RuntimeAvailability(bool isAvailable, string message)
            {
                IsAvailable = isAvailable;
                Message = message ?? string.Empty;
            }

            public bool IsAvailable { get; }

            public string Message { get; }
        }
    }
}
