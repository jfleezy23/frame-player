using System;

namespace FramePlayer.Core.Coordination
{
    public sealed class ReviewWorkspacePreparationState
    {
        public static ReviewWorkspacePreparationState Idle { get; } =
            new ReviewWorkspacePreparationState(ReviewWorkspacePreparationPhase.Idle, string.Empty);

        public ReviewWorkspacePreparationState(
            ReviewWorkspacePreparationPhase phase,
            string targetFilePath)
        {
            Phase = phase;
            TargetFilePath = targetFilePath ?? string.Empty;
        }

        public ReviewWorkspacePreparationPhase Phase { get; }

        public string TargetFilePath { get; }

        public bool IsActive
        {
            get
            {
                return Phase == ReviewWorkspacePreparationPhase.Opening ||
                       Phase == ReviewWorkspacePreparationPhase.PreparingFirstFrame;
            }
        }
    }
}
