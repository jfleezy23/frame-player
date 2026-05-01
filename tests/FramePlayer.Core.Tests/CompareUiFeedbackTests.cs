using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace FramePlayer.Core.Tests
{
    public sealed class CompareUiFeedbackTests
    {
        private static readonly XNamespace XamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";

        [Fact]
        public void CompareToolbar_UsesSyncTerminology_ForPaneTimingActions()
        {
            var xaml = File.ReadAllText(GetMainWindowXamlPath());
            var document = XDocument.Parse(xaml);

            Assert.Equal("Sync Right to Left", GetNamedElementAttribute(document, "AlignRightToLeftButton", "Content"));
            Assert.Equal("Sync Left to Right", GetNamedElementAttribute(document, "AlignLeftToRightButton", "Content"));
            Assert.Contains("Last sync: none", xaml, StringComparison.Ordinal);
            Assert.DoesNotContain("Align Right to Left", xaml, StringComparison.Ordinal);
            Assert.DoesNotContain("Align Left to Right", xaml, StringComparison.Ordinal);
            Assert.DoesNotContain("Last alignment: none", xaml, StringComparison.Ordinal);

            var codeBehind = File.ReadAllText(GetMainWindowCodeBehindPath());
            Assert.Contains("\"Last sync: none\"", codeBehind, StringComparison.Ordinal);
            Assert.DoesNotContain("\"Last align:", codeBehind, StringComparison.Ordinal);
            Assert.DoesNotContain("before aligning them", codeBehind, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Aligned the {0} pane", codeBehind, StringComparison.Ordinal);
        }

        [Fact]
        public void ComparePaneTransport_UsesSinglePlayPauseTogglePerPane()
        {
            var document = XDocument.Load(GetMainWindowXamlPath());

            AssertPanePlayPauseToggle(document, "PrimaryPanePlayPauseButton", "PrimaryPanePlayPauseIcon", "pane-primary");
            AssertPanePlayPauseToggle(document, "ComparePanePlayPauseButton", "ComparePanePlayPauseIcon", "pane-compare-a");
            Assert.Null(FindNamedElement(document, "PrimaryPanePlayButton"));
            Assert.Null(FindNamedElement(document, "PrimaryPanePauseButton"));
            Assert.Null(FindNamedElement(document, "ComparePanePlayButton"));
            Assert.Null(FindNamedElement(document, "ComparePanePauseButton"));
        }

        [Fact]
        public void ComparePaneTransport_IncludesSingleAndHundredFrameSkipControls()
        {
            var document = XDocument.Load(GetMainWindowXamlPath());

            AssertTransportButton(
                document,
                "PrimaryPaneStepBackButton",
                "pane-primary",
                "Previous Frame",
                "PaneStepBackButton_Click",
                "StepBackIcon");
            AssertTransportButton(
                document,
                "PrimaryPaneSkipBackHundredFramesButton",
                "pane-primary",
                "Rewind 100 Frames",
                "PaneSkipBackHundredFramesButton_Click",
                "RewindIcon");
            AssertPanePlayPauseToggle(document, "PrimaryPanePlayPauseButton", "PrimaryPanePlayPauseIcon", "pane-primary");
            AssertTransportButton(
                document,
                "PrimaryPaneSkipForwardHundredFramesButton",
                "pane-primary",
                "Fast Forward 100 Frames",
                "PaneSkipForwardHundredFramesButton_Click",
                "FastForwardIcon");
            AssertTransportButton(
                document,
                "PrimaryPaneStepForwardButton",
                "pane-primary",
                "Next Frame",
                "PaneStepForwardButton_Click",
                "StepForwardIcon");

            AssertTransportButton(
                document,
                "ComparePaneStepBackButton",
                "pane-compare-a",
                "Previous Frame",
                "PaneStepBackButton_Click",
                "StepBackIcon");
            AssertTransportButton(
                document,
                "ComparePaneSkipBackHundredFramesButton",
                "pane-compare-a",
                "Rewind 100 Frames",
                "PaneSkipBackHundredFramesButton_Click",
                "RewindIcon");
            AssertPanePlayPauseToggle(document, "ComparePanePlayPauseButton", "ComparePanePlayPauseIcon", "pane-compare-a");
            AssertTransportButton(
                document,
                "ComparePaneSkipForwardHundredFramesButton",
                "pane-compare-a",
                "Fast Forward 100 Frames",
                "PaneSkipForwardHundredFramesButton_Click",
                "FastForwardIcon");
            AssertTransportButton(
                document,
                "ComparePaneStepForwardButton",
                "pane-compare-a",
                "Next Frame",
                "PaneStepForwardButton_Click",
                "StepForwardIcon");
        }

        [Fact]
        public void ComparePaneTransport_OrdersFrameControlsLikeMasterTransport()
        {
            var document = XDocument.Load(GetMainWindowXamlPath());
            var masterOrder = GetSiblingButtonNames(document, "PlayPauseButton")
                .Where(name => !string.Equals(name, "ToggleFullScreenButton", StringComparison.Ordinal))
                .Select(NormalizeTransportRole)
                .ToArray();

            Assert.Equal(
                masterOrder,
                GetSiblingButtonNames(document, "PrimaryPanePlayPauseButton").Select(NormalizeTransportRole).ToArray());
            Assert.Equal(
                masterOrder,
                GetSiblingButtonNames(document, "ComparePanePlayPauseButton").Select(NormalizeTransportRole).ToArray());
        }

        private static void AssertTransportButton(
            XDocument document,
            string buttonName,
            string paneId,
            string toolTip,
            string clickHandler,
            string iconResourceName)
        {
            var button = FindNamedElement(document, buttonName);
            Assert.NotNull(button);
            Assert.Equal(toolTip, GetAttribute(button!, "ToolTip"));
            Assert.Equal(paneId, GetAttribute(button!, "Tag"));
            Assert.Equal(clickHandler, GetAttribute(button!, "Click"));
            Assert.Contains(
                button!.Descendants(),
                element => string.Equals(GetAttribute(element, "Source"), "{StaticResource " + iconResourceName + "}", StringComparison.Ordinal));
        }

        private static void AssertPanePlayPauseToggle(
            XDocument document,
            string buttonName,
            string iconName,
            string paneId)
        {
            var button = FindNamedElement(document, buttonName);
            Assert.NotNull(button);
            Assert.Equal("Play", GetAttribute(button!, "ToolTip"));
            Assert.Equal(paneId, GetAttribute(button!, "Tag"));
            Assert.Equal("PanePlayPauseButton_Click", GetAttribute(button!, "Click"));

            var icon = FindNamedElement(document, iconName);
            Assert.NotNull(icon);
            Assert.Equal("16", GetAttribute(icon!, "Width"));
            Assert.Equal("16", GetAttribute(icon!, "Height"));
        }

        private static string[] GetSiblingButtonNames(XDocument document, string buttonName)
        {
            var button = FindNamedElement(document, buttonName);
            Assert.NotNull(button);
            var parent = button!.Parent;
            Assert.NotNull(parent);
            return parent!
                .Elements()
                .Where(element => string.Equals(element.Name.LocalName, "Button", StringComparison.Ordinal))
                .Select(element => GetAttribute(element, XamlNamespace + "Name"))
                .ToArray();
        }

        private static string NormalizeTransportRole(string buttonName)
        {
            if (buttonName.Contains("StepBack", StringComparison.Ordinal) ||
                string.Equals(buttonName, "PreviousFrameButton", StringComparison.Ordinal))
            {
                return "previous-frame";
            }

            if (buttonName.Contains("Rewind", StringComparison.Ordinal) ||
                buttonName.Contains("SkipBackHundredFrames", StringComparison.Ordinal))
            {
                return "skip-back";
            }

            if (buttonName.Contains("PlayPause", StringComparison.Ordinal))
            {
                return "play-pause";
            }

            if (buttonName.Contains("FastForward", StringComparison.Ordinal) ||
                buttonName.Contains("SkipForwardHundredFrames", StringComparison.Ordinal))
            {
                return "skip-forward";
            }

            if (buttonName.Contains("StepForward", StringComparison.Ordinal) ||
                string.Equals(buttonName, "NextFrameButton", StringComparison.Ordinal))
            {
                return "next-frame";
            }

            return buttonName;
        }

        private static string GetNamedElementAttribute(XDocument document, string name, string attributeName)
        {
            var element = FindNamedElement(document, name);
            Assert.NotNull(element);
            return GetAttribute(element!, attributeName);
        }

        private static XElement? FindNamedElement(XDocument document, string name)
        {
            return document
                .Descendants()
                .FirstOrDefault(element => string.Equals(GetAttribute(element, XamlNamespace + "Name"), name, StringComparison.Ordinal));
        }

        private static string GetAttribute(XElement element, string attributeName)
        {
            return GetAttribute(element, XName.Get(attributeName));
        }

        private static string GetAttribute(XElement element, XName attributeName)
        {
            return element.Attribute(attributeName)?.Value ?? string.Empty;
        }

        private static string GetMainWindowXamlPath()
        {
            return Path.Combine(GetRepositoryRoot(), "MainWindow.xaml");
        }

        private static string GetMainWindowCodeBehindPath()
        {
            return Path.Combine(GetRepositoryRoot(), "MainWindow.xaml.cs");
        }

        private static string GetRepositoryRoot()
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

            throw new InvalidOperationException("Could not locate the repository root from the test output directory.");
        }
    }
}
