using System;
using System.Collections.Generic;

namespace FramePlayer.Core.Models
{
    public enum LoopPlaybackTargetKind
    {
        SharedWorkspace = 0,
        PaneLocal = 1
    }

    public enum LoopPlaybackMarkerEndpoint
    {
        In = 0,
        Out = 1
    }

    public sealed class LoopPlaybackFrameIdentitySnapshot
    {
        public static readonly LoopPlaybackFrameIdentitySnapshot Empty = new LoopPlaybackFrameIdentitySnapshot(null, false, null, null);

        public LoopPlaybackFrameIdentitySnapshot(
            long? absoluteFrameIndex,
            bool isFrameIndexAbsolute,
            long? presentationTimestamp,
            long? decodeTimestamp)
        {
            AbsoluteFrameIndex = absoluteFrameIndex.HasValue
                ? Math.Max(0L, absoluteFrameIndex.Value)
                : (long?)null;
            IsFrameIndexAbsolute = isFrameIndexAbsolute && AbsoluteFrameIndex.HasValue;
            PresentationTimestamp = presentationTimestamp;
            DecodeTimestamp = decodeTimestamp;
        }

        public long? AbsoluteFrameIndex { get; }

        public bool IsFrameIndexAbsolute { get; }

        public long? PresentationTimestamp { get; }

        public long? DecodeTimestamp { get; }
    }

    public sealed class LoopPlaybackAnchorSnapshot
    {
        public LoopPlaybackAnchorSnapshot(
            string paneId,
            string sessionId,
            string displayLabel,
            TimeSpan presentationTime,
            LoopPlaybackFrameIdentitySnapshot frameIdentity)
        {
            PaneId = paneId ?? string.Empty;
            SessionId = sessionId ?? string.Empty;
            DisplayLabel = displayLabel ?? string.Empty;
            PresentationTime = presentationTime < TimeSpan.Zero ? TimeSpan.Zero : presentationTime;
            FrameIdentity = frameIdentity ?? LoopPlaybackFrameIdentitySnapshot.Empty;
        }

        public string PaneId { get; }

        public string SessionId { get; }

        public string DisplayLabel { get; }

        public TimeSpan PresentationTime { get; }

        public LoopPlaybackFrameIdentitySnapshot FrameIdentity { get; }

        public long? AbsoluteFrameIndex
        {
            get { return FrameIdentity.AbsoluteFrameIndex; }
        }

        public bool IsFrameIndexAbsolute
        {
            get { return FrameIdentity.IsFrameIndexAbsolute; }
        }

        public long? PresentationTimestamp
        {
            get { return FrameIdentity.PresentationTimestamp; }
        }

        public long? DecodeTimestamp
        {
            get { return FrameIdentity.DecodeTimestamp; }
        }

        public bool HasAbsoluteFrameIdentity
        {
            get { return AbsoluteFrameIndex.HasValue && IsFrameIndexAbsolute; }
        }

        public bool IsPending
        {
            get { return !HasAbsoluteFrameIdentity; }
        }
    }

    public sealed class LoopPlaybackPaneRangeSnapshot
    {
        public LoopPlaybackPaneRangeSnapshot(
            string paneId,
            string sessionId,
            string displayLabel,
            string currentFilePath,
            TimeSpan duration,
            LoopPlaybackAnchorSnapshot loopIn,
            LoopPlaybackAnchorSnapshot loopOut)
        {
            PaneId = paneId ?? string.Empty;
            SessionId = sessionId ?? string.Empty;
            DisplayLabel = displayLabel ?? string.Empty;
            CurrentFilePath = currentFilePath ?? string.Empty;
            Duration = duration > TimeSpan.Zero ? duration : TimeSpan.Zero;
            LoopIn = loopIn;
            LoopOut = loopOut;
        }

        public string PaneId { get; }

        public string SessionId { get; }

        public string DisplayLabel { get; }

        public string CurrentFilePath { get; }

        public TimeSpan Duration { get; }

        public LoopPlaybackAnchorSnapshot LoopIn { get; }

        public LoopPlaybackAnchorSnapshot LoopOut { get; }

        public bool HasLoopIn
        {
            get { return LoopIn != null; }
        }

        public bool HasLoopOut
        {
            get { return LoopOut != null; }
        }

        public bool HasAnyMarkers
        {
            get { return HasLoopIn || HasLoopOut; }
        }

        public bool HasPendingMarkers
        {
            get
            {
                return (LoopIn != null && LoopIn.IsPending) ||
                       (LoopOut != null && LoopOut.IsPending);
            }
        }

        public bool IsInvalidRange
        {
            get
            {
                return LoopIn != null &&
                       LoopOut != null &&
                       LoopOut.PresentationTime < LoopIn.PresentationTime;
            }
        }

        public TimeSpan EffectiveStartTime
        {
            get { return LoopIn != null ? LoopIn.PresentationTime : TimeSpan.Zero; }
        }

        public TimeSpan EffectiveEndTime
        {
            get
            {
                if (LoopOut != null)
                {
                    return Duration > TimeSpan.Zero && LoopOut.PresentationTime > Duration
                        ? Duration
                        : LoopOut.PresentationTime;
                }

                return Duration > TimeSpan.Zero ? Duration : TimeSpan.Zero;
            }
        }

        public bool TryGetRestartFrameIndex(out long frameIndex)
        {
            if (LoopIn == null)
            {
                frameIndex = 0L;
                return true;
            }

            if (LoopIn.HasAbsoluteFrameIdentity)
            {
                frameIndex = Math.Max(0L, LoopIn.AbsoluteFrameIndex.GetValueOrDefault());
                return true;
            }

            frameIndex = 0L;
            return false;
        }
    }

    public sealed class LoopPlaybackRangeSnapshot
    {
        public static LoopPlaybackRangeSnapshot Empty { get; } =
            new LoopPlaybackRangeSnapshot(
                LoopPlaybackTargetKind.SharedWorkspace,
                Array.Empty<LoopPlaybackPaneRangeSnapshot>());

        public LoopPlaybackRangeSnapshot(
            LoopPlaybackTargetKind targetKind,
            IReadOnlyList<LoopPlaybackPaneRangeSnapshot> paneRanges)
        {
            TargetKind = targetKind;
            PaneRanges = paneRanges ?? Array.Empty<LoopPlaybackPaneRangeSnapshot>();
        }

        public LoopPlaybackTargetKind TargetKind { get; }

        public IReadOnlyList<LoopPlaybackPaneRangeSnapshot> PaneRanges { get; }

        public int TargetPaneCount
        {
            get { return PaneRanges.Count; }
        }

        public bool HasMarkers
        {
            get
            {
                for (var index = 0; index < PaneRanges.Count; index++)
                {
                    var paneRange = PaneRanges[index];
                    if (paneRange != null && paneRange.HasAnyMarkers)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        public bool TryGetPaneRange(string paneId, out LoopPlaybackPaneRangeSnapshot paneRange)
        {
            for (var index = 0; index < PaneRanges.Count; index++)
            {
                paneRange = PaneRanges[index];
                if (paneRange != null &&
                    string.Equals(paneRange.PaneId, paneId, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            paneRange = null;
            return false;
        }
    }
}
