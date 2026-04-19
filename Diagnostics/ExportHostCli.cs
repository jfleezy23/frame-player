using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FramePlayer.Core.Models;
using FramePlayer.Services;

namespace FramePlayer.Diagnostics
{
    internal static class ExportHostCli
    {
        private const string RequestArgument = "--run-export-request";

        public static bool TryGetRequestPath(string[] args, out string requestPath)
        {
            requestPath = string.Empty;
            if (args == null || args.Length == 0)
            {
                return false;
            }

            for (var index = 0; index < args.Length; index++)
            {
                if (!string.Equals(args[index], RequestArgument, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                {
                    throw new ArgumentException("The export request path is missing.");
                }

                requestPath = Path.GetFullPath(args[index + 1]);
                return true;
            }

            return false;
        }

        public static async Task<int> RunAsync(string requestPath, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(requestPath))
            {
                throw new ArgumentException("An export request path is required.", nameof(requestPath));
            }

            var request = ReadRequest(requestPath);
            if (request == null)
            {
                throw new InvalidOperationException("The export request could not be read.");
            }

            if (string.IsNullOrWhiteSpace(request.ResponseJsonPath))
            {
                throw new InvalidOperationException("The export request did not include a response output path.");
            }

            ExportHostResponse response;
            switch ((request.Operation ?? string.Empty).Trim().ToLowerInvariant())
            {
                case ExportHostClient.ProbeOperation:
                    response = RunProbe(request);
                    break;
                case ExportHostClient.AudioInsertionOperation:
                    response = await RunAudioInsertionAsync(request, cancellationToken).ConfigureAwait(false);
                    break;
                case ExportHostClient.ClipExportOperation:
                    response = await RunClipExportAsync(request, cancellationToken).ConfigureAwait(false);
                    break;
                case ExportHostClient.CompareExportOperation:
                    response = await RunCompareExportAsync(request, cancellationToken).ConfigureAwait(false);
                    break;
                default:
                    throw new InvalidOperationException("Unsupported export host operation: " + request.Operation);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(request.ResponseJsonPath) ?? ".");
            WriteResponse(request.ResponseJsonPath, response);

            if (!string.IsNullOrWhiteSpace(response.FailureMessage))
            {
                return 2;
            }

            if (response.ClipExportResult != null && !response.ClipExportResult.Succeeded)
            {
                return 2;
            }

            if (response.AudioInsertionResult != null && !response.AudioInsertionResult.Succeeded)
            {
                return 2;
            }

            if (response.CompareSideBySideExportResult != null && !response.CompareSideBySideExportResult.Succeeded)
            {
                return 2;
            }

            return 0;
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
                if (request == null)
                {
                    return;
                }

                var errorPath = string.IsNullOrWhiteSpace(request.ErrorPath)
                    ? request.ResponseJsonPath + ".error.txt"
                    : request.ErrorPath;
                if (string.IsNullOrWhiteSpace(errorPath))
                {
                    return;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(errorPath) ?? ".");
                File.WriteAllText(errorPath, exception.ToString(), new UTF8Encoding(false));
            }
            catch
            {
                // Best-effort CLI failure logging must never hide the original export-host exception path.
            }
        }

        private static ExportHostResponse RunProbe(ExportHostRequest request)
        {
            if (!MediaProbeService.TryProbeVideoMediaInfo(request.ProbeFilePath, out var mediaInfo, out var errorMessage))
            {
                return new ExportHostResponse
                {
                    Operation = ExportHostClient.ProbeOperation,
                    FailureMessage = string.IsNullOrWhiteSpace(errorMessage)
                        ? "Media probe failed."
                        : errorMessage,
                    MediaInfo = VideoMediaInfo.Empty
                };
            }

            return new ExportHostResponse
            {
                Operation = ExportHostClient.ProbeOperation,
                MediaInfo = mediaInfo ?? VideoMediaInfo.Empty
            };
        }

        private static async Task<ExportHostResponse> RunAudioInsertionAsync(
            ExportHostRequest request,
            CancellationToken cancellationToken)
        {
            if (request.AudioInsertionPlan == null)
            {
                throw new InvalidOperationException("The export host request did not include an audio insertion plan.");
            }

            return new ExportHostResponse
            {
                Operation = ExportHostClient.AudioInsertionOperation,
                AudioInsertionResult = await NativeAudioInsertionService.InsertAsync(request.AudioInsertionPlan, cancellationToken).ConfigureAwait(false)
            };
        }

        private static async Task<ExportHostResponse> RunClipExportAsync(
            ExportHostRequest request,
            CancellationToken cancellationToken)
        {
            if (request.ClipExportPlan == null)
            {
                throw new InvalidOperationException("The export host request did not include a clip export plan.");
            }

            return new ExportHostResponse
            {
                Operation = ExportHostClient.ClipExportOperation,
                ClipExportResult = await NativeClipExportService.ExportAsync(request.ClipExportPlan, cancellationToken).ConfigureAwait(false)
            };
        }

        private static async Task<ExportHostResponse> RunCompareExportAsync(
            ExportHostRequest request,
            CancellationToken cancellationToken)
        {
            if (request.CompareSideBySideExportPlan == null)
            {
                throw new InvalidOperationException("The export host request did not include a compare export plan.");
            }

            return new ExportHostResponse
            {
                Operation = ExportHostClient.CompareExportOperation,
                CompareSideBySideExportResult = await NativeCompareSideBySideExportService.ExportAsync(
                    request.CompareSideBySideExportPlan,
                    cancellationToken).ConfigureAwait(false)
            };
        }

        private static ExportHostRequest ReadRequest(string requestPath)
        {
            if (!File.Exists(requestPath))
            {
                throw new FileNotFoundException("The export request file was not found.", requestPath);
            }

            var json = File.ReadAllText(requestPath, Encoding.UTF8);
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            return JsonSerializer.Deserialize<ExportHostRequest>(json, ExportHostClient.JsonOptions);
        }

        private static void WriteResponse(string responsePath, ExportHostResponse response)
        {
            var json = JsonSerializer.Serialize(response ?? new ExportHostResponse(), ExportHostClient.JsonOptions);
            File.WriteAllText(responsePath, json, new UTF8Encoding(false));
        }
    }
}
