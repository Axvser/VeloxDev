using System.Windows;
using VeloxDev.DynamicTheme;
using VeloxDev.TransitionSystem;

namespace Demo;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 两项都是全局的，必须在任何元素注册、任何切换开始之前就位：插值器让动画切换能跑在平台调度器上，
        // StartModel 决定切换从缓存主题值还是从实时属性起动画。
        ThemeManager.SetPlatformInterpolator(new Interpolator());
        ThemeManager.StartModel = StartModel.Cache;

        // 在任何元素注册之前设定，元素初始化时应用的就是本主题的值。
        ThemeManager.SetCurrent<Dark>();

        if (e.Args.Any(static arg => string.Equals(arg, "bench", StringComparison.OrdinalIgnoreCase)))
        {
            _ = BenchRunner.RunAsync(() => Shutdown());
            return;
        }

        new MainWindow().Show();
    }
}
