using System;
using System.IO;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using FFmpeg.AutoGen;
using FramePlayer.Diagnostics;
using FramePlayer.Engines.FFmpeg;
using FramePlayer.Services;

namespace FramePlayer.Desktop
{
    internal static class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            if (ExportHostCli.TryGetRequestPath(args, out var requestPath))
            {
                try
                {
                    ConfigureExportHostRuntime();
                    Environment.ExitCode = ExportHostCli.RunAsync(requestPath, default).GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    ExportHostCli.TryWriteFailure(requestPath, ex);
                    Console.Error.WriteLine(ex);
                    Environment.ExitCode = 2;
                }

                return;
            }

            BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args);
        }

        public static AppBuilder BuildAvaloniaApp()
        {
            return AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .WithInterFont()
                .LogToTrace();
        }

        private static void ConfigureExportHostRuntime()
        {
            var exportRuntimeDirectory = Path.Combine(AppContext.BaseDirectory, ExportHostClient.ExportRuntimeFolderName);
            if (!ExportRuntimeManifestService.TryValidateRuntimeDirectory(exportRuntimeDirectory, out var validationMessage))
            {
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(validationMessage)
                    ? "The bundled FFmpeg export runtime could not be validated."
                    : validationMessage);
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && !SetDllDirectory(exportRuntimeDirectory))
            {
                throw new InvalidOperationException("The bundled FFmpeg export runtime directory could not be activated for native DLL loading. Win32 error: " + Marshal.GetLastWin32Error().ToString());
            }

            ffmpeg.RootPath = exportRuntimeDirectory;
        }

        [DllImport("kernel32", SetLastError = true)]
        private static extern bool SetDllDirectory(string lpPathName);
    }

    public sealed class App : Application
    {
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.MainWindow = new Views.MainWindow();
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}
