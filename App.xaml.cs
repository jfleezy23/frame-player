using System;
using System.IO;
using System.Linq;
using System.Windows;
using Unosquare.FFME;

namespace Rpcs3VideoPlayer
{
    public partial class App : Application
    {
        public static string RuntimeDirectory { get; private set; }

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
            if (!IsValidRuntimeDirectory(baseDirectory))
            {
                RuntimeDirectory = string.Empty;
                return;
            }

            RuntimeDirectory = baseDirectory;
            Library.FFmpegDirectory = baseDirectory;
        }

        private static bool IsValidRuntimeDirectory(string candidatePath)
        {
            if (string.IsNullOrWhiteSpace(candidatePath) || !Directory.Exists(candidatePath))
            {
                return false;
            }

            return Directory.EnumerateFiles(candidatePath, "avcodec*.dll").Any()
                && Directory.EnumerateFiles(candidatePath, "avformat*.dll").Any()
                && Directory.EnumerateFiles(candidatePath, "avutil*.dll").Any()
                && Directory.EnumerateFiles(candidatePath, "swresample*.dll").Any()
                && Directory.EnumerateFiles(candidatePath, "swscale*.dll").Any();
        }
    }
}
