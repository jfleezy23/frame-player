using System;
using FramePlayer.Core.Abstractions;

namespace FramePlayer.Core.Models
{
    public sealed class ClipExportRequest
    {
        public ClipExportRequest(
            string sourceFilePath,
            string outputFilePath,
            string displayLabel,
            string paneId,
            bool isPaneLocal,
            ReviewSessionSnapshot sessionSnapshot,
            LoopPlaybackPaneRangeSnapshot loopRange,
            IIndexedFrameTimeResolver indexedFrameTimeResolver)
        {
            SourceFilePath = sourceFilePath ?? string.Empty;
            OutputFilePath = outputFilePath ?? string.Empty;
            DisplayLabel = displayLabel ?? string.Empty;
            PaneId = paneId ?? string.Empty;
            IsPaneLocal = isPaneLocal;
            SessionSnapshot = sessionSnapshot ?? ReviewSessionSnapshot.Empty;
            LoopRange = loopRange;
            IndexedFrameTimeResolver = indexedFrameTimeResolver;
        }

        public string SourceFilePath { get; }

        public string OutputFilePath { get; }

        public string DisplayLabel { get; }

        public string PaneId { get; }

        public bool IsPaneLocal { get; }

        public ReviewSessionSnapshot SessionSnapshot { get; }

        public LoopPlaybackPaneRangeSnapshot LoopRange { get; }

        public IIndexedFrameTimeResolver IndexedFrameTimeResolver { get; }
    }
}
