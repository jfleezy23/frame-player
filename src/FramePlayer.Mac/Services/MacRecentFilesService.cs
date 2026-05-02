using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FramePlayer.Mac.Services
{
    internal sealed class MacRecentFilesService
    {
        private const int MaxRecentFiles = 10;
        private readonly string _storagePath;

        public MacRecentFilesService()
        {
            var appSupport = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var appDirectory = Path.Combine(appSupport, "FramePlayer");
            Directory.CreateDirectory(appDirectory);
            _storagePath = Path.Combine(appDirectory, "recent-files.txt");
        }

        internal MacRecentFilesService(string storagePath)
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
                .Distinct(StringComparer.Ordinal)
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
                .Where(path => !string.Equals(path, normalized, StringComparison.Ordinal))
                .ToList();
            entries.Insert(0, normalized);
            File.WriteAllLines(_storagePath, entries.Take(MaxRecentFiles));
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
