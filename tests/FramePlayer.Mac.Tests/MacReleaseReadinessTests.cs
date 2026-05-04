using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace FramePlayer.Mac.Tests
{
    public sealed class MacReleaseReadinessTests
    {
        [Fact]
        public void BuildScript_StagesAppIconAndWritesBundleIconMetadata()
        {
            var script = File.ReadAllText(RepositoryPath("script", "build_and_run.sh"));

            Assert.Contains("APP_ICON_SOURCE=", script, StringComparison.Ordinal);
            Assert.Contains("FramePlayer.icns", script, StringComparison.Ordinal);
            Assert.Contains("APP_RESOURCES=", script, StringComparison.Ordinal);
            Assert.Contains("CFBundleIconFile", script, StringComparison.Ordinal);
            Assert.Contains("<string>$APP_ICON_NAME</string>", script, StringComparison.Ordinal);
            Assert.Contains("MAC_RUNTIME_SOURCE=", script, StringComparison.Ordinal);
        }

        [Fact]
        public void MacProject_PublishesIconAndRuntimeManifests()
        {
            var project = XDocument.Load(RepositoryPath("src", "FramePlayer.Mac", "FramePlayer.Mac.csproj"));
            var projectText = project.ToString(SaveOptions.DisableFormatting);

            Assert.Contains("Assets\\FramePlayer.icns", projectText, StringComparison.Ordinal);
            Assert.Contains("Runtime\\runtime-manifest.json", projectText, StringComparison.Ordinal);
            Assert.Contains("Runtime\\export-runtime-manifest.json", projectText, StringComparison.Ordinal);
            Assert.Contains("Runtime\\export-tools-manifest.json", projectText, StringComparison.Ordinal);
            Assert.Contains("EmbeddedResource", projectText, StringComparison.Ordinal);
            Assert.Contains("FramePlayer.Runtime.export-runtime-manifest.json", projectText, StringComparison.Ordinal);
        }

        [Fact]
        public void ExportHostPath_BootstrapsRuntimeAndCleansUpCancellation()
        {
            var program = File.ReadAllText(RepositoryPath("src", "FramePlayer.Mac", "Program.cs"));
            var hostClient = File.ReadAllText(RepositoryPath("src", "FramePlayer.Mac", "Services", "ExportHostClient.cs"));

            Assert.Contains("FfmpegRuntimeBootstrap.ConfigureForCurrentPlatform(ResolveRuntimeBaseDirectory())", program, StringComparison.Ordinal);
            Assert.Contains("FRAMEPLAYER_MAC_RUNTIME_BASE", hostClient, StringComparison.Ordinal);
            Assert.Contains("process.Kill(entireProcessTree: true)", hostClient, StringComparison.Ordinal);
            Assert.Contains("OperationCanceledException", hostClient, StringComparison.Ordinal);
        }

        [Fact]
        public void MainWindow_RoutesLongRunningExportsThroughExportHost()
        {
            var mainWindow = File.ReadAllText(RepositoryPath("src", "FramePlayer.Mac", "Views", "MainWindow.axaml.cs"));

            Assert.Contains("ClipExportService.ExportPlanAsync(plan)", mainWindow, StringComparison.Ordinal);
            Assert.Contains("CompareSideBySideExportService.ExportPlanAsync(plan)", mainWindow, StringComparison.Ordinal);
            Assert.Contains("AudioInsertionService.InsertPlanAsync(plan)", mainWindow, StringComparison.Ordinal);
            Assert.DoesNotContain("NativeClipExportService.ExportAsync(plan)", mainWindow, StringComparison.Ordinal);
            Assert.DoesNotContain("NativeCompareSideBySideExportService.ExportAsync(plan)", mainWindow, StringComparison.Ordinal);
            Assert.DoesNotContain("NativeAudioInsertionService.InsertAsync(plan)", mainWindow, StringComparison.Ordinal);
        }

        [Fact]
        public void MacIconAsset_IsPresentAndNonEmpty()
        {
            var icon = new FileInfo(RepositoryPath("src", "FramePlayer.Mac", "Assets", "FramePlayer.icns"));

            Assert.True(icon.Exists);
            Assert.True(icon.Length > 1024);
        }

        [Fact]
        public void CiWorkflow_BuildsMacAppAndRunsMacTests()
        {
            var workflow = File.ReadAllText(RepositoryPath(".github", "workflows", "macos-avalonia.yml"));

            Assert.Contains("runs-on: macos-", workflow, StringComparison.Ordinal);
            Assert.Contains("dotnet build src/FramePlayer.Mac/FramePlayer.Mac.csproj -c Release", workflow, StringComparison.Ordinal);
            Assert.Contains("dotnet test tests/FramePlayer.Mac.Tests/FramePlayer.Mac.Tests.csproj -c Release", workflow, StringComparison.Ordinal);
        }

        [Fact]
        public void ReleaseCandidateValidator_RequiresRealCorpusAndRunsCorpusTests()
        {
            var script = File.ReadAllText(RepositoryPath("script", "validate_macos_release_candidate.sh"));

            Assert.Contains("This script intentionally does not download substitute sample videos.", script, StringComparison.Ordinal);
            Assert.Contains("--corpus <folder-or-zip>", script, StringComparison.Ordinal);
            Assert.Contains("Video Test Files", script, StringComparison.Ordinal);
            Assert.Contains("FRAMEPLAYER_MAC_REQUIRE_CORPUS=1", script, StringComparison.Ordinal);
            Assert.Contains("FRAMEPLAYER_MAC_APP_BUNDLE=", script, StringComparison.Ordinal);
            Assert.Contains("corpus-files.txt", script, StringComparison.Ordinal);
            Assert.Contains("libavutil.60.dylib", script, StringComparison.Ordinal);
        }

        private static string RepositoryPath(params string[] segments)
        {
            var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
            return Path.Combine(new[] { root }.Concat(segments).ToArray());
        }
    }
}
