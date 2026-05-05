using Avalonia;
using System;

namespace VeloxDev.Docs.Desktop
{
    internal sealed class Program
    {
        // Initialization code. Don't use any Avalonia, third-party APIs or any
        // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
        // yet and stuff might break.
        [STAThread]
        public static void Main(string[] args) => BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);

        // Avalonia configuration, don't remove; also used by visual designer.
        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .WithSystemFontSource(new Uri("avares://VeloxDev.Docs/Assets/Fonts/NotoColorEmoji.ttf"))
                .UsePlatformDetect()
#if DEBUG
                .WithDeveloperTools()
#endif
                .WithInterFont()
                .LogToTrace();
    }
}
