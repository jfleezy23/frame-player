using System;
using System.Collections.Generic;

namespace FramePlayer.Core.Coordination
{
    public sealed class ReviewWorkspaceSnapshot
    {
        public static ReviewWorkspaceSnapshot Empty { get; } =
            new ReviewWorkspaceSnapshot(
                TimeSpan.Zero,
                TimelineSynchronizationMode.Independent,
                SynchronizedOperationScope.FocusedPane,
                string.Empty,
                string.Empty,
                string.Empty,
                Array.Empty<ReviewWorkspacePaneSnapshot>());

        public ReviewWorkspaceSnapshot(
            TimeSpan masterTimelinePosition,
            TimelineSynchronizationMode synchronizationMode,
            SynchronizedOperationScope defaultOperationScope,
            string primaryPaneId,
            string activePaneId,
            string focusedPaneId,
            IReadOnlyList<ReviewWorkspacePaneSnapshot> panes)
        {
            MasterTimelinePosition = masterTimelinePosition;
            SynchronizationMode = synchronizationMode;
            DefaultOperationScope = defaultOperationScope;
            PrimaryPaneId = primaryPaneId ?? string.Empty;
            ActivePaneId = activePaneId ?? string.Empty;
            FocusedPaneId = focusedPaneId ?? string.Empty;
            Panes = panes ?? Array.Empty<ReviewWorkspacePaneSnapshot>();
        }

        public TimeSpan MasterTimelinePosition { get; }

        public TimelineSynchronizationMode SynchronizationMode { get; }

        public SynchronizedOperationScope DefaultOperationScope { get; }

        public string PrimaryPaneId { get; }

        public string ActivePaneId { get; }

        public string FocusedPaneId { get; }

        public IReadOnlyList<ReviewWorkspacePaneSnapshot> Panes { get; }

        public int PaneCount
        {
            get { return Panes.Count; }
        }

        public ReviewWorkspacePaneSnapshot FocusedPane
        {
            get
            {
                return FindPane(
                    pane => pane.IsFocused || string.Equals(pane.PaneId, FocusedPaneId, StringComparison.Ordinal),
                    pane => pane.IsActive,
                    pane => pane.IsPrimary);
            }
        }

        public ReviewWorkspacePaneSnapshot ActivePane
        {
            get
            {
                return FindPane(
                    pane => pane.IsActive || string.Equals(pane.PaneId, ActivePaneId, StringComparison.Ordinal),
                    pane => pane.IsFocused,
                    pane => pane.IsPrimary);
            }
        }

        public ReviewWorkspacePaneSnapshot PrimaryPane
        {
            get
            {
                return FindPane(
                    pane => pane.IsPrimary || string.Equals(pane.PaneId, PrimaryPaneId, StringComparison.Ordinal));
            }
        }

        public bool TryGetPane(string paneId, out ReviewWorkspacePaneSnapshot pane)
        {
            pane = FindExactPane(paneId);
            return pane != null;
        }

        private ReviewWorkspacePaneSnapshot FindExactPane(string paneId)
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

        private ReviewWorkspacePaneSnapshot FindPane(params Func<ReviewWorkspacePaneSnapshot, bool>[] matchers)
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
