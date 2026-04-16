using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using FramePlayer.Core.Abstractions;

namespace FramePlayer.Host.Avalonia
{
    internal sealed class FileBackedRecentFilesCatalog : IRecentFilesCatalog
    {
        private const int MaxRecentFiles = 10;
        private readonly string _storagePath;

        public FileBackedRecentFilesCatalog()
        {
            var appDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FramePlayer");

            Directory.CreateDirectory(appDirectory);
            _storagePath = Path.Combine(appDirectory, "recent-files-cross-platform.txt");
        }

        public IReadOnlyList<string> Load()
        {
            if (!File.Exists(_storagePath))
            {
                return Array.Empty<string>();
            }

            try
            {
                return File.ReadAllLines(_storagePath, Encoding.UTF8)
                    .Select(NormalizePath)
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(MaxRecentFiles)
                    .ToArray();
            }
            catch
            {
                return Array.Empty<string>();
            }
        }

        public void Add(string filePath)
        {
            var normalizedPath = NormalizePath(filePath);
            if (string.IsNullOrWhiteSpace(normalizedPath))
            {
                return;
            }

            var entries = Load()
                .Where(path => !string.Equals(path, normalizedPath, StringComparison.OrdinalIgnoreCase))
                .Where(File.Exists)
                .ToList();

            entries.Insert(0, normalizedPath);
            Save(entries);
        }

        public void Remove(string filePath)
        {
            var normalizedPath = NormalizePath(filePath);
            if (string.IsNullOrWhiteSpace(normalizedPath))
            {
                return;
            }

            var entries = Load()
                .Where(path => !string.Equals(path, normalizedPath, StringComparison.OrdinalIgnoreCase))
                .ToList();

            Save(entries);
        }

        public void Clear()
        {
            Save(Array.Empty<string>());
        }

        private void Save(IEnumerable<string> entries)
        {
            var normalizedEntries = entries
                .Select(NormalizePath)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(MaxRecentFiles)
                .ToArray();

            if (normalizedEntries.Length == 0)
            {
                if (File.Exists(_storagePath))
                {
                    File.Delete(_storagePath);
                }

                return;
            }

            File.WriteAllLines(_storagePath, normalizedEntries, Encoding.UTF8);
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
