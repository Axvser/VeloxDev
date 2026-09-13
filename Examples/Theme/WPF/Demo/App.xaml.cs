using System.Windows;
using VeloxDev.DynamicTheme;
using VeloxDev.TransitionSystem;

namespace Demo;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Both of these are global and must be in place before any element registers itself or any switch runs:
        // the interpolator is what lets an animated switch run on the platform's scheduler at all, and StartModel
        // decides whether a switch animates from the cached theme value or from the live property.
        ThemeManager.SetPlatformInterpolator(new Interpolator());
        ThemeManager.StartModel = StartModel.Cache;

        // Set before any element registers itself, so the values each one applies as it initialises are the ones
        // for this theme.
        ThemeManager.SetCurrent<Dark>();

        if (e.Args.Any(static arg => string.Equals(arg, "bench", StringComparison.OrdinalIgnoreCase)))
        {
            _ = BenchRunner.RunAsync(() => Shutdown());
            return;
        }

        new MainWindow().Show();
    }
}
