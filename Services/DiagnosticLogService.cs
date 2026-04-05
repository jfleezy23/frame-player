using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Rpcs3VideoPlayer.Services
{
    internal sealed class DiagnosticLogService
    {
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
                File.WriteAllText(_latestLogPath, string.Empty, Encoding.UTF8);
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

            builder.AppendLine("Event Log");
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
                File.AppendAllText(_latestLogPath, entry + Environment.NewLine, Encoding.UTF8);
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
    }
}
