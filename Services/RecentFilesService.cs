using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace FramePlayer.Services
{
    internal sealed class RecentFilesService
    {
        private const int MaxRecentFiles = 10;
        private const string StoragePrefix = "DPAPIv1:";
        private static readonly byte[] StorageEntropy = Encoding.UTF8.GetBytes("FramePlayer.RecentFiles");
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

            var storedContent = File.ReadAllText(_storagePath);
            if (string.IsNullOrWhiteSpace(storedContent))
            {
                return Array.Empty<string>();
            }

            var rawContent = TryDecrypt(storedContent);
            var items = rawContent
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(NormalizePath)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(MaxRecentFiles)
                .ToList();

            if (!storedContent.StartsWith(StoragePrefix, StringComparison.Ordinal))
            {
                Save(items);
            }

            return items;
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

            if (content.Length == 0)
            {
                File.Delete(_storagePath);
                return;
            }

            var serializedContent = string.Join(Environment.NewLine, content);
            var protectedBytes = ProtectedData.Protect(
                Encoding.UTF8.GetBytes(serializedContent),
                StorageEntropy,
                DataProtectionScope.CurrentUser);

            File.WriteAllText(
                _storagePath,
                StoragePrefix + Convert.ToBase64String(protectedBytes),
                Encoding.UTF8);
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

        private static string TryDecrypt(string storedContent)
        {
            if (!storedContent.StartsWith(StoragePrefix, StringComparison.Ordinal))
            {
                return storedContent;
            }

            try
            {
                var protectedBytes = Convert.FromBase64String(storedContent.Substring(StoragePrefix.Length).Trim());
                var plaintextBytes = ProtectedData.Unprotect(
                    protectedBytes,
                    StorageEntropy,
                    DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(plaintextBytes);
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
