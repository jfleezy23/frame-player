using System;

namespace FramePlayer.Core.Hosting
{
    public sealed class ReviewWorkspaceViewStateChangedEventArgs : EventArgs
    {
        public ReviewWorkspaceViewStateChangedEventArgs(
            ReviewWorkspaceViewState previous,
            ReviewWorkspaceViewState current)
        {
            Previous = previous ?? ReviewWorkspaceViewState.Empty;
            Current = current ?? ReviewWorkspaceViewState.Empty;
        }

        public ReviewWorkspaceViewState Previous { get; }

        public ReviewWorkspaceViewState Current { get; }
    }
}
