using System;
using System.Linq;
using FramePlayer.Core.Models;
using FramePlayer.Diagnostics;
using Xunit;

namespace FramePlayer.Core.Tests
{
    public sealed class ReviewEngineManualTestRunnerTests
    {
        [Fact]
        public void EvaluateBackend_Passes_WhenPlanningFallbackStillRecoversAbsoluteFrameIdentity()
        {
            var plan = CreatePlan(
                indexAvailable: false,
                seekTimeStrategy: "quarter-duration",
                seekFrameStrategy: "duration-fps-midpoint",
                preflightError: string.Empty,
                warnings: new[]
                {
                    "Global index data was unavailable during planning, so the frame target used duration/fps estimation.",
                    "The custom FFmpeg global index was unavailable during preflight planning."
                });
            var scenario = CreateScenario(
                seekToTimeAbsolute: false,
                seekToTimeUsedGlobalIndex: false,
                seekToTimeAnchorStrategy: "timestamp-seek-pending-index",
                seekToFrameAbsolute: true,
                seekToFrameUsedGlobalIndex: false,
                seekToFrameAnchorStrategy: "stream-start",
                openIndexAvailable: false);

            var result = ReviewEngineManualTestRunner.EvaluateBackend(scenario, plan);

            Assert.Equal("pass", result.Classification);
            Assert.Empty(result.Failures);
            Assert.Empty(result.Warnings);
        }

        [Fact]
        public void EvaluateBackend_Warns_WhenPlanningFallbackDoesNotRecoverAbsoluteFrameIdentity()
        {
            var plan = CreatePlan(
                indexAvailable: false,
                seekTimeStrategy: "quarter-duration",
                seekFrameStrategy: "duration-fps-midpoint",
                preflightError: string.Empty,
                warnings: new[]
                {
                    "Global index data was unavailable during planning, so the frame target used duration/fps estimation."
                });
            var scenario = CreateScenario(
                seekToTimeAbsolute: false,
                seekToTimeUsedGlobalIndex: false,
                seekToTimeAnchorStrategy: "timestamp-seek-pending-index",
                seekToFrameAbsolute: false,
                seekToFrameUsedGlobalIndex: false,
                seekToFrameAnchorStrategy: "stream-start",
                openIndexAvailable: false);

            var result = ReviewEngineManualTestRunner.EvaluateBackend(scenario, plan);

            Assert.Equal("warning", result.Classification);
            Assert.Empty(result.Failures);
            Assert.Contains(
                result.Warnings,
                warning => warning.Contains("duration/fps estimation", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(
                result.Warnings,
                warning => warning.Contains("Seek-to-frame did not retain absolute frame identity", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(
                result.Warnings,
                warning => warning.Contains("Seek-to-time did not report absolute frame identity", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void EvaluateBackend_Warns_WhenPreflightPlanningFailed()
        {
            var plan = CreatePlan(
                indexAvailable: false,
                seekTimeStrategy: "start-fallback",
                seekFrameStrategy: "start-frame-fallback",
                preflightError: "Probe open failed.",
                warnings: new[]
                {
                    "Custom FFmpeg preflight probe failed, so the test plan fell back to start-position defaults."
                });
            var scenario = CreateScenario(
                seekToTimeAbsolute: true,
                seekToTimeUsedGlobalIndex: true,
                seekToTimeAnchorStrategy: "global-index-keyframe",
                seekToFrameAbsolute: true,
                seekToFrameUsedGlobalIndex: true,
                seekToFrameAnchorStrategy: "global-index-keyframe",
                openIndexAvailable: false);

            var result = ReviewEngineManualTestRunner.EvaluateBackend(scenario, plan);

            Assert.Equal("warning", result.Classification);
            Assert.Empty(result.Failures);
            Assert.Contains(
                result.Warnings,
                warning => warning.Contains("preflight probe failed", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(
                result.Warnings,
                warning => warning.Contains("The custom FFmpeg global index was unavailable during preflight planning.", StringComparison.Ordinal));
        }

        private static ReviewEngineManualTestPlan CreatePlan(
            bool indexAvailable,
            string seekTimeStrategy,
            string seekFrameStrategy,
            string preflightError,
            string[] warnings)
        {
            return new ReviewEngineManualTestPlan(
                "sample.mp4",
                TimeSpan.FromSeconds(1),
                42L,
                durationKnown: !string.Equals(seekTimeStrategy, "start-fallback", StringComparison.OrdinalIgnoreCase),
                nominalFpsKnown: true,
                indexAvailable: indexAvailable,
                indexedFrameCount: indexAvailable ? 100L : 0L,
                reducedTestPathUsed: !indexAvailable,
                limitedSteppingExpected: false,
                seekTimeStrategy: seekTimeStrategy,
                seekFrameStrategy: seekFrameStrategy,
                sequenceSummary: "test",
                warnings: warnings,
                preflightError: preflightError);
        }

        private static ReviewEngineScenarioReport CreateScenario(
            bool seekToTimeAbsolute,
            bool seekToTimeUsedGlobalIndex,
            string seekToTimeAnchorStrategy,
            bool seekToFrameAbsolute,
            bool seekToFrameUsedGlobalIndex,
            string seekToFrameAnchorStrategy,
            bool openIndexAvailable)
        {
            return new ReviewEngineScenarioReport(
                "custom-ffmpeg",
                CreateOperationSnapshot(
                    operationName: "open",
                    frameIndex: 0L,
                    isAbsolute: true,
                    isGlobalIndexAvailable: openIndexAvailable,
                    usedGlobalIndex: false,
                    anchorStrategy: openIndexAvailable ? "global-index-keyframe" : "stream-start-index-background"),
                CreateOperationSnapshot(
                    operationName: "playback",
                    frameIndex: 12L,
                    isAbsolute: true,
                    isGlobalIndexAvailable: openIndexAvailable,
                    usedGlobalIndex: false,
                    anchorStrategy: openIndexAvailable ? "global-index-keyframe" : "cache"),
                CreateOperationSnapshot(
                    operationName: "seek-to-time",
                    frameIndex: seekToTimeAbsolute ? 30L : 0L,
                    isAbsolute: seekToTimeAbsolute,
                    isGlobalIndexAvailable: seekToTimeUsedGlobalIndex,
                    usedGlobalIndex: seekToTimeUsedGlobalIndex,
                    anchorStrategy: seekToTimeAnchorStrategy),
                CreateOperationSnapshot(
                    operationName: "seek-to-frame",
                    frameIndex: 42L,
                    isAbsolute: seekToFrameAbsolute,
                    isGlobalIndexAvailable: seekToFrameUsedGlobalIndex,
                    usedGlobalIndex: seekToFrameUsedGlobalIndex,
                    anchorStrategy: seekToFrameAnchorStrategy),
                CreateStepSnapshot("step-backward", 41L),
                CreateStepSnapshot("step-forward", 42L),
                string.Empty);
        }

        private static ReviewOperationSnapshot CreateOperationSnapshot(
            string operationName,
            long frameIndex,
            bool isAbsolute,
            bool isGlobalIndexAvailable,
            bool usedGlobalIndex,
            string anchorStrategy)
        {
            return new ReviewOperationSnapshot(
                operationName,
                succeeded: true,
                errorMessage: string.Empty,
                isMediaOpen: true,
                position: new ReviewPosition(TimeSpan.FromSeconds(1), frameIndex, true, isAbsolute, null, null),
                mediaInfo: CreateMediaInfo(),
                note: string.Empty,
                elapsedMilliseconds: 1d,
                isGlobalFrameIndexAvailable: isGlobalIndexAvailable,
                indexedFrameCount: isGlobalIndexAvailable ? 100L : 0L,
                usedGlobalIndex: usedGlobalIndex,
                anchorStrategy: anchorStrategy,
                anchorFrameIndex: 0L,
                activeDecodeBackend: "ffmpeg-cpu",
                isGpuActive: false,
                gpuCapabilityStatus: "no-vulkan-config",
                gpuFallbackReason: string.Empty,
                operationalQueueDepth: 1,
                sessionDecodedFrameCacheBudgetBytes: 1024L,
                decodedFrameCacheBudgetBytes: 1024L,
                budgetBand: "SinglePaneCpu",
                hostResourceClass: "Workstation32To64",
                actualBackendUsed: "ffmpeg-cpu",
                previousCachedFrameCount: 0,
                forwardCachedFrameCount: 0,
                maxPreviousCachedFrameCount: 3,
                maxForwardCachedFrameCount: 3,
                approximateCachedFrameBytes: 0L,
                hardwareFrameTransferMilliseconds: 0d,
                bgraConversionMilliseconds: 0d,
                hasAudioStream: false,
                audioPlaybackAvailable: false,
                audioPlaybackActive: false,
                lastPlaybackUsedAudioClock: false,
                lastAudioSubmittedBytes: 0L,
                audioCodecName: string.Empty,
                audioErrorMessage: string.Empty);
        }

        private static ReviewStepOperationSnapshot CreateStepSnapshot(string operationName, long frameIndex)
        {
            return new ReviewStepOperationSnapshot(
                operationName,
                FrameStepResult.Succeeded(
                    operationName.Contains("backward", StringComparison.OrdinalIgnoreCase) ? -1 : 1,
                    new ReviewPosition(TimeSpan.FromSeconds(1), frameIndex, true, true, null, null),
                    wasCacheHit: true,
                    requiredReconstruction: false,
                    message: string.Empty),
                isMediaOpen: true,
                position: new ReviewPosition(TimeSpan.FromSeconds(1), frameIndex, true, true, null, null),
                mediaInfo: CreateMediaInfo(),
                elapsedMilliseconds: 1d,
                isGlobalFrameIndexAvailable: true,
                indexedFrameCount: 100L,
                usedGlobalIndex: true,
                anchorStrategy: "cache",
                anchorFrameIndex: frameIndex,
                activeDecodeBackend: "ffmpeg-cpu",
                isGpuActive: false,
                gpuCapabilityStatus: "no-vulkan-config",
                gpuFallbackReason: string.Empty,
                operationalQueueDepth: 1,
                sessionDecodedFrameCacheBudgetBytes: 1024L,
                decodedFrameCacheBudgetBytes: 1024L,
                budgetBand: "SinglePaneCpu",
                hostResourceClass: "Workstation32To64",
                actualBackendUsed: "ffmpeg-cpu",
                previousCachedFrameCount: 0,
                forwardCachedFrameCount: 0,
                maxPreviousCachedFrameCount: 3,
                maxForwardCachedFrameCount: 3,
                approximateCachedFrameBytes: 0L,
                hardwareFrameTransferMilliseconds: 0d,
                bgraConversionMilliseconds: 0d,
                hasAudioStream: false,
                audioPlaybackAvailable: false,
                audioPlaybackActive: false,
                lastPlaybackUsedAudioClock: false,
                lastAudioSubmittedBytes: 0L,
                audioCodecName: string.Empty,
                audioErrorMessage: string.Empty);
        }

        private static VideoMediaInfo CreateMediaInfo()
        {
            return new VideoMediaInfo(
                "sample.mp4",
                TimeSpan.FromSeconds(5),
                TimeSpan.FromMilliseconds(33.333),
                30d,
                1280,
                720,
                "h264",
                0,
                30,
                1,
                1,
                30);
        }
    }
}
