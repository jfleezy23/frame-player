using System;
using System.IO;
using System.Runtime.Serialization.Json;

namespace FramePlayer.Diagnostics
{
    internal static class DecodedFrameBudgetCoordinatorProbeCli
    {
        private const string OutputArgument = "--run-decoded-frame-budget-probe";

        public static bool TryGetOutputPath(string[] args, out string outputPath)
        {
            outputPath = string.Empty;
            if (args == null || args.Length == 0)
            {
                return false;
            }

            for (var index = 0; index < args.Length; index++)
            {
                if (!string.Equals(args[index], OutputArgument, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                {
                    throw new ArgumentException("The decoded-frame budget probe output path is missing.");
                }

                outputPath = Path.GetFullPath(args[index + 1]);
                return true;
            }

            return false;
        }

        public static int Run(string outputPath)
        {
            if (string.IsNullOrWhiteSpace(outputPath))
            {
                throw new ArgumentException("A decoded-frame budget probe output path is required.", nameof(outputPath));
            }

            var report = DecodedFrameBudgetCoordinatorProbe.Run();
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");

            using (var stream = File.Create(outputPath))
            {
                var serializer = new DataContractJsonSerializer(
                    typeof(DecodedFrameBudgetCoordinatorProbeReport),
                    new DataContractJsonSerializerSettings
                    {
                        UseSimpleDictionaryFormat = true
                    });
                serializer.WriteObject(stream, report);
            }

            return report.FailCount > 0 ? 2 : 0;
        }
    }
}
