using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace FramePlayer.Avalonia.Services
{
    internal sealed class DiagnosticLogService
    {
        private readonly List<string> _entries = new List<string>();
        private readonly object _lock = new object();
        private readonly string _latestLogPath;

        public DiagnosticLogService()
        {
            SessionStarted = DateTime.Now;
            _latestLogPath = ResolveLatestLogPath();

            try
            {
                PersistEntries();
            }
            catch (Exception ex)
            {
                // Keep diagnostics best-effort. Export still works from the in-memory buffer.
                Trace.TraceWarning("Diagnostic log initialization failed: " + ex.Message);
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

            lock (_lock)
            {
                _entries.Add(entry);

                try
                {
                    PersistEntriesLocked();
                }
                catch (Exception ex)
                {
                    // Keep diagnostics best-effort.
                    Trace.TraceWarning("Diagnostic log write failed: " + ex.Message);
                }
            }
        }

        private static string ResolveLatestLogPath()
        {
            try
            {
                var logRoot = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (string.IsNullOrWhiteSpace(logRoot))
                {
                    logRoot = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                }

                if (string.IsNullOrWhiteSpace(logRoot))
                {
                    logRoot = AppContext.BaseDirectory;
                }

                var logDirectory = Path.Combine(logRoot, "FramePlayer", "Logs");
                Directory.CreateDirectory(logDirectory);
                return Path.Combine(logDirectory, "latest-session.log");
            }
            catch (Exception ex)
            {
                Trace.TraceWarning("Diagnostic log directory unavailable: " + ex.Message);
                return Path.Combine(AppContext.BaseDirectory, "FramePlayer-latest-session.log");
            }
        }

        private List<string> Snapshot()
        {
            lock (_lock)
            {
                return _entries.ToList();
            }
        }

        private void PersistEntries()
        {
            lock (_lock)
            {
                PersistEntriesLocked();
            }
        }

        private void PersistEntriesLocked()
        {
            var serializedEntries = string.Join(Environment.NewLine, _entries);
            File.WriteAllText(_latestLogPath, serializedEntries, new UTF8Encoding(false));
        }
    }
}
