using System;
using System.IO;
using System.Windows;
using FFmpeg.AutoGen;
using FramePlayer.Diagnostics;
using FramePlayer.Engines.FFmpeg;
using FramePlayer.Services;

namespace FramePlayer
{
    public partial class App : Application
    {
        public static string RuntimeDirectory { get; private set; }
        public static string RuntimeValidationMessage { get; private set; }
        public static string StartupOpenFilePath { get; private set; }

        public static bool HasBundledFfmpegRuntime
        {
            get { return !string.IsNullOrWhiteSpace(RuntimeDirectory); }
        }

        public static string ConsumeStartupOpenFilePath()
        {
            var path = StartupOpenFilePath;
            StartupOpenFilePath = string.Empty;
            return path;
        }

        protected override async void OnStartup(StartupEventArgs e)
        {
            string regressionSuiteRequestPath;
            if (RegressionSuiteCli.TryGetRequestPath(e.Args, out regressionSuiteRequestPath))
            {
                var startupLogPath = Path.Combine(Path.GetTempPath(), "frameplayer-regression-startup.log");
                ShutdownMode = ShutdownMode.OnExplicitShutdown;
                AppendCliStartupLog(startupLogPath, "Regression startup requested.");
                AppendCliStartupLog(startupLogPath, "Request path: " + regressionSuiteRequestPath);
                ConfigureBundledRuntime();
                AppendCliStartupLog(startupLogPath, "Runtime configured. Root=" + (RuntimeDirectory ?? string.Empty));

                try
                {
                    AppendCliStartupLog(startupLogPath, "RegressionSuiteCli.RunAsync starting.");
                    var exitCode = await RegressionSuiteCli.RunAsync(regressionSuiteRequestPath, default(System.Threading.CancellationToken));
                    AppendCliStartupLog(startupLogPath, "RegressionSuiteCli.RunAsync completed with exit code " + exitCode.ToString());
                    Shutdown(exitCode);
                }
                catch (Exception ex)
                {
                    AppendCliStartupLog(startupLogPath, "RegressionSuiteCli.RunAsync failed: " + ex);
                    RegressionSuiteCli.TryWriteFailure(regressionSuiteRequestPath, ex);
                    System.Diagnostics.Trace.TraceError("Regression suite execution failed: " + ex);
                    Shutdown(1);
                }

                return;
            }

            string manualTestRequestPath;
            if (ReviewEngineManualTestCli.TryGetRequestPath(e.Args, out manualTestRequestPath))
            {
                var startupLogPath = Path.Combine(Path.GetTempPath(), "frameplayer-review-engine-manual-startup.log");
                ShutdownMode = ShutdownMode.OnExplicitShutdown;
                AppendCliStartupLog(startupLogPath, "Manual review-engine startup requested.");
                AppendCliStartupLog(startupLogPath, "Request path: " + manualTestRequestPath);
                ConfigureBundledRuntime();
                AppendCliStartupLog(startupLogPath, "Runtime configured. Root=" + (RuntimeDirectory ?? string.Empty));

                try
                {
                    AppendCliStartupLog(startupLogPath, "ReviewEngineManualTestCli.RunAsync starting.");
                    var exitCode = await ReviewEngineManualTestCli.RunAsync(manualTestRequestPath, default(System.Threading.CancellationToken));
                    AppendCliStartupLog(startupLogPath, "ReviewEngineManualTestCli.RunAsync completed with exit code " + exitCode.ToString());
                    Shutdown(exitCode);
                }
                catch (Exception ex)
                {
                    AppendCliStartupLog(startupLogPath, "ReviewEngineManualTestCli.RunAsync failed: " + ex);
                    ReviewEngineManualTestCli.TryWriteFailure(manualTestRequestPath, ex);
                    System.Diagnostics.Trace.TraceError("Review engine manual tests failed: " + ex);
                    Shutdown(1);
                }

                return;
            }

            base.OnStartup(e);
            ConfigureBundledRuntime();
            StartupOpenFilePath = ResolveStartupOpenFilePath(e.Args);
        }

        private static void ConfigureBundledRuntime()
        {
            var baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            string validationMessage;
            if (!RuntimeManifestService.TryValidateRuntimeDirectory(baseDirectory, out validationMessage))
            {
                RuntimeDirectory = string.Empty;
                RuntimeValidationMessage = string.IsNullOrWhiteSpace(validationMessage)
                    ? "The bundled FFmpeg runtime could not be validated."
                    : validationMessage;
                return;
            }

            RuntimeDirectory = baseDirectory;
            RuntimeValidationMessage = string.Empty;
            ffmpeg.RootPath = baseDirectory;
            StartGpuWarmupIfEnabled();
        }

        private static void StartGpuWarmupIfEnabled()
        {
            var optionsProvider = new FfmpegReviewEngineOptionsProvider(new AppPreferencesService());
            var options = optionsProvider.GetCurrent();
            if (options.GpuBackendPreference == GpuBackendPreference.Disabled)
            {
                return;
            }

            FfmpegHardwareDeviceCache.StartVulkanWarmup();
        }

        private static void AppendCliStartupLog(string logPath, string message)
        {
            try
            {
                File.AppendAllText(
                    logPath,
                    DateTimeOffset.Now.ToString("o") + " " + message + Environment.NewLine);
            }
            catch
            {
            }
        }

        private static string ResolveStartupOpenFilePath(string[] args)
        {
            if (args == null || args.Length == 0)
            {
                return string.Empty;
            }

            for (var index = 0; index < args.Length; index++)
            {
                var argument = args[index];
                if (string.IsNullOrWhiteSpace(argument))
                {
                    continue;
                }

                if (string.Equals(argument, "--open-file", StringComparison.OrdinalIgnoreCase) &&
                    index + 1 < args.Length &&
                    File.Exists(args[index + 1]))
                {
                    return args[index + 1];
                }

                if (!argument.StartsWith("-", StringComparison.Ordinal) && File.Exists(argument))
                {
                    return argument;
                }
            }

            return string.Empty;
        }
    }
}
