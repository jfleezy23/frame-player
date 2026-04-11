using System;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FramePlayer.Diagnostics
{
    internal static class DualPaneBudgetHarnessCli
    {
        private const string RequestArgument = "--run-dual-pane-budget-harness-request";

        public static bool TryGetRequestPath(string[] args, out string requestPath)
        {
            requestPath = string.Empty;
            if (args == null || args.Length == 0)
            {
                return false;
            }

            for (var i = 0; i < args.Length; i++)
            {
                if (!string.Equals(args[i], RequestArgument, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (i + 1 >= args.Length || string.IsNullOrWhiteSpace(args[i + 1]))
                {
                    throw new ArgumentException("The dual-pane budget harness request path is missing.");
                }

                requestPath = Path.GetFullPath(args[i + 1]);
                return true;
            }

            return false;
        }

        public static async Task<int> RunAsync(string requestPath, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(requestPath))
            {
                throw new ArgumentException("A dual-pane budget harness request path is required.", nameof(requestPath));
            }

            var request = ReadRequest(requestPath);
            if (request == null)
            {
                throw new InvalidOperationException("The dual-pane budget harness request could not be read.");
            }

            if (string.IsNullOrWhiteSpace(request.ReportJsonPath))
            {
                throw new InvalidOperationException("The dual-pane budget harness request did not include a report output path.");
            }

            var report = await DualPaneBudgetHarnessRunner.RunAsync(
                    request.Pairs ?? Array.Empty<DualPaneBudgetHarnessPairRequest>(),
                    request.HostScenarios ?? Array.Empty<DualPaneBudgetHarnessHostScenarioRequest>(),
                    cancellationToken)
                .ConfigureAwait(true);

            Directory.CreateDirectory(Path.GetDirectoryName(request.ReportJsonPath) ?? ".");
            WriteReport(request.ReportJsonPath, report);
            return report.Summary != null && report.Summary.FailCount > 0 ? 2 : 0;
        }

        public static void TryWriteFailure(string requestPath, Exception exception)
        {
            if (string.IsNullOrWhiteSpace(requestPath) || exception == null)
            {
                return;
            }

            try
            {
                var request = ReadRequest(requestPath);
                var errorPath = request != null && !string.IsNullOrWhiteSpace(request.ErrorPath)
                    ? request.ErrorPath
                    : requestPath + ".error.txt";
                Directory.CreateDirectory(Path.GetDirectoryName(errorPath) ?? ".");
                File.WriteAllText(errorPath, exception.ToString());
            }
            catch
            {
            }
        }

        private static DualPaneBudgetHarnessRequest ReadRequest(string requestPath)
        {
            if (!File.Exists(requestPath))
            {
                throw new FileNotFoundException("The dual-pane budget harness request file was not found.", requestPath);
            }

            string json;
            using (var stream = File.OpenRead(requestPath))
            using (var reader = new StreamReader(stream, Encoding.UTF8, true))
            {
                json = reader.ReadToEnd();
            }

            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            using (var jsonStream = new MemoryStream(Encoding.UTF8.GetBytes(json.TrimStart('\uFEFF'))))
            {
                var serializer = new DataContractJsonSerializer(typeof(DualPaneBudgetHarnessRequest));
                return serializer.ReadObject(jsonStream) as DualPaneBudgetHarnessRequest;
            }
        }

        private static void WriteReport(string reportPath, DualPaneBudgetHarnessReport report)
        {
            using (var stream = File.Create(reportPath))
            {
                var serializer = new DataContractJsonSerializer(
                    typeof(DualPaneBudgetHarnessReport),
                    new DataContractJsonSerializerSettings
                    {
                        UseSimpleDictionaryFormat = true
                    });
                serializer.WriteObject(stream, report);
            }
        }
    }
}
