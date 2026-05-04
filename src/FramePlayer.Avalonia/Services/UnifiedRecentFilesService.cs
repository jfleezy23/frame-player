using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace FramePlayer.Avalonia.Services
{
    internal sealed class UnifiedRecentFilesService
    {
        private const int MaxRecentFiles = 10;
        private readonly string _storagePath;

        public UnifiedRecentFilesService()
        {
            var appSupport = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var appDirectory = Path.Combine(appSupport, "FramePlayer.AvaloniaPreview");
            Directory.CreateDirectory(appDirectory);
            _storagePath = Path.Combine(appDirectory, "recent-files.txt");
        }

        internal UnifiedRecentFilesService(string storagePath)
        {
            _storagePath = storagePath;
            var directory = Path.GetDirectoryName(_storagePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }

        public IReadOnlyList<string> Load()
        {
            if (!File.Exists(_storagePath))
            {
                return Array.Empty<string>();
            }

            return File.ReadAllLines(_storagePath)
                .Select(NormalizePath)
                .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
                .Distinct(RecentFilePathComparer)
                .Take(MaxRecentFiles)
                .ToArray();
        }

        public void Add(string filePath)
        {
            var normalized = NormalizePath(filePath);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return;
            }

            var entries = Load()
                .Where(path => !RecentFilePathComparer.Equals(path, normalized))
                .ToList();
            entries.Insert(0, normalized);
            File.WriteAllLines(_storagePath, entries.Take(MaxRecentFiles));
        }

        private static StringComparer RecentFilePathComparer
        {
            get
            {
                return RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                    ? StringComparer.OrdinalIgnoreCase
                    : StringComparer.Ordinal;
            }
        }

        private static string NormalizePath(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return string.Empty;
            }

            try
            {
                return Path.GetFullPath(filePath.Trim());
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
