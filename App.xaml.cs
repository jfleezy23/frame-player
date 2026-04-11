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
            string dualPaneBudgetHarnessRequestPath;
            if (DualPaneBudgetHarnessCli.TryGetRequestPath(e.Args, out dualPaneBudgetHarnessRequestPath))
            {
                var startupLogPath = Path.Combine(Path.GetTempPath(), "frameplayer-dual-pane-budget-startup.log");
                ShutdownMode = ShutdownMode.OnExplicitShutdown;
                AppendRegressionStartupLog(startupLogPath, "Dual-pane budget harness startup requested.");
                AppendRegressionStartupLog(startupLogPath, "Request path: " + dualPaneBudgetHarnessRequestPath);
                ConfigureBundledRuntime();
                AppendRegressionStartupLog(startupLogPath, "Runtime configured. Root=" + (RuntimeDirectory ?? string.Empty));

                try
                {
                    AppendRegressionStartupLog(startupLogPath, "DualPaneBudgetHarnessCli.RunAsync starting.");
                    var exitCode = await DualPaneBudgetHarnessCli.RunAsync(dualPaneBudgetHarnessRequestPath, default(System.Threading.CancellationToken));
                    AppendRegressionStartupLog(startupLogPath, "DualPaneBudgetHarnessCli.RunAsync completed with exit code " + exitCode.ToString());
                    Shutdown(exitCode);
                }
                catch (Exception ex)
                {
                    AppendRegressionStartupLog(startupLogPath, "DualPaneBudgetHarnessCli.RunAsync failed: " + ex);
                    DualPaneBudgetHarnessCli.TryWriteFailure(dualPaneBudgetHarnessRequestPath, ex);
                    System.Diagnostics.Trace.WriteLine("Dual-pane budget harness execution failed: " + ex);
                    Shutdown(1);
                }

                return;
            }

            string decodedFrameBudgetProbeOutputPath;
            if (DecodedFrameBudgetCoordinatorProbeCli.TryGetOutputPath(e.Args, out decodedFrameBudgetProbeOutputPath))
            {
                var startupLogPath = Path.Combine(Path.GetTempPath(), "frameplayer-budget-probe-startup.log");
                ShutdownMode = ShutdownMode.OnExplicitShutdown;
                AppendRegressionStartupLog(startupLogPath, "Decoded-frame budget probe startup requested.");
                AppendRegressionStartupLog(startupLogPath, "Probe output path: " + decodedFrameBudgetProbeOutputPath);

                try
                {
                    AppendRegressionStartupLog(startupLogPath, "DecodedFrameBudgetCoordinatorProbeCli.Run starting.");
                    var exitCode = DecodedFrameBudgetCoordinatorProbeCli.Run(decodedFrameBudgetProbeOutputPath);
                    AppendRegressionStartupLog(startupLogPath, "DecodedFrameBudgetCoordinatorProbeCli.Run completed with exit code " + exitCode.ToString());
                    Shutdown(exitCode);
                }
                catch (Exception ex)
                {
                    AppendRegressionStartupLog(startupLogPath, "DecodedFrameBudgetCoordinatorProbeCli.Run failed: " + ex);
                    TryWriteProbeFailure(decodedFrameBudgetProbeOutputPath, ex);
                    System.Diagnostics.Trace.WriteLine("Decoded-frame budget probe execution failed: " + ex);
                    Shutdown(1);
                }

                return;
            }

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

        private static void TryWriteProbeFailure(string outputPath, Exception exception)
        {
            if (string.IsNullOrWhiteSpace(outputPath) || exception == null)
            {
                return;
            }

            try
            {
                File.WriteAllText(outputPath + ".error.txt", exception.ToString());
            }
            catch
            {
            }
        }
    }
}
