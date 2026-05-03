using System;
using System.IO;
using Xunit;

namespace FramePlayer.Desktop.Tests
{
    public sealed class CrossPlatformShellParityTests
    {
        [Fact]
        public void FileMenuCommands_MatchWindowsBehaviorContract()
        {
            var windowsXaml = ReadRepositoryFile("MainWindow.xaml");
            var windowsCode = ReadRepositoryFile("MainWindow.xaml.cs");
            var desktopCode = ReadRepositoryFile("src", "FramePlayer.Desktop", "Views", "MainWindow.axaml.cs");

            Assert.Contains("Header=\"_New Window\"", windowsXaml, StringComparison.Ordinal);
            Assert.Contains("InputGestureText=\"Ctrl+N\"", windowsXaml, StringComparison.Ordinal);
            Assert.Contains("LaunchNewWindow();", windowsCode, StringComparison.Ordinal);
            Assert.Contains("CreateMenuItem(\"New Window\"", desktopCode, StringComparison.Ordinal);
            Assert.Contains("new KeyGesture(Key.N, CommandKeyModifier)", desktopCode, StringComparison.Ordinal);
            Assert.Contains("e.KeyModifiers.HasFlag(CommandKeyModifier)", desktopCode, StringComparison.Ordinal);
            Assert.Contains("LaunchNewWindow();", desktopCode, StringComparison.Ordinal);

            Assert.Contains("Header=\"_Open Video...\"", windowsXaml, StringComparison.Ordinal);
            Assert.Contains("InputGestureText=\"Ctrl+O\"", windowsXaml, StringComparison.Ordinal);
            Assert.Contains("CreateMenuItem(\"Open Video...\"", desktopCode, StringComparison.Ordinal);
            Assert.Contains("new KeyGesture(Key.O, CommandKeyModifier)", desktopCode, StringComparison.Ordinal);

            Assert.Contains("Header=\"_Close Video\"", windowsXaml, StringComparison.Ordinal);
            Assert.Contains("InputGestureText=\"Ctrl+W\"", windowsXaml, StringComparison.Ordinal);
            Assert.Contains("CreateMenuItem(\"Close Video\"", desktopCode, StringComparison.Ordinal);
            Assert.Contains("new KeyGesture(Key.W, CommandKeyModifier)", desktopCode, StringComparison.Ordinal);
        }

        [Fact]
        public void OpenRecent_TargetsPersistentlyFocusedPaneOnBothShells()
        {
            var windowsCode = ReadRepositoryFile("MainWindow.xaml.cs");
            var desktopCode = ReadRepositoryFile("src", "FramePlayer.Desktop", "Views", "MainWindow.axaml.cs");

            Assert.Contains("await OpenMediaAsync(recentPath);", windowsCode, StringComparison.Ordinal);
            Assert.Contains("private string GetFocusedPaneId()", windowsCode, StringComparison.Ordinal);
            Assert.Contains("TrySelectPaneForShell", windowsCode, StringComparison.Ordinal);

            Assert.Contains("private Pane GetFileOpenTargetPane()", desktopCode, StringComparison.Ordinal);
            Assert.Contains("return OpenPathAsync(filePath, GetFileOpenTargetPane());", desktopCode, StringComparison.Ordinal);
            Assert.Contains("private void SelectPane(Pane pane)", desktopCode, StringComparison.Ordinal);
            Assert.DoesNotContain("ComparePaneBorder.IsPointerOver", desktopCode, StringComparison.Ordinal);
        }

        [Fact]
        public void ComparePaneFocusHighlight_UsesSameAccentContract()
        {
            var windowsXaml = ReadRepositoryFile("MainWindow.xaml");
            var windowsCode = ReadRepositoryFile("MainWindow.xaml.cs");
            var desktopCode = ReadRepositoryFile("src", "FramePlayer.Desktop", "Views", "MainWindow.axaml.cs");

            Assert.Contains("x:Key=\"PaneSelectedBorderBrush\" Color=\"#5AA9E6\"", windowsXaml, StringComparison.Ordinal);
            Assert.Contains("paneSnapshot.IsFocused", windowsCode, StringComparison.Ordinal);
            Assert.Contains("PaneSelectedBorderBrush = Brush.Parse(\"#5AA9E6\")", desktopCode, StringComparison.Ordinal);
            Assert.Contains("PrimaryPaneBorder.BorderThickness = primaryIsFocused ? new Thickness(2)", desktopCode, StringComparison.Ordinal);
            Assert.Contains("ComparePaneBorder.BorderThickness = compareIsFocused ? new Thickness(2)", desktopCode, StringComparison.Ordinal);
        }

        private static string ReadRepositoryFile(params string[] pathParts)
        {
            var root = FindRepositoryRoot();
            var path = Path.Combine(Combine(root, pathParts));
            return File.ReadAllText(path);
        }

        private static string[] Combine(string root, string[] pathParts)
        {
            var parts = new string[pathParts.Length + 1];
            parts[0] = root;
            Array.Copy(pathParts, 0, parts, 1, pathParts.Length);
            return parts;
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
