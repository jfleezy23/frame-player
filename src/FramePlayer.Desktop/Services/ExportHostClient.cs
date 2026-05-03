using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FramePlayer.Core.Models;
using FramePlayer.Engines.FFmpeg;

namespace FramePlayer.Services
{
    /// <summary>
    /// Runs export, audio insertion, and export-side probe work in a hidden child Frame Player process.
    /// </summary>
    /// <remarks>
    /// This keeps heavy FFmpeg export work outside the interactive desktop process while avoiding any
    /// network IPC surface. Requests and responses move through per-operation temporary JSON files.
    /// </remarks>
    internal sealed class ExportHostClient
    {
        internal const string ProbeOperation = "probe";
        internal const string AudioInsertionOperation = "audio-insertion";
        internal const string ClipExportOperation = "clip-export";
        internal const string CompareExportOperation = "compare-export";
        internal const string ExportRuntimeFolderName = "ffmpeg-export";
        internal const string DesktopAppBaseDirectoryEnvironmentVariable = "FRAMEPLAYER_DESKTOP_APP_BASE_DIRECTORY";
        internal const string DesktopExportHostExecutableEnvironmentVariable = "FRAMEPLAYER_DESKTOP_EXPORT_HOST_EXECUTABLE";
        private const string DesktopExecutableName = "FramePlayer.Desktop";
        private static readonly Lazy<RuntimeAvailability> CachedRuntimeAvailability =
            new Lazy<RuntimeAvailability>(DiscoverRuntimeAvailability);

        internal static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        public static bool IsBundledRuntimeAvailable
        {
            get
            {
                return CachedRuntimeAvailability.Value.IsAvailable;
            }
        }

        public static string GetRuntimeAvailabilityMessage()
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

            var launchInfo = ResolveExportHostLaunchInfo();
            if (string.IsNullOrWhiteSpace(launchInfo.ExecutablePath) || !File.Exists(launchInfo.ExecutablePath))
            {
                throw new InvalidOperationException("The current Frame Player executable path could not be resolved for export hosting.");
            }

            if (string.IsNullOrWhiteSpace(launchInfo.WorkingDirectory) || !Directory.Exists(launchInfo.WorkingDirectory))
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
                await File.WriteAllTextAsync(requestPath, requestJson, new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);

                // The export host is the current executable in a headless mode, not a daemon.
                // UseShellExecute stays false so arguments and redirected output remain local to this process tree.
                var startInfo = new ProcessStartInfo
                {
                    FileName = launchInfo.ExecutablePath,
                    Arguments = "--run-export-request \"" + requestPath + "\"",
                    WorkingDirectory = launchInfo.WorkingDirectory,
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
                    try
                    {
                        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        await KillExportHostAsync(process).ConfigureAwait(false);
                        throw;
                    }

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
            var errors = new StringBuilder();
            foreach (var baseDirectory in ResolveRuntimeBaseDirectories(AppDomain.CurrentDomain.BaseDirectory))
            {
                foreach (var runtimeDirectory in ResolveRuntimeCandidateDirectories(baseDirectory))
                {
                    if (ExportRuntimeManifestService.TryValidateRuntimeDirectory(runtimeDirectory, out var message))
                    {
                        return new RuntimeAvailability(
                            true,
                            "Bundled FFmpeg export runtime is ready: " + runtimeDirectory);
                    }

                    if (!string.IsNullOrWhiteSpace(message))
                    {
                        if (errors.Length > 0)
                        {
                            errors.Append(' ');
                        }

                        errors.Append(message);
                    }
                }
            }

            return new RuntimeAvailability(
                false,
                errors.Length == 0
                    ? "The bundled FFmpeg export runtime is unavailable."
                    : errors.ToString());
        }

        internal static IEnumerable<string> ResolveRuntimeBaseDirectories(string baseDirectory)
        {
            var yieldedDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var configuredBaseDirectory = ResolveConfiguredAppBaseDirectory();
            if (!string.IsNullOrWhiteSpace(configuredBaseDirectory) && yieldedDirectories.Add(configuredBaseDirectory))
            {
                yield return configuredBaseDirectory;
            }

            if (!string.IsNullOrWhiteSpace(baseDirectory) && yieldedDirectories.Add(baseDirectory))
            {
                yield return baseDirectory;
            }
        }

        internal static IEnumerable<string> ResolveRuntimeCandidateDirectories(string baseDirectory)
        {
            yield return Path.Combine(baseDirectory, ExportRuntimeFolderName);

            var platformRuntimeDirectory = FfmpegRuntimeBootstrap.ResolveRuntimeDirectory(baseDirectory);
            if (!string.Equals(
                platformRuntimeDirectory,
                Path.Combine(baseDirectory, ExportRuntimeFolderName),
                StringComparison.OrdinalIgnoreCase))
            {
                yield return platformRuntimeDirectory;
            }
        }

        internal static ExportHostLaunchInfo ResolveExportHostLaunchInfo()
        {
            var explicitExecutablePath = Environment.GetEnvironmentVariable(DesktopExportHostExecutableEnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(explicitExecutablePath))
            {
                return new ExportHostLaunchInfo(
                    explicitExecutablePath,
                    Path.GetDirectoryName(explicitExecutablePath) ?? AppDomain.CurrentDomain.BaseDirectory);
            }

            var configuredBaseDirectory = ResolveConfiguredAppBaseDirectory();
            if (!string.IsNullOrWhiteSpace(configuredBaseDirectory))
            {
                return new ExportHostLaunchInfo(
                    Path.Combine(configuredBaseDirectory, ResolveDefaultExecutableName()),
                    configuredBaseDirectory);
            }

            var bundledExecutablePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ResolveDefaultExecutableName());
            if (File.Exists(bundledExecutablePath))
            {
                return new ExportHostLaunchInfo(
                    bundledExecutablePath,
                    AppDomain.CurrentDomain.BaseDirectory);
            }

            return new ExportHostLaunchInfo(
                Environment.ProcessPath ?? string.Empty,
                AppDomain.CurrentDomain.BaseDirectory);
        }

        private static string ResolveConfiguredAppBaseDirectory()
        {
            var appBaseDirectory = Environment.GetEnvironmentVariable(DesktopAppBaseDirectoryEnvironmentVariable);
            return string.IsNullOrWhiteSpace(appBaseDirectory)
                ? string.Empty
                : appBaseDirectory;
        }

        private static string ResolveDefaultExecutableName()
        {
            return RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? DesktopExecutableName + ".exe"
                : DesktopExecutableName;
        }

        private static async Task KillExportHostAsync(Process process)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // Cancellation cleanup is best-effort; the caller's cancellation remains the primary result.
            }

            try
            {
                await process.WaitForExitAsync().ConfigureAwait(false);
            }
            catch
            {
                // The process may have already exited between the kill request and the wait.
            }
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

        internal readonly struct ExportHostLaunchInfo
        {
            public ExportHostLaunchInfo(string executablePath, string workingDirectory)
            {
                ExecutablePath = executablePath ?? string.Empty;
                WorkingDirectory = workingDirectory ?? string.Empty;
            }

            public string ExecutablePath { get; }

            public string WorkingDirectory { get; }
        }
    }
}
