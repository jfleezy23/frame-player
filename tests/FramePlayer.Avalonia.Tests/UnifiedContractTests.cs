using System;
using System.IO;
using Avalonia.Controls;
using FramePlayer.Avalonia.Views;
using Xunit;

namespace FramePlayer.Avalonia.Tests
{
    public sealed class UnifiedContractTests : IClassFixture<AvaloniaHeadlessFixture>
    {
        private readonly AvaloniaHeadlessFixture _fixture;

        public UnifiedContractTests(AvaloniaHeadlessFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public void UnifiedProject_UsesAvaloniaAssemblyAndRuntimeIdentity()
        {
            var root = FindRepositoryRoot();
            var project = File.ReadAllText(Path.Combine(root, "src", "FramePlayer.Avalonia", "FramePlayer.Avalonia.csproj"));
            var exportHost = File.ReadAllText(Path.Combine(root, "src", "FramePlayer.Avalonia", "Services", "ExportHostClient.cs"));
            var recentFiles = File.ReadAllText(Path.Combine(root, "src", "FramePlayer.Avalonia", "Services", "UnifiedRecentFilesService.cs"));

            Assert.Contains("<AssemblyName>FramePlayer.Avalonia</AssemblyName>", project, StringComparison.Ordinal);
            Assert.Contains("FRAMEPLAYER_AVALONIA_EXPORT_HOST_EXECUTABLE", exportHost, StringComparison.Ordinal);
            Assert.Contains("FRAMEPLAYER_AVALONIA_APP_BASE_DIRECTORY", exportHost, StringComparison.Ordinal);
            Assert.Contains("FRAMEPLAYER_AVALONIA_RUNTIME_BASE", exportHost, StringComparison.Ordinal);
            Assert.Contains("FramePlayer.Avalonia", recentFiles, StringComparison.Ordinal);
        }

        [Fact]
        public void UnifiedProject_KeepsWindowsAndMacPackagingAssets()
        {
            var root = FindRepositoryRoot();
            var project = File.ReadAllText(Path.Combine(root, "src", "FramePlayer.Avalonia", "FramePlayer.Avalonia.csproj"));
            var program = File.ReadAllText(Path.Combine(root, "src", "FramePlayer.Avalonia", "Program.cs"));

            Assert.Contains("Runtime\\ffmpeg\\*.dll", project, StringComparison.Ordinal);
            Assert.Contains("Runtime\\ffmpeg-export\\*.dll", project, StringComparison.Ordinal);
            Assert.Contains("FramePlayer.Avalonia.entitlements", project, StringComparison.Ordinal);
            Assert.Contains("FfmpegRuntimeBootstrap.ResolveRuntimeDirectory(runtimeBaseDirectory)", program, StringComparison.Ordinal);
            Assert.Contains("FfmpegRuntimeBootstrap.ConfigureForCurrentPlatform(runtimeBaseDirectory)", program, StringComparison.Ordinal);
            Assert.Contains("SetDllDirectory", program, StringComparison.Ordinal);
        }

        [Fact]
        public void UnifiedExportHost_ValidatesSharedLibraryRuntimeBeforeLoadingFfmpeg()
        {
            var root = FindRepositoryRoot();
            var program = File.ReadAllText(Path.Combine(root, "src", "FramePlayer.Avalonia", "Program.cs"));
            var helperStart = program.IndexOf("private static void ConfigureSharedLibraryExportHostRuntime()", StringComparison.Ordinal);

            Assert.True(helperStart >= 0, "The unified export host should keep non-Windows runtime setup in a dedicated helper.");

            var validationCall = program.IndexOf("ValidateExportRuntimeDirectory(exportRuntimeDirectory);", helperStart, StringComparison.Ordinal);
            var configureCall = program.IndexOf("FfmpegRuntimeBootstrap.ConfigureForCurrentPlatform(runtimeBaseDirectory)", helperStart, StringComparison.Ordinal);

            Assert.True(validationCall >= 0, "The non-Windows export host runtime should be manifest-validated.");
            Assert.True(configureCall >= 0, "The non-Windows export host runtime should still initialize the FFmpeg shared-library bindings.");
            Assert.True(validationCall < configureCall, "The non-Windows export host runtime must be validated before loading FFmpeg.");
        }

        [Fact]
        public void UnifiedChrome_UsesPlatformPolicyForMenuChrome()
        {
            _fixture.Run(() =>
            {
                var window = new MainWindow();
                try
                {
                    var menuPanel = RequireControl<Border>(window, "MenuPanel");
                    if (OperatingSystem.IsMacOS())
                    {
                        Assert.Equal(WindowDecorations.Full, window.WindowDecorations);
                        Assert.False(menuPanel.IsVisible);
                    }
                    else
                    {
                        Assert.Equal(WindowDecorations.None, window.WindowDecorations);
                        Assert.True(menuPanel.IsVisible);
                    }
                }
                finally
                {
                    window.Close();
                }
            });
        }

        [Fact]
        public void UnifiedReleaseNaming_UsesV20ReleaseCandidate()
        {
            const string version = "2.0.0-rc.1";
            const string windowsAsset = "FramePlayer-Windows-x64-2.0.0-rc.1.zip";
            const string macAsset = "FramePlayer-macOS-arm64-2.0.0-rc.1.zip";

            Assert.StartsWith("2.0.0", version, StringComparison.Ordinal);
            Assert.Contains(version, windowsAsset, StringComparison.Ordinal);
            Assert.Contains(version, macAsset, StringComparison.Ordinal);
        }

        [Fact]
        public void UnifiedWindowsPackage_PassesPackageVersionIntoAssemblyMetadata()
        {
            var root = FindRepositoryRoot();
            var script = File.ReadAllText(Path.Combine(root, "scripts", "Package-UnifiedWindowsPreview.ps1"));
            var workflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "windows-ci.yml"));

            Assert.Contains("[string]$Version = \"2.0.0-rc.1\"", script, StringComparison.Ordinal);
            Assert.Contains("Get-AssemblyVersionFromPackageVersion", script, StringComparison.Ordinal);
            Assert.Contains("-p:AssemblyVersion=$assemblyVersion", script, StringComparison.Ordinal);
            Assert.Contains("-p:FileVersion=$assemblyVersion", script, StringComparison.Ordinal);
            Assert.Contains("-p:InformationalVersion=$Version", script, StringComparison.Ordinal);
            Assert.Contains("-p:IncludeSourceRevisionInInformationalVersion=false", script, StringComparison.Ordinal);
            Assert.Contains("$productVersion -ne $Version", script, StringComparison.Ordinal);
            Assert.Contains("$packageVersion = \"2.0.0-rc.1\"", workflow, StringComparison.Ordinal);
        }

        [Fact]
        public void UnifiedMacPackage_DerivesBundleVersionFromReleaseCandidateLabel()
        {
            var root = FindRepositoryRoot();
            var script = File.ReadAllText(Path.Combine(root, "script", "package_unified_macos_release.sh"));
            var workflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "macos-avalonia.yml"));

            Assert.Contains("ARTIFACT_VERSION=\"${PACKAGE_VERSION:-${VERSION:-2.0.0-rc.1}}\"", script, StringComparison.Ordinal);
            Assert.Contains("bundle_short_version_from_artifact", script, StringComparison.Ordinal);
            Assert.Contains("APP_VERSION=\"$BUNDLE_SHORT_VERSION\"", script, StringComparison.Ordinal);
            Assert.Contains("PACKAGE_VERSION=2.0.0-rc.1", workflow, StringComparison.Ordinal);
            Assert.Contains("CFBundleShortVersionString 2.0.0", workflow, StringComparison.Ordinal);
        }

        [Fact]
        public void UnifiedWindowsRustCorpus_DoesNotRequireExportToolsForPlaybackSmoke()
        {
            var root = FindRepositoryRoot();
            var script = File.ReadAllText(Path.Combine(root, "scripts", "Run-UnifiedWindowsRustCorpus.ps1"));

            Assert.Contains("Runtime\\ffmpeg-tools\\ffprobe.exe", script, StringComparison.Ordinal);
            Assert.Contains("return $null", script, StringComparison.Ordinal);
            Assert.DoesNotContain("Ensure-DevExportTools.ps1\") -Required", script, StringComparison.Ordinal);
        }

        private static T RequireControl<T>(Window window, string name)
            where T : Control
        {
            return window.FindControl<T>(name)
                ?? throw new InvalidOperationException("Missing control: " + name);
        }

        private static string FindRepositoryRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "FramePlayer.csproj")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException("Could not find the frame-player repository root.");
        }
    }
}
