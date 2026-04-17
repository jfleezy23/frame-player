namespace FramePlayer.Services
{
    internal sealed class AppPreferences
    {
        public static AppPreferences Default { get; } = new AppPreferences(false);

        public AppPreferences(bool useGpuAcceleration)
        {
            UseGpuAcceleration = useGpuAcceleration;
        }

        public bool UseGpuAcceleration { get; }
    }
}
