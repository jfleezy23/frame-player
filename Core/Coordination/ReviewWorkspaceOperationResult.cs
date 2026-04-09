using System;
using System.Collections.Generic;

namespace FramePlayer.Core.Coordination
{
    public sealed class ReviewWorkspaceOperationResult
    {
        public ReviewWorkspaceOperationResult(
            string operationName,
            SynchronizedOperationScope operationScope,
            string focusedPaneId,
            IReadOnlyList<ReviewWorkspacePaneOperationResult> paneResults)
        {
            OperationName = operationName ?? string.Empty;
            OperationScope = operationScope;
            FocusedPaneId = focusedPaneId ?? string.Empty;
            PaneResults = paneResults ?? Array.Empty<ReviewWorkspacePaneOperationResult>();
        }

        public string OperationName { get; }

        public SynchronizedOperationScope OperationScope { get; }

        public string FocusedPaneId { get; }

        public IReadOnlyList<ReviewWorkspacePaneOperationResult> PaneResults { get; }

        public int PaneCount
        {
            get { return PaneResults.Count; }
        }

        public bool Succeeded
        {
            get
            {
                for (var index = 0; index < PaneResults.Count; index++)
                {
                    var paneResult = PaneResults[index];
                    if (paneResult != null && paneResult.Failed)
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        public int AttemptedPaneCount
        {
            get
            {
                var count = 0;
                for (var index = 0; index < PaneResults.Count; index++)
                {
                    var paneResult = PaneResults[index];
                    if (paneResult != null && paneResult.WasAttempted)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        public int SucceededPaneCount
        {
            get
            {
                var count = 0;
                for (var index = 0; index < PaneResults.Count; index++)
                {
                    var paneResult = PaneResults[index];
                    if (paneResult != null && paneResult.Succeeded)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        public int FailedPaneCount
        {
            get
            {
                var count = 0;
                for (var index = 0; index < PaneResults.Count; index++)
                {
                    var paneResult = PaneResults[index];
                    if (paneResult != null && paneResult.Failed)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        public int SkippedPaneCount
        {
            get
            {
                var count = 0;
                for (var index = 0; index < PaneResults.Count; index++)
                {
                    var paneResult = PaneResults[index];
                    if (paneResult != null && paneResult.Skipped)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        public bool HasFailures
        {
            get { return FailedPaneCount > 0; }
        }

        public bool HasExceptionalFailures
        {
            get { return FirstExceptionalFailurePaneResult != null; }
        }

        public ReviewWorkspacePaneOperationResult FocusedPaneResult
        {
            get
            {
                return FindPaneResult(FocusedPaneId) ??
                       (PaneCount > 0 ? PaneResults[0] : null);
            }
        }

        public bool TryGetPaneResult(string paneId, out ReviewWorkspacePaneOperationResult paneResult)
        {
            paneResult = FindPaneResult(paneId);
            return paneResult != null;
        }

        public ReviewWorkspacePaneOperationResult FirstFailedPaneResult
        {
            get
            {
                for (var index = 0; index < PaneResults.Count; index++)
                {
                    var paneResult = PaneResults[index];
                    if (paneResult != null && paneResult.Failed)
                    {
                        return paneResult;
                    }
                }

                return null;
            }
        }

        public ReviewWorkspacePaneOperationResult FirstExceptionalFailurePaneResult
        {
            get
            {
                for (var index = 0; index < PaneResults.Count; index++)
                {
                    var paneResult = PaneResults[index];
                    if (paneResult != null &&
                        paneResult.Failed &&
                        paneResult.FailureException != null)
                    {
                        return paneResult;
                    }
                }

                return null;
            }
        }

        private ReviewWorkspacePaneOperationResult FindPaneResult(string paneId)
        {
            for (var index = 0; index < PaneResults.Count; index++)
            {
                var paneResult = PaneResults[index];
                if (paneResult != null &&
                    string.Equals(paneResult.PaneId, paneId, StringComparison.Ordinal))
                {
                    return paneResult;
                }
            }

            return null;
        }
    }
}
