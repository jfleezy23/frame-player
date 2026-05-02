namespace FramePlayer.Services
{
    public sealed class AppPreferences
    {
        public static AppPreferences Default { get; } = new AppPreferences(true);

        public AppPreferences(bool useGpuAcceleration)
        {
            UseGpuAcceleration = useGpuAcceleration;
        }

        public bool UseGpuAcceleration { get; }
    }
}
