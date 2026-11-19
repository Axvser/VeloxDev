using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;

namespace VeloxDev.Adapters.Benchmarks.Headed;

// Avalonia 的入口：AppBuilder 自己吃掉进程生命周期，所以宿主在框架初始化完成后 Post 到 UI 线程跑。
internal static class AvaloniaHeadedProgram
{
    private static HeadedOptions? s_options;

    public static int Run(HeadedOptions options)
    {
        s_options = options;
        AppBuilder.Configure<AvaloniaHeadedApplication>()
            .UsePlatformDetect()
            .LogToTrace()
            .StartWithClassicDesktopLifetime([]);
        return AvaloniaHeadedApplication.ExitCode;
    }

    internal static HeadedOptions Options
        => s_options ?? throw new InvalidOperationException("The Avalonia headed options were not parsed.");
}

internal sealed class AvaloniaHeadedApplication : Avalonia.Application
{
    public static int ExitCode;

    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var options = AvaloniaHeadedProgram.Options;
            var host = new AvaloniaHeadedHost(options);
            desktop.MainWindow = host.Window;
            Dispatcher.UIThread.Post(async () =>
            {
                try
                {
                    await HeadedBenchmarkCore.ExecuteAsync(options, host);
                }
                catch (Exception exception)
                {
                    Console.Error.WriteLine(exception);
                    ExitCode = 1;
                }
                finally
                {
                    host.Dispose();
                    desktop.Shutdown();
                }
            });
        }

        base.OnFrameworkInitializationCompleted();
    }
}
