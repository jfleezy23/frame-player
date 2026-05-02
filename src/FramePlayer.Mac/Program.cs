using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using FramePlayer.Diagnostics;

namespace FramePlayer.Mac
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
