namespace VeloxDev.TransitionSystem
{
    public static class TransitionEffects
    {
        /// <summary>An effect with zero duration: values snap without interpolation.</summary>
        public static TransitionEffect Empty { get; set; } = new()
        {
            Duration = TimeSpan.Zero
        };
        /// <summary>The default effect applied when the theme changes.</summary>
        public static TransitionEffect Theme { get; set; } = new()
        {
            Duration = TimeSpan.FromSeconds(0.46)
        };
        /// <summary>The default effect applied on pointer hover.</summary>
        public static TransitionEffect Hover { get; set; } = new()
        {
            Duration = TimeSpan.FromSeconds(0.32)
        };
    }
}
