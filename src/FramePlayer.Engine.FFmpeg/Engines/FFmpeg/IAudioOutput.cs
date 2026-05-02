using System;
using System.Threading;

namespace FramePlayer.Engines.FFmpeg
{
    internal interface IAudioOutput : IDisposable
    {
        bool IsOpen { get; }

        long SubmittedBytes { get; }

        TimeSpan PlayedDuration { get; }

        void Write(byte[] buffer, CancellationToken cancellationToken);
    }
}
