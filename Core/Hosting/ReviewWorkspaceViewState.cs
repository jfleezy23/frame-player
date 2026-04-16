using System;
using System.Collections.Generic;

namespace FramePlayer.Core.Hosting
{
    public sealed class ReviewWorkspaceViewState
    {
        public ReviewWorkspaceViewState()
        {
            PrimaryPaneId = string.Empty;
            ActivePaneId = string.Empty;
            FocusedPaneId = string.Empty;
            CurrentFilePath = string.Empty;
            PlaybackMessage = string.Empty;
            MediaSummary = string.Empty;
            Transport = TransportCommandState.Disabled;
            Loop = LoopCommandState.Empty;
            Export = ExportCommandState.Empty;
            RecentFiles = RecentFilesCommandState.Empty;
            Panes = Array.Empty<PaneViewState>();
        }

        public static ReviewWorkspaceViewState Empty
        {
            get { return new ReviewWorkspaceViewState(); }
        }

        public string PrimaryPaneId { get; set; }

        public string ActivePaneId { get; set; }

        public string FocusedPaneId { get; set; }

        public string CurrentFilePath { get; set; }

        public string PlaybackMessage { get; set; }

        public string MediaSummary { get; set; }

        public TransportCommandState Transport { get; set; }

        public LoopCommandState Loop { get; set; }

        public ExportCommandState Export { get; set; }

        public RecentFilesCommandState RecentFiles { get; set; }

        public IReadOnlyList<PaneViewState> Panes { get; set; }
    }
}
