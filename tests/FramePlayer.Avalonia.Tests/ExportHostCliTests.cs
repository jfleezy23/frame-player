using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FramePlayer.Core.Models;
using FramePlayer.Diagnostics;
using FramePlayer.Services;
using Xunit;

namespace FramePlayer.Avalonia.Tests
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
        public void TryGetRequestPath_IgnoresArgumentsWithoutHostRequest()
        {
            Assert.False(ExportHostCli.TryGetRequestPath(null!, out var nullPath));
            Assert.Empty(nullPath);
            Assert.False(ExportHostCli.TryGetRequestPath(Array.Empty<string>(), out var emptyPath));
            Assert.Empty(emptyPath);
            Assert.False(ExportHostCli.TryGetRequestPath(new[] { "--other", "request.json" }, out var otherPath));
            Assert.Empty(otherPath);
            Assert.Throws<ArgumentException>(() => ExportHostCli.TryGetRequestPath(
                new[] { "--run-export-request", " " },
                out _));
        }

        [Fact]
        public async Task RunAsync_ValidatesRequestsAndWritesProbeFailureResponse()
        {
            var temporaryDirectory = CreateTemporaryDirectory();

            try
            {
                await Assert.ThrowsAsync<ArgumentException>(() => ExportHostCli.RunAsync(string.Empty, CancellationToken.None));
                await Assert.ThrowsAsync<FileNotFoundException>(() => ExportHostCli.RunAsync(
                    Path.Combine(temporaryDirectory, "missing.json"),
                    CancellationToken.None));

                var emptyRequestPath = Path.Combine(temporaryDirectory, "empty.json");
                File.WriteAllText(emptyRequestPath, string.Empty, Encoding.UTF8);
                await Assert.ThrowsAsync<InvalidOperationException>(() => ExportHostCli.RunAsync(
                    emptyRequestPath,
                    CancellationToken.None));

                var missingResponsePath = Path.Combine(temporaryDirectory, "missing-response.json");
                WriteRequest(missingResponsePath, new ExportHostRequest { Operation = "unsupported" });
                await Assert.ThrowsAsync<InvalidOperationException>(() => ExportHostCli.RunAsync(
                    missingResponsePath,
                    CancellationToken.None));

                var unsupportedRequestPath = Path.Combine(temporaryDirectory, "unsupported.json");
                WriteRequest(unsupportedRequestPath, new ExportHostRequest
                {
                    Operation = "unsupported",
                    ResponseJsonPath = Path.Combine(temporaryDirectory, "unsupported-response.json")
                });
                var unsupported = await Assert.ThrowsAsync<InvalidOperationException>(() => ExportHostCli.RunAsync(
                    unsupportedRequestPath,
                    CancellationToken.None));
                Assert.Contains("Unsupported export host operation", unsupported.Message, StringComparison.Ordinal);

                var probeResponsePath = Path.Combine(temporaryDirectory, "nested", "probe-response.json");
                var probeRequestPath = Path.Combine(temporaryDirectory, "probe.json");
                WriteRequest(probeRequestPath, new ExportHostRequest
                {
                    Operation = ExportHostClient.ProbeOperation,
                    ProbeFilePath = Path.Combine(temporaryDirectory, "missing-video.mp4"),
                    ResponseJsonPath = probeResponsePath
                });

                var exitCode = await ExportHostCli.RunAsync(probeRequestPath, CancellationToken.None);

                Assert.Equal(2, exitCode);
                Assert.True(File.Exists(probeResponsePath));
                var response = JsonSerializer.Deserialize<ExportHostResponse>(
                    File.ReadAllText(probeResponsePath, Encoding.UTF8),
                    ExportHostClient.JsonOptions);
                Assert.NotNull(response);
                Assert.Equal(ExportHostClient.ProbeOperation, response!.Operation);
                Assert.False(string.IsNullOrWhiteSpace(response.FailureMessage));
                Assert.NotNull(response.MediaInfo);
            }
            finally
            {
                DeleteTemporaryDirectory(temporaryDirectory);
            }
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

        private static void WriteRequest(string requestPath, ExportHostRequest request)
        {
            File.WriteAllText(
                requestPath,
                JsonSerializer.Serialize(request, ExportHostClient.JsonOptions),
                new UTF8Encoding(false));
        }
    }
}
