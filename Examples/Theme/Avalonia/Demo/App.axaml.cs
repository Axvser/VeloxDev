using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Demo.Views;
using VeloxDev.DynamicTheme;
using VeloxDev.TransitionSystem;

namespace Demo;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Both of these are global and must be in place before any element registers itself or any switch runs:
            // the interpolator is what lets an animated switch run on the platform's scheduler at all, and StartModel
            // decides whether a switch animates from the cached theme value or from the live property.
            ThemeManager.SetPlatformInterpolator(new Interpolator());
            ThemeManager.StartModel = StartModel.Cache;

            // Set before any element registers itself, so the values each one applies as it initialises are the
            // ones for this theme.
            ThemeManager.SetCurrent<Dark>();

            if (desktop.Args?.Any(static arg => string.Equals(arg, "bench", StringComparison.OrdinalIgnoreCase)) == true)
            {
                // No window to close, so the run itself decides when the process ends.
                desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                _ = BenchRunner.RunAsync(() => desktop.Shutdown());
            }
            else
            {
                desktop.MainWindow = new MainWindow();
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}
