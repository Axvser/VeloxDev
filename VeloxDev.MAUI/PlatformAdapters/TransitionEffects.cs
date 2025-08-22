namespace VeloxDev.MAUI.PlatformAdapters
{
    public static class TransitionEffects
    {
        public static TransitionEffect Empty { get; set; } = new()
        {
            Duration = TimeSpan.Zero
        };
        public static TransitionEffect Theme { get; set; } = new()
        {
            Duration = TimeSpan.FromSeconds(0.46)
        };
        public static TransitionEffect Hover { get; set; } = new()
        {
            Duration = TimeSpan.FromSeconds(0.32)
        };
    }
}
