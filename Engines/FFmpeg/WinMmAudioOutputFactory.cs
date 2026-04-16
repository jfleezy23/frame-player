using System;
using FramePlayer.Core.Abstractions;

namespace FramePlayer.Engines.FFmpeg
{
    internal sealed class WinMmAudioOutputFactory : IAudioOutputFactory
    {
        public static WinMmAudioOutputFactory Instance { get; } = new WinMmAudioOutputFactory();

        private WinMmAudioOutputFactory()
        {
        }

        public IAudioOutput Create(int sampleRate, int channelCount, int bitsPerSample)
        {
            return new WinMmAudioOutput(sampleRate, channelCount, bitsPerSample);
        }
    }
}
