using System.Collections.Generic;

namespace FramePlayer.Core.Abstractions
{
    public interface IRecentFilesCatalog
    {
        IReadOnlyList<string> Load();

        void Add(string filePath);

        void Remove(string filePath);

        void Clear();
    }
}
