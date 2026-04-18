using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace FramePlayer.Services
{
    internal sealed class DiagnosticLogService
    {
        private const string StoragePrefix = "DPAPIv1:";
        private static readonly byte[] StorageEntropy = Encoding.UTF8.GetBytes("FramePlayer.DiagnosticLog");
        private readonly List<string> _entries = new List<string>();
        private readonly string _latestLogPath;

        public DiagnosticLogService()
        {
            var logDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FramePlayer",
                "Logs");

            Directory.CreateDirectory(logDirectory);
            _latestLogPath = Path.Combine(logDirectory, "latest-session.log");
            SessionStarted = DateTime.Now;

            try
            {
                PersistEntries();
            }
            catch
            {
                // Keep diagnostics best-effort. Export still works from the in-memory buffer.
            }
        }

        public DateTime SessionStarted { get; }

        public string LatestLogPath
        {
            get { return _latestLogPath; }
        }

        public void Info(string message)
        {
            Append("INFO", message);
        }

        public void Warn(string message)
        {
            Append("WARN", message);
        }

        public void Error(string message)
        {
            Append("ERROR", message);
        }

        public string BuildReport(IEnumerable<string> headerLines)
        {
            var builder = new StringBuilder();

            foreach (var line in headerLines.Where(line => !string.IsNullOrWhiteSpace(line)))
            {
                builder.AppendLine(line);
            }

            if (builder.Length > 0)
            {
                builder.AppendLine();
            }

            builder.AppendLine("Session Event Log");
            builder.AppendLine("---------");

            foreach (var entry in Snapshot())
            {
                builder.AppendLine(entry);
            }

            return builder.ToString();
        }

        private void Append(string level, string message)
        {
            var sanitizedMessage = (message ?? string.Empty)
                .Replace(Environment.NewLine, " ")
                .Trim();

            var entry = string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "{0:yyyy-MM-dd HH:mm:ss.fff zzz} [{1}] {2}",
                DateTime.Now,
                level,
                sanitizedMessage);

            _entries.Add(entry);

            try
            {
                PersistEntries();
            }
            catch
            {
                // Keep diagnostics best-effort.
            }
        }

        private IReadOnlyList<string> Snapshot()
        {
            return _entries.ToList();
        }

        private void PersistEntries()
        {
            var serializedEntries = string.Join(Environment.NewLine, _entries);
            var protectedBytes = ProtectedData.Protect(
                Encoding.UTF8.GetBytes(serializedEntries),
                StorageEntropy,
                DataProtectionScope.CurrentUser);

            File.WriteAllText(
                _latestLogPath,
                StoragePrefix + Convert.ToBase64String(protectedBytes),
                Encoding.UTF8);
        }
    }
}
