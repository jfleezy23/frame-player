using FramePlayer.Core.Abstractions;

namespace FramePlayer.Engines.FFmpeg
{
    public sealed class SdlAudioOutputFactory : IAudioOutputFactory
    {
        public static SdlAudioOutputFactory Instance { get; } = new SdlAudioOutputFactory();

        private SdlAudioOutputFactory()
        {
        }

        public IAudioOutput Create(int sampleRate, int channelCount, int bitsPerSample)
        {
            return new SdlAudioOutput(sampleRate, channelCount, bitsPerSample);
        }
    }
}
