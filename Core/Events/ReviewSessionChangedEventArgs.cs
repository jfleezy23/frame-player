using System;
using FramePlayer.Core.Models;

namespace FramePlayer.Core.Events
{
    public sealed class ReviewSessionChangedEventArgs : EventArgs
    {
        public ReviewSessionChangedEventArgs(
            ReviewSessionSnapshot previousSession,
            ReviewSessionSnapshot currentSession)
        {
            PreviousSession = previousSession ?? ReviewSessionSnapshot.Empty;
            CurrentSession = currentSession ?? ReviewSessionSnapshot.Empty;
        }

        public ReviewSessionSnapshot PreviousSession { get; }

        public ReviewSessionSnapshot CurrentSession { get; }
    }
}
