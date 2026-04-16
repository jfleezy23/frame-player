using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using FFmpeg.AutoGen;
using FramePlayer.Services;

namespace FramePlayer.Host.Avalonia
{
    public sealed class App : Application
    {
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            string validationMessage;
            if (RuntimeManifestService.TryValidateRuntimeDirectory(AppContext.BaseDirectory, out validationMessage))
            {
                RuntimeEnvironment.CurrentRuntimeDirectory = AppContext.BaseDirectory;
                ffmpeg.RootPath = AppContext.BaseDirectory;
            }
            else
            {
                RuntimeEnvironment.CurrentRuntimeDirectory = string.Empty;
            }

            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.MainWindow = new MainWindow();
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}
