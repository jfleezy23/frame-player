using System;

namespace FramePlayer.Core.Abstractions
{
    public interface IIndexedFrameTimeResolver
    {
        bool TryGetIndexedPresentationTime(long absoluteFrameIndex, out TimeSpan presentationTime);
    }
}
