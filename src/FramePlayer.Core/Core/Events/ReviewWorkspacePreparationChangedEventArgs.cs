using System;
using FramePlayer.Core.Coordination;

namespace FramePlayer.Core.Events
{
    public sealed class ReviewWorkspacePreparationChangedEventArgs : EventArgs
    {
        public ReviewWorkspacePreparationChangedEventArgs(
            ReviewWorkspacePreparationState previousState,
            ReviewWorkspacePreparationState currentState)
        {
            PreviousState = previousState ?? ReviewWorkspacePreparationState.Idle;
            CurrentState = currentState ?? ReviewWorkspacePreparationState.Idle;
        }

        public ReviewWorkspacePreparationState PreviousState { get; }

        public ReviewWorkspacePreparationState CurrentState { get; }
    }
}
