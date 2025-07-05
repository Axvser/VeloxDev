namespace VeloxDev.Core.ThemeSystem
{
    public delegate void ThemeChangingEventHandler(object sender, ThemeChangingEventArgs e);
    public class ThemeChangingEventArgs(Type? oldTheme, Type? newTheme) : EventArgs
    {
        public Type? OldTheme { get; } = oldTheme;
        public Type? NewTheme { get; } = newTheme;
    }
    public delegate void ThemeChangedEventHandler(object sender, ThemeChangedEventArgs e);
    public class ThemeChangedEventArgs(Type? oldTheme, Type? newTheme) : EventArgs
    {
        public Type? OldTheme { get; } = oldTheme;
        public Type? NewTheme { get; } = newTheme;
    }
}
