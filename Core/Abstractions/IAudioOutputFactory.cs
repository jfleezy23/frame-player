namespace FramePlayer.Core.Abstractions
{
    public interface IAudioOutputFactory
    {
        IAudioOutput Create(int sampleRate, int channelCount, int bitsPerSample);
    }
}
