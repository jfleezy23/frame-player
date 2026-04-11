using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace FramePlayer.Services
{
    internal sealed class AppPreferencesService
    {
        private readonly string _storagePath;

        public AppPreferencesService()
        {
            var appDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FramePlayer");

            Directory.CreateDirectory(appDirectory);
            _storagePath = Path.Combine(appDirectory, "preferences.txt");
        }

        public AppPreferences Load()
        {
            if (!File.Exists(_storagePath))
            {
                return AppPreferences.Default;
            }

            try
            {
                var entries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var rawLine in File.ReadAllLines(_storagePath, Encoding.UTF8))
                {
                    if (string.IsNullOrWhiteSpace(rawLine))
                    {
                        continue;
                    }

                    var separatorIndex = rawLine.IndexOf('=');
                    if (separatorIndex <= 0 || separatorIndex == rawLine.Length - 1)
                    {
                        continue;
                    }

                    var key = rawLine.Substring(0, separatorIndex).Trim();
                    var value = rawLine.Substring(separatorIndex + 1).Trim();
                    if (string.IsNullOrWhiteSpace(key))
                    {
                        continue;
                    }

                    entries[key] = value;
                }

                bool useGpuAcceleration;
                if (!entries.TryGetValue("useGpuAcceleration", out var useGpuAccelerationText) ||
                    !bool.TryParse(useGpuAccelerationText, out useGpuAcceleration))
                {
                    return AppPreferences.Default;
                }

                return new AppPreferences(useGpuAcceleration);
            }
            catch
            {
                return AppPreferences.Default;
            }
        }

        public void Save(AppPreferences preferences)
        {
            if (preferences == null)
            {
                throw new ArgumentNullException(nameof(preferences));
            }

            var lines = new[]
            {
                string.Format(
                    CultureInfo.InvariantCulture,
                    "useGpuAcceleration={0}",
                    preferences.UseGpuAcceleration ? "true" : "false")
            };

            File.WriteAllLines(_storagePath, lines, Encoding.UTF8);
        }
    }
}
