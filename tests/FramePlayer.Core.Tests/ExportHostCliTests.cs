using System;
using System.IO;
using System.Text;
using FramePlayer.Diagnostics;
using Xunit;

namespace FramePlayer.Core.Tests
{
    public sealed class ExportHostCliTests
    {
        [Fact]
        public void TryGetRequestPath_ReturnsNormalizedAbsolutePath()
        {
            var requestPath = Path.Combine("artifacts", "export-host", "request.json");

            var handled = ExportHostCli.TryGetRequestPath(
                new[] { "--run-export-request", requestPath },
                out var resolvedPath);

            Assert.True(handled);
            Assert.Equal(Path.GetFullPath(requestPath), resolvedPath);
        }

        [Fact]
        public void TryGetRequestPath_Throws_WhenPathArgumentIsMissing()
        {
            var exception = Assert.Throws<ArgumentException>(
                () => ExportHostCli.TryGetRequestPath(
                    new[] { "--run-export-request" },
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
                var responseJsonPath = Path.Combine(temporaryDirectory, "response.json");
                var fallbackErrorPath = responseJsonPath + ".error.txt";

                File.WriteAllText(
                    requestPath,
                    "{\"operation\":\"probe\",\"probeFilePath\":\"sample.mp4\",\"responseJsonPath\":\"" + EscapeJson(responseJsonPath) + "\"}",
                    new UTF8Encoding(false));

                ExportHostCli.TryWriteFailure(requestPath, new InvalidOperationException("export host failed"));

                Assert.True(File.Exists(fallbackErrorPath));
                var errorText = File.ReadAllText(fallbackErrorPath);
                Assert.Contains("export host failed", errorText, StringComparison.Ordinal);
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
