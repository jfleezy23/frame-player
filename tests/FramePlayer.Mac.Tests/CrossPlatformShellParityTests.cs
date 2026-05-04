using System;
using System.IO;
using Xunit;

namespace FramePlayer.Mac.Tests
{
    public sealed class CrossPlatformShellParityTests
    {
        [Fact]
        public void FileMenuCommands_MatchWindowsBehaviorContract()
        {
            var windowsXaml = ReadRepositoryFile("MainWindow.xaml");
            var windowsCode = ReadRepositoryFile("MainWindow.xaml.cs");
            var macCode = ReadRepositoryFile("src", "FramePlayer.Mac", "Views", "MainWindow.axaml.cs");

            Assert.Contains("Header=\"_New Window\"", windowsXaml, StringComparison.Ordinal);
            Assert.Contains("InputGestureText=\"Ctrl+N\"", windowsXaml, StringComparison.Ordinal);
            Assert.Contains("LaunchNewWindow();", windowsCode, StringComparison.Ordinal);
            Assert.Contains("CreateMenuItem(\"New Window\"", macCode, StringComparison.Ordinal);
            Assert.Contains("new KeyGesture(Key.N, KeyModifiers.Meta)", macCode, StringComparison.Ordinal);
            Assert.Contains("LaunchNewWindow();", macCode, StringComparison.Ordinal);

            Assert.Contains("Header=\"_Open Video...\"", windowsXaml, StringComparison.Ordinal);
            Assert.Contains("InputGestureText=\"Ctrl+O\"", windowsXaml, StringComparison.Ordinal);
            Assert.Contains("CreateMenuItem(\"Open Video...\"", macCode, StringComparison.Ordinal);
            Assert.Contains("new KeyGesture(Key.O, KeyModifiers.Meta)", macCode, StringComparison.Ordinal);

            Assert.Contains("Header=\"_Close Video\"", windowsXaml, StringComparison.Ordinal);
            Assert.Contains("InputGestureText=\"Ctrl+W\"", windowsXaml, StringComparison.Ordinal);
            Assert.Contains("CreateMenuItem(\"Close Video\"", macCode, StringComparison.Ordinal);
            Assert.Contains("new KeyGesture(Key.W, KeyModifiers.Meta)", macCode, StringComparison.Ordinal);
        }

        [Fact]
        public void OpenRecent_TargetsPersistentlyFocusedPaneOnBothShells()
        {
            var windowsCode = ReadRepositoryFile("MainWindow.xaml.cs");
            var macCode = ReadRepositoryFile("src", "FramePlayer.Mac", "Views", "MainWindow.axaml.cs");

            Assert.Contains("await OpenMediaAsync(recentPath);", windowsCode, StringComparison.Ordinal);
            Assert.Contains("private string GetFocusedPaneId()", windowsCode, StringComparison.Ordinal);
            Assert.Contains("TrySelectPaneForShell", windowsCode, StringComparison.Ordinal);

            Assert.Contains("private Pane GetFileOpenTargetPane()", macCode, StringComparison.Ordinal);
            Assert.Contains("return OpenPathAsync(filePath, GetFileOpenTargetPane());", macCode, StringComparison.Ordinal);
            Assert.Contains("private void SelectPane(Pane pane)", macCode, StringComparison.Ordinal);
            Assert.DoesNotContain("ComparePaneBorder.IsPointerOver", macCode, StringComparison.Ordinal);
        }

        [Fact]
        public void ComparePaneFocusHighlight_UsesSameAccentContract()
        {
            var windowsXaml = ReadRepositoryFile("MainWindow.xaml");
            var windowsCode = ReadRepositoryFile("MainWindow.xaml.cs");
            var macCode = ReadRepositoryFile("src", "FramePlayer.Mac", "Views", "MainWindow.axaml.cs");

            Assert.Contains("x:Key=\"PaneSelectedBorderBrush\" Color=\"#5AA9E6\"", windowsXaml, StringComparison.Ordinal);
            Assert.Contains("paneSnapshot.IsFocused", windowsCode, StringComparison.Ordinal);
            Assert.Contains("PaneSelectedBorderBrush = Brush.Parse(\"#5AA9E6\")", macCode, StringComparison.Ordinal);
            Assert.Contains("PrimaryPaneBorder.BorderThickness = new Thickness(1);", macCode, StringComparison.Ordinal);
            Assert.Contains("ComparePaneBorder.BorderThickness = new Thickness(1);", macCode, StringComparison.Ordinal);
            Assert.DoesNotContain("new Thickness(2)", macCode, StringComparison.Ordinal);
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
                    Directory.Exists(Path.Combine(directory.FullName, "src", "FramePlayer.Mac")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException("Could not find frame-player repository root from " + AppContext.BaseDirectory);
        }
    }
}
