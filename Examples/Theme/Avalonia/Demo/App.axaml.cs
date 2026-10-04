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
            // 两项都是全局的，必须在任何元素注册、任何切换开始之前就位：插值器让动画切换能跑在平台调度器上，
            // StartModel 决定切换从缓存主题值还是从实时属性起动画。
            ThemeManager.SetPlatformInterpolator(new Interpolator());
            ThemeManager.StartModel = StartModel.Cache;

            // 在任何元素注册之前设定，元素初始化时应用的就是本主题的值。
            ThemeManager.SetCurrent<Dark>();

            if (desktop.Args?.Any(static arg => string.Equals(arg, "bench", StringComparison.OrdinalIgnoreCase)) == true)
            {
                // 没有窗口可关，由这次运行自己决定进程何时结束。
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
