using System;
using System.Windows;
using FFmpeg.AutoGen;
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

        protected override void OnStartup(StartupEventArgs e)
        {
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
        }
    }
}
