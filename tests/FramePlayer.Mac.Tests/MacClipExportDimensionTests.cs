using System;
using FramePlayer.Core.Models;
using FramePlayer.Services;
using Xunit;

namespace FramePlayer.Mac.Tests
{
    public sealed class MacClipExportDimensionTests
    {
        [Theory]
        [InlineData(1, 2)]
        [InlineData(2, 2)]
        [InlineData(641, 642)]
        [InlineData(642, 642)]
        public void NativeExportSupport_ResolvesEvenVideoDimensionsForX264(int sourceDimension, int expectedDimension)
        {
            Assert.Equal(expectedDimension, NativeExportSupport.ResolveEvenVideoDimension(sourceDimension));
        }

        [Fact]
        public void ClipExport_FullFrameOddDimensionsPadsBeforeYuv420p()
        {
            var plan = CreatePlan(PaneViewportSnapshot.CreateFullFrame(641, 479));

            var outputDimensions = NativeClipExportService.ResolveOutputDimensions(plan);
            var filterGraph = NativeClipExportService.BuildFilterGraph(plan, includeAudio: false);

            Assert.Equal((642, 480), outputDimensions);
            Assert.Contains("pad=width=642:height=480:x=0:y=0:color=black", filterGraph, StringComparison.Ordinal);
            AssertFilterPrecedes("pad=width=642", "format=pix_fmts=yuv420p", filterGraph);
        }

        [Fact]
        public void ClipExport_ZoomedOddDimensionsScalesToEvenCanvas()
        {
            var viewport = new PaneViewportSnapshot(
                2d,
                0.5d,
                0.5d,
                641,
                479,
                7,
                5,
                319,
                239);
            var plan = CreatePlan(viewport);

            var outputDimensions = NativeClipExportService.ResolveOutputDimensions(plan);
            var filterGraph = NativeClipExportService.BuildFilterGraph(plan, includeAudio: false);

            Assert.Equal((642, 480), outputDimensions);
            Assert.Contains("crop=319:239:7:5,scale=642:480:flags=lanczos", filterGraph, StringComparison.Ordinal);
            Assert.DoesNotContain("scale=641:479", filterGraph, StringComparison.Ordinal);
        }

        private static ClipExportPlan CreatePlan(PaneViewportSnapshot viewport)
        {
            return new ClipExportPlan(
                "/tmp/source.mp4",
                "/tmp/output.mp4",
                "Test",
                "primary",
                false,
                TimeSpan.Zero,
                TimeSpan.FromSeconds(1),
                0,
                30,
                "exclusive",
                viewport,
                string.Empty,
                string.Empty,
                string.Empty);
        }

        private static void AssertFilterPrecedes(string earlierFilter, string laterFilter, string filterGraph)
        {
            var earlierIndex = filterGraph.IndexOf(earlierFilter, StringComparison.Ordinal);
            var laterIndex = filterGraph.IndexOf(laterFilter, StringComparison.Ordinal);

            Assert.True(earlierIndex >= 0, earlierFilter + " was not found in " + filterGraph);
            Assert.True(laterIndex >= 0, laterFilter + " was not found in " + filterGraph);
            Assert.True(earlierIndex < laterIndex, earlierFilter + " should precede " + laterFilter + " in " + filterGraph);
        }
    }
}
