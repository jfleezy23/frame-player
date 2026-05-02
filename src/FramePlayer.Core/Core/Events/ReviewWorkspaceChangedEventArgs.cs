using System;
using FramePlayer.Core.Coordination;

namespace FramePlayer.Core.Events
{
    public sealed class ReviewWorkspaceChangedEventArgs : EventArgs
    {
        public ReviewWorkspaceChangedEventArgs(
            MultiVideoWorkspaceState previousWorkspace,
            MultiVideoWorkspaceState currentWorkspace)
        {
            PreviousWorkspace = previousWorkspace ?? MultiVideoWorkspaceState.Empty;
            CurrentWorkspace = currentWorkspace ?? MultiVideoWorkspaceState.Empty;
        }

        public MultiVideoWorkspaceState PreviousWorkspace { get; }

        public MultiVideoWorkspaceState CurrentWorkspace { get; }
    }
}
