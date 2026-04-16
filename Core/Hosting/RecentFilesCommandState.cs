using System;
using System.Collections.Generic;

namespace FramePlayer.Core.Hosting
{
    public sealed class RecentFilesCommandState
    {
        public static RecentFilesCommandState Empty { get; } =
            new RecentFilesCommandState(Array.Empty<RecentFileViewState>(), false, "No recent files.");

        public RecentFilesCommandState(
            IReadOnlyList<RecentFileViewState> entries,
            bool canClear,
            string statusText)
        {
            Entries = entries ?? Array.Empty<RecentFileViewState>();
            CanClear = canClear;
            StatusText = statusText ?? string.Empty;
        }

        public IReadOnlyList<RecentFileViewState> Entries { get; }

        public bool CanClear { get; }

        public string StatusText { get; }
    }
}
