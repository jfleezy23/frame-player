using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using FramePlayer.Core.Models;

namespace FramePlayer.Core.Coordination
{
    // Neutral workspace snapshot for future synchronized multi-video review hosts.
    public sealed class MultiVideoWorkspaceState
    {
        public static MultiVideoWorkspaceState Empty { get; } =
            new MultiVideoWorkspaceState(
                TimeSpan.Zero,
                TimelineSynchronizationMode.Independent,
                SynchronizedOperationScope.FocusedPane,
                string.Empty,
                string.Empty,
                string.Empty,
                LoopPlaybackRangeSnapshot.Empty,
                Array.Empty<ReviewPaneState>());

        [SuppressMessage(
            "Major Code Smell",
            "S107:Methods should not have too many parameters",
            Justification = "Workspace state snapshots intentionally keep scalar identifiers and pane collections explicit so coordination logic stays transparent.")]
        public MultiVideoWorkspaceState(
            TimeSpan masterTimelinePosition,
            TimelineSynchronizationMode synchronizationMode,
            SynchronizedOperationScope defaultOperationScope,
            string primaryPaneId,
            string activePaneId,
            string focusedPaneId,
            LoopPlaybackRangeSnapshot sharedLoopRange,
            IReadOnlyList<ReviewPaneState> panes)
        {
            MasterTimelinePosition = masterTimelinePosition;
            SynchronizationMode = synchronizationMode;
            DefaultOperationScope = defaultOperationScope;
            PrimaryPaneId = primaryPaneId ?? string.Empty;
            ActivePaneId = activePaneId ?? string.Empty;
            FocusedPaneId = focusedPaneId ?? string.Empty;
            SharedLoopRange = sharedLoopRange ?? LoopPlaybackRangeSnapshot.Empty;
            Panes = panes ?? Array.Empty<ReviewPaneState>();
        }

        public TimeSpan MasterTimelinePosition { get; }

        public TimelineSynchronizationMode SynchronizationMode { get; }

        public SynchronizedOperationScope DefaultOperationScope { get; }

        public string PrimaryPaneId { get; }

        public string ActivePaneId { get; }

        public string FocusedPaneId { get; }

        public LoopPlaybackRangeSnapshot SharedLoopRange { get; }

        public IReadOnlyList<ReviewPaneState> Panes { get; }

        public int PaneCount
        {
            get { return Panes.Count; }
        }

        public ReviewPaneState FocusedPane
        {
            get
            {
                return FindPane(
                    pane => pane.IsFocused || string.Equals(pane.PaneId, FocusedPaneId, StringComparison.Ordinal),
                    pane => pane.IsActive,
                    pane => pane.IsPrimary);
            }
        }

        public ReviewPaneState ActivePane
        {
            get
            {
                return FindPane(
                    pane => pane.IsActive || string.Equals(pane.PaneId, ActivePaneId, StringComparison.Ordinal),
                    pane => pane.IsFocused,
                    pane => pane.IsPrimary);
            }
        }

        public ReviewPaneState PrimaryPane
        {
            get
            {
                return FindPane(
                    pane => pane.IsPrimary || string.Equals(pane.PaneId, PrimaryPaneId, StringComparison.Ordinal));
            }
        }

        public ReviewSessionSnapshot FocusedSession
        {
            get
            {
                return FocusedPane != null
                    ? FocusedPane.Session
                    : ReviewSessionSnapshot.Empty;
            }
        }

        public ReviewSessionSnapshot ActiveSession
        {
            get
            {
                return ActivePane != null
                    ? ActivePane.Session
                    : ReviewSessionSnapshot.Empty;
            }
        }

        public bool TryGetPane(string paneId, out ReviewPaneState pane)
        {
            pane = FindExactPane(paneId);
            return pane != null;
        }

        private ReviewPaneState FindExactPane(string paneId)
        {
            for (var paneIndex = 0; paneIndex < Panes.Count; paneIndex++)
            {
                var pane = Panes[paneIndex];
                if (pane != null &&
                    string.Equals(pane.PaneId, paneId, StringComparison.Ordinal))
                {
                    return pane;
                }
            }

            return null;
        }

        private ReviewPaneState FindPane(params Func<ReviewPaneState, bool>[] matchers)
        {
            if (matchers != null)
            {
                for (var matcherIndex = 0; matcherIndex < matchers.Length; matcherIndex++)
                {
                    var matcher = matchers[matcherIndex];
                    if (matcher == null)
                    {
                        continue;
                    }

                    for (var paneIndex = 0; paneIndex < Panes.Count; paneIndex++)
                    {
                        var pane = Panes[paneIndex];
                        if (pane != null && matcher(pane))
                        {
                            return pane;
                        }
                    }
                }
            }

            return PaneCount > 0 ? Panes[0] : null;
        }
    }
}
