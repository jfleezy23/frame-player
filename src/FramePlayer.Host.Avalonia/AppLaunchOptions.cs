using System;
using System.IO;

namespace FramePlayer.Host.Avalonia
{
    internal static class AppLaunchOptions
    {
        private static string _startupOpenFilePath = string.Empty;

        public static void Initialize(string[] args)
        {
            _startupOpenFilePath = ResolveStartupOpenFilePath(args);
        }

        public static string ConsumeStartupOpenFilePath()
        {
            var filePath = _startupOpenFilePath;
            _startupOpenFilePath = string.Empty;
            return filePath;
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
