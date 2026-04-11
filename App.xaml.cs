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

        public static bool HasBundledFfmpegRuntime
        {
            get { return !string.IsNullOrWhiteSpace(RuntimeDirectory); }
        }

        protected override async void OnStartup(StartupEventArgs e)
        {
            string regressionSuiteRequestPath;
            if (RegressionSuiteCli.TryGetRequestPath(e.Args, out regressionSuiteRequestPath))
            {
                var startupLogPath = Path.Combine(Path.GetTempPath(), "frameplayer-regression-startup.log");
                ShutdownMode = ShutdownMode.OnExplicitShutdown;
                AppendRegressionStartupLog(startupLogPath, "Regression startup requested.");
                AppendRegressionStartupLog(startupLogPath, "Request path: " + regressionSuiteRequestPath);
                ConfigureBundledRuntime();
                AppendRegressionStartupLog(startupLogPath, "Runtime configured. Root=" + (RuntimeDirectory ?? string.Empty));

                try
                {
                    AppendRegressionStartupLog(startupLogPath, "RegressionSuiteCli.RunAsync starting.");
                    var exitCode = await RegressionSuiteCli.RunAsync(regressionSuiteRequestPath, default(System.Threading.CancellationToken));
                    AppendRegressionStartupLog(startupLogPath, "RegressionSuiteCli.RunAsync completed with exit code " + exitCode.ToString());
                    Shutdown(exitCode);
                }
                catch (Exception ex)
                {
                    AppendRegressionStartupLog(startupLogPath, "RegressionSuiteCli.RunAsync failed: " + ex);
                    RegressionSuiteCli.TryWriteFailure(regressionSuiteRequestPath, ex);
                    System.Diagnostics.Trace.WriteLine("Regression suite execution failed: " + ex);
                    Shutdown(1);
                }

                return;
            }

            base.OnStartup(e);
            ConfigureBundledRuntime();
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

        private static void AppendRegressionStartupLog(string logPath, string message)
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
    }
}
