using System;
using System.Collections.Generic;
using System.Linq;

namespace FramePlayer.Avalonia.Views
{
    internal sealed class VideoInfoSnapshot
    {
        public string WindowTitle { get; set; } = string.Empty;
        public string PaneLabel { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public VideoInfoSection SummarySection { get; set; } = VideoInfoSection.Empty;
        public VideoInfoSection VideoSection { get; set; } = VideoInfoSection.Empty;
        public VideoInfoSection AudioSection { get; set; } = VideoInfoSection.Empty;
        public VideoInfoSection AdvancedSection { get; set; } = VideoInfoSection.Empty;
    }

    internal sealed class VideoInfoSection
    {
        public static VideoInfoSection Empty { get; } = new VideoInfoSection(Array.Empty<VideoInfoField>());

        public VideoInfoSection(IEnumerable<VideoInfoField> fields, string? emptyMessage = null)
        {
            Fields = fields == null
                ? Array.Empty<VideoInfoField>()
                : fields.Where(field => field != null).ToArray();
            EmptyMessage = emptyMessage ?? string.Empty;
        }

        public VideoInfoField[] Fields { get; }
        public string EmptyMessage { get; }
        public bool HasFields => Fields.Length > 0;
    }

    internal sealed class VideoInfoField
    {
        public VideoInfoField(string label, string value)
        {
            Label = label ?? string.Empty;
            Value = value ?? string.Empty;
        }

        public string Label { get; }
        public string Value { get; }
    }
}
