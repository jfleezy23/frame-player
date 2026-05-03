using System;
using System.IO;
using Xunit;

namespace FramePlayer.Desktop.Tests
{
    public sealed class DesktopPreviewIsolationTests
    {
        [Fact]
        public void DesktopPreviewProject_IsSeparateFromShippingMacPreview()
        {
            var root = FindRepositoryRoot();
            var project = File.ReadAllText(Path.Combine(root, "src", "FramePlayer.Desktop", "FramePlayer.Desktop.csproj"));

            Assert.Contains("<AssemblyName>FramePlayer.Desktop</AssemblyName>", project, StringComparison.Ordinal);
            Assert.Contains("Runtime\\ffmpeg\\*.dll", project, StringComparison.Ordinal);
            Assert.Contains("Runtime\\ffmpeg-export\\*.dll", project, StringComparison.Ordinal);
            Assert.Contains("FramePlayer.Desktop.Tests", File.ReadAllText(Path.Combine(root, "src", "FramePlayer.Desktop", "Properties", "AssemblyInfo.cs")), StringComparison.Ordinal);
            Assert.DoesNotContain("FramePlayer.Mac", project, StringComparison.Ordinal);
            Assert.DoesNotContain("src\\FramePlayer.Mac", project, StringComparison.Ordinal);
            Assert.DoesNotContain("src/FramePlayer.Mac", project, StringComparison.Ordinal);
        }

        [Fact]
        public void DesktopPreviewSource_UsesDesktopEnvironmentNames()
        {
            var root = FindRepositoryRoot();
            var exportHost = File.ReadAllText(Path.Combine(root, "src", "FramePlayer.Desktop", "Services", "ExportHostClient.cs"));
            var exportRuntimeManifest = File.ReadAllText(Path.Combine(root, "src", "FramePlayer.Desktop", "Services", "ExportRuntimeManifestService.cs"));
            var mainWindow = File.ReadAllText(Path.Combine(root, "src", "FramePlayer.Desktop", "Views", "MainWindow.axaml.cs"));
            var recentFiles = File.ReadAllText(Path.Combine(root, "src", "FramePlayer.Desktop", "Services", "DesktopRecentFilesService.cs"));

            Assert.Contains("FRAMEPLAYER_DESKTOP_EXPORT_HOST_EXECUTABLE", exportHost, StringComparison.Ordinal);
            Assert.Contains("FRAMEPLAYER_DESKTOP_APP_BASE_DIRECTORY", exportHost, StringComparison.Ordinal);
            Assert.Contains("ResolveDefaultExecutableName", exportHost, StringComparison.Ordinal);
            Assert.Contains("WindowsRequiredRuntimeFiles", exportRuntimeManifest, StringComparison.Ordinal);
            Assert.Contains("avcodec-62.dll", exportRuntimeManifest, StringComparison.Ordinal);
            Assert.Contains("FRAMEPLAYER_DESKTOP_SKIP_RUNTIME_BOOTSTRAP", mainWindow, StringComparison.Ordinal);
            Assert.Contains("CommandKeyModifier", mainWindow, StringComparison.Ordinal);
            Assert.Contains("FramePlayer.DesktopPreview", recentFiles, StringComparison.Ordinal);
            Assert.DoesNotContain("FRAMEPLAYER_MAC", exportHost, StringComparison.Ordinal);
            Assert.DoesNotContain("FRAMEPLAYER_MAC", mainWindow, StringComparison.Ordinal);
            Assert.DoesNotContain("macOS preview release track", mainWindow, StringComparison.Ordinal);
        }

        private static string FindRepositoryRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "MainWindow.xaml")) &&
                    Directory.Exists(Path.Combine(directory.FullName, "src", "FramePlayer.Desktop")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException("Could not find frame-player repository root from " + AppContext.BaseDirectory);
        }
    }
}
