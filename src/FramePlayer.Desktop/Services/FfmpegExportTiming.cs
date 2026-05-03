using System;
using FramePlayer.Core.Models;
using FramePlayer.Engines.FFmpeg;

namespace FramePlayer.Services
{
    internal static class FfmpegExportTiming
    {
        private static readonly TimeSpan MinimumFallbackFrameStep = TimeSpan.FromMilliseconds(1d);

        public static TimeSpan BuildExclusiveEndTime(
            FfmpegReviewEngine engine,
            ReviewSessionSnapshot sessionSnapshot,
            LoopPlaybackAnchorSnapshot loopOut,
            TimeSpan mediaDuration,
            out string boundaryStrategy)
        {
            if (loopOut == null)
            {
                boundaryStrategy = "missing";
                return TimeSpan.Zero;
            }

            if (engine != null &&
                loopOut.AbsoluteFrameIndex.HasValue &&
                engine.TryGetIndexedPresentationTime(loopOut.AbsoluteFrameIndex.Value + 1L, out var nextIndexedTime))
            {
                boundaryStrategy = "next-indexed-frame";
                return ClampTime(nextIndexedTime, mediaDuration);
            }

            var positionStep = sessionSnapshot != null
                ? sessionSnapshot.MediaInfo.PositionStep
                : TimeSpan.Zero;
            if (positionStep <= TimeSpan.Zero)
            {
                var framesPerSecond = sessionSnapshot != null
                    ? sessionSnapshot.MediaInfo.FramesPerSecond
                    : 0d;
                if (framesPerSecond > 0d)
                {
                    positionStep = TimeSpan.FromSeconds(1d / framesPerSecond);
                }
            }

            if (positionStep <= TimeSpan.Zero)
            {
                positionStep = MinimumFallbackFrameStep;
            }

            boundaryStrategy = "position-step";
            return ClampTime(loopOut.PresentationTime + positionStep, mediaDuration);
        }

        public static string FormatFfmpegTime(TimeSpan value)
        {
            return value.TotalSeconds.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);
        }

        public static TimeSpan ClampTime(TimeSpan value, TimeSpan mediaDuration)
        {
            if (value < TimeSpan.Zero)
            {
                return TimeSpan.Zero;
            }

            if (mediaDuration > TimeSpan.Zero && value > mediaDuration)
            {
                return mediaDuration;
            }

            return value;
        }
    }
}
