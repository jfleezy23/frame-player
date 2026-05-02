using System;
using System.Diagnostics.CodeAnalysis;
using FramePlayer.Core.Models;

namespace FramePlayer.Core.Coordination
{
    public sealed class ReviewWorkspacePaneOperationResult
    {
        [SuppressMessage(
            "Major Code Smell",
            "S107:Methods should not have too many parameters",
            Justification = "Pane operation results are immutable coordination snapshots; explicit scalar fields keep failure reporting and tests straightforward.")]
        public ReviewWorkspacePaneOperationResult(
            string paneId,
            string sessionId,
            string displayLabel,
            bool wasTargeted,
            bool wasAttempted,
            ReviewWorkspacePaneOperationOutcome outcomeStatus,
            ReviewSessionSnapshot session,
            string failureDetail,
            string failureExceptionType,
            FrameStepResult frameStepResult,
            Exception failureException)
        {
            PaneId = paneId ?? string.Empty;
            SessionId = sessionId ?? string.Empty;
            DisplayLabel = displayLabel ?? string.Empty;
            WasTargeted = wasTargeted;
            WasAttempted = wasAttempted;
            OutcomeStatus = outcomeStatus;
            Session = session ?? ReviewSessionSnapshot.Empty;
            FailureDetail = failureDetail ?? string.Empty;
            FailureExceptionType = failureExceptionType ?? string.Empty;
            FrameStepResult = frameStepResult;
            FailureException = failureException;
        }

        public string PaneId { get; }

        public string SessionId { get; }

        public string DisplayLabel { get; }

        public bool WasTargeted { get; }

        public bool WasAttempted { get; }

        public ReviewWorkspacePaneOperationOutcome OutcomeStatus { get; }

        public ReviewSessionSnapshot Session { get; }

        public string FailureDetail { get; }

        public string FailureExceptionType { get; }

        public FrameStepResult FrameStepResult { get; }

        public ReviewPosition Position
        {
            get { return Session.Position; }
        }

        public bool Succeeded
        {
            get { return OutcomeStatus == ReviewWorkspacePaneOperationOutcome.Succeeded; }
        }

        public bool Failed
        {
            get { return OutcomeStatus == ReviewWorkspacePaneOperationOutcome.Failed; }
        }

        public bool Skipped
        {
            get { return OutcomeStatus == ReviewWorkspacePaneOperationOutcome.Skipped; }
        }

        internal Exception FailureException { get; }
    }
}
