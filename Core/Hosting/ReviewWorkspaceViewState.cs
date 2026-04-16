using System;
using System.Collections.Generic;

namespace FramePlayer.Core.Hosting
{
    public sealed class ReviewWorkspaceViewState
    {
        public static ReviewWorkspaceViewState Empty { get; } =
            new ReviewWorkspaceViewState(
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                TransportCommandState.Disabled,
                LoopCommandState.Empty,
                ExportCommandState.Empty,
                Array.Empty<PaneViewState>());

        public ReviewWorkspaceViewState(
            string primaryPaneId,
            string activePaneId,
            string focusedPaneId,
            string currentFilePath,
            string playbackMessage,
            string mediaSummary,
            TransportCommandState transport,
            LoopCommandState loop,
            ExportCommandState export,
            IReadOnlyList<PaneViewState> panes)
        {
            PrimaryPaneId = primaryPaneId ?? string.Empty;
            ActivePaneId = activePaneId ?? string.Empty;
            FocusedPaneId = focusedPaneId ?? string.Empty;
            CurrentFilePath = currentFilePath ?? string.Empty;
            PlaybackMessage = playbackMessage ?? string.Empty;
            MediaSummary = mediaSummary ?? string.Empty;
            Transport = transport ?? TransportCommandState.Disabled;
            Loop = loop ?? LoopCommandState.Empty;
            Export = export ?? ExportCommandState.Empty;
            Panes = panes ?? Array.Empty<PaneViewState>();
        }

        public string PrimaryPaneId { get; }

        public string ActivePaneId { get; }

        public string FocusedPaneId { get; }

        public string CurrentFilePath { get; }

        public string PlaybackMessage { get; }

        public string MediaSummary { get; }

        public TransportCommandState Transport { get; }

        public LoopCommandState Loop { get; }

        public ExportCommandState Export { get; }

        public IReadOnlyList<PaneViewState> Panes { get; }
    }
}
