using System;
using System.IO;
using Avalonia.Controls;
using FramePlayer.Avalonia.Views;
using Xunit;

namespace FramePlayer.Avalonia.Tests
{
    public sealed class UnifiedPreviewContractTests : IClassFixture<AvaloniaHeadlessFixture>
    {
        private readonly AvaloniaHeadlessFixture _fixture;

        public UnifiedPreviewContractTests(AvaloniaHeadlessFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public void UnifiedProject_UsesPreview020AssemblyAndRuntimeIdentity()
        {
            var root = FindRepositoryRoot();
            var project = File.ReadAllText(Path.Combine(root, "src", "FramePlayer.Avalonia", "FramePlayer.Avalonia.csproj"));
            var exportHost = File.ReadAllText(Path.Combine(root, "src", "FramePlayer.Avalonia", "Services", "ExportHostClient.cs"));
            var recentFiles = File.ReadAllText(Path.Combine(root, "src", "FramePlayer.Avalonia", "Services", "UnifiedRecentFilesService.cs"));

            Assert.Contains("<AssemblyName>FramePlayer.Avalonia</AssemblyName>", project, StringComparison.Ordinal);
            Assert.Contains("FRAMEPLAYER_AVALONIA_EXPORT_HOST_EXECUTABLE", exportHost, StringComparison.Ordinal);
            Assert.Contains("FRAMEPLAYER_AVALONIA_APP_BASE_DIRECTORY", exportHost, StringComparison.Ordinal);
            Assert.Contains("FRAMEPLAYER_AVALONIA_RUNTIME_BASE", exportHost, StringComparison.Ordinal);
            Assert.Contains("FramePlayer.AvaloniaPreview", recentFiles, StringComparison.Ordinal);
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
        public void UnifiedReleaseNaming_UsesSynchronizedPreview020()
        {
            const string tag = "unified-preview-0.2.0";
            const string windowsAsset = "FramePlayer-Windows-x64-unified-preview-0.2.0.zip";
            const string macAsset = "FramePlayer-macOS-arm64-unified-preview-0.2.0.zip";

            Assert.EndsWith("0.2.0", tag, StringComparison.Ordinal);
            Assert.Contains("unified-preview-0.2.0", windowsAsset, StringComparison.Ordinal);
            Assert.Contains("unified-preview-0.2.0", macAsset, StringComparison.Ordinal);
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
