using System;
using System.IO;
using System.Text;
using FramePlayer.Diagnostics;
using Xunit;

namespace FramePlayer.Core.Tests
{
    public sealed class ReviewEngineManualTestCliTests
    {
        [Fact]
        public void TryGetRequestPath_ReturnsNormalizedAbsolutePath()
        {
            var requestPath = Path.Combine("artifacts", "manual-tests", "request.json");

            var handled = ReviewEngineManualTestCli.TryGetRequestPath(
                new[] { "--run-review-engine-manual-tests-request", requestPath },
                out var resolvedPath);

            Assert.True(handled);
            Assert.Equal(Path.GetFullPath(requestPath), resolvedPath);
        }

        [Fact]
        public void TryGetRequestPath_Throws_WhenPathArgumentIsMissing()
        {
            var exception = Assert.Throws<ArgumentException>(
                () => ReviewEngineManualTestCli.TryGetRequestPath(
                    new[] { "--run-review-engine-manual-tests-request" },
                    out _));

            Assert.Contains("request path is missing", exception.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void TryWriteFailure_UsesFallbackErrorPath_WhenRequestDoesNotProvideOne()
        {
            var temporaryDirectory = CreateTemporaryDirectory();

            try
            {
                var requestPath = Path.Combine(temporaryDirectory, "request.json");
                var reportJsonPath = Path.Combine(temporaryDirectory, "report.json");
                var fallbackErrorPath = reportJsonPath + ".error.txt";

                File.WriteAllText(
                    requestPath,
                    "{\"filePaths\":[],\"reportJsonPath\":\"" + EscapeJson(reportJsonPath) + "\"}",
                    new UTF8Encoding(false));

                var exception = new InvalidOperationException("manual harness failed");

                ReviewEngineManualTestCli.TryWriteFailure(requestPath, exception);

                Assert.True(File.Exists(fallbackErrorPath));
                var errorText = File.ReadAllText(fallbackErrorPath);
                Assert.Contains("manual harness failed", errorText, StringComparison.Ordinal);
            }
            finally
            {
                DeleteTemporaryDirectory(temporaryDirectory);
            }
        }

        private static string CreateTemporaryDirectory()
        {
            var directoryPath = Path.Combine(
                Path.GetTempPath(),
                "FramePlayer.Core.Tests",
                Guid.NewGuid().ToString("N"));

            Directory.CreateDirectory(directoryPath);
            return directoryPath;
        }

        private static void DeleteTemporaryDirectory(string directoryPath)
        {
            if (!string.IsNullOrWhiteSpace(directoryPath) && Directory.Exists(directoryPath))
            {
                Directory.Delete(directoryPath, true);
            }
        }

        private static string EscapeJson(string value)
        {
            return (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
    }
}
