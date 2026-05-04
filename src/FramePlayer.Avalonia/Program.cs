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

namespace FramePlayer.Avalonia
{
    internal static partial class Program
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
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                ConfigureSharedLibraryExportHostRuntime();
                return;
            }

            var exportRuntimeDirectory = Path.Combine(AppContext.BaseDirectory, ExportHostClient.ExportRuntimeFolderName);
            ValidateExportRuntimeDirectory(exportRuntimeDirectory);

            if (!SetDllDirectory(exportRuntimeDirectory))
            {
                throw new InvalidOperationException("The bundled FFmpeg export runtime directory could not be activated for native DLL loading. Win32 error: " + Marshal.GetLastWin32Error().ToString());
            }

            ffmpeg.RootPath = exportRuntimeDirectory;
        }

        private static void ConfigureSharedLibraryExportHostRuntime()
        {
            var runtimeBaseDirectory = ResolveRuntimeBaseDirectory();
            var exportRuntimeDirectory = FfmpegRuntimeBootstrap.ResolveRuntimeDirectory(runtimeBaseDirectory);
            ValidateExportRuntimeDirectory(exportRuntimeDirectory);
            FfmpegRuntimeBootstrap.ConfigureForCurrentPlatform(runtimeBaseDirectory);
        }

        private static void ValidateExportRuntimeDirectory(string exportRuntimeDirectory)
        {
            if (!ExportRuntimeManifestService.TryValidateRuntimeDirectory(exportRuntimeDirectory, out var validationMessage))
            {
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(validationMessage)
                    ? "The bundled FFmpeg export runtime could not be validated."
                    : validationMessage);
            }
        }

        private static string ResolveRuntimeBaseDirectory()
        {
            var runtimeBase = Environment.GetEnvironmentVariable(ExportHostClient.AppRuntimeBaseEnvironmentVariable);
            return string.IsNullOrWhiteSpace(runtimeBase)
                ? AppContext.BaseDirectory
                : runtimeBase;
        }

        [LibraryImport("kernel32", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool SetDllDirectory(string lpPathName);
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
