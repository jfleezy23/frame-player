using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Rpcs3VideoPlayer.Services
{
    internal sealed class RecentFilesService
    {
        private const int MaxRecentFiles = 10;
        private readonly string _storagePath;

        public RecentFilesService()
        {
            var appDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FramePlayer");

            Directory.CreateDirectory(appDirectory);
            _storagePath = Path.Combine(appDirectory, "recent-files.txt");
        }

        public IReadOnlyList<string> Load()
        {
            if (!File.Exists(_storagePath))
            {
                return Array.Empty<string>();
            }

            return File.ReadAllLines(_storagePath)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(NormalizePath)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(MaxRecentFiles)
                .ToList();
        }

        public void Add(string filePath)
        {
            var normalizedPath = NormalizePath(filePath);
            if (string.IsNullOrWhiteSpace(normalizedPath))
            {
                return;
            }

            var items = Load()
                .Where(path => !string.Equals(path, normalizedPath, StringComparison.OrdinalIgnoreCase))
                .Where(File.Exists)
                .ToList();

            items.Insert(0, normalizedPath);
            Save(items.Take(MaxRecentFiles));
        }

        public void Remove(string filePath)
        {
            var normalizedPath = NormalizePath(filePath);
            if (string.IsNullOrWhiteSpace(normalizedPath))
            {
                return;
            }

            var items = Load()
                .Where(path => !string.Equals(path, normalizedPath, StringComparison.OrdinalIgnoreCase))
                .ToList();

            Save(items);
        }

        public void Clear()
        {
            Save(Array.Empty<string>());
        }

        private void Save(IEnumerable<string> entries)
        {
            var content = entries
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(MaxRecentFiles)
                .ToArray();

            File.WriteAllLines(_storagePath, content);
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
