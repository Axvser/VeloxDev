namespace VeloxDev.Adapters.Benchmarks.Headed;

// WPF 的入口：先建 Application，再在 Startup 里显示窗口并把基准跑在同一个 UI 线程上。
internal static class WpfHeadedProgram
{
    public static int Run(HeadedOptions options)
    {
        var exitCode = 0;
        var application = new System.Windows.Application
        {
            ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown,
        };

        application.Startup += (_, _) =>
        {
            var host = new WpfHeadedHost(options);
            host.Show();
            _ = RunAsync(application, host);

            async Task RunAsync(System.Windows.Application app, WpfHeadedHost headedHost)
            {
                try
                {
                    await HeadedBenchmarkCore.ExecuteAsync(options, headedHost);
                }
                catch (Exception exception)
                {
                    Console.Error.WriteLine(exception);
                    exitCode = 1;
                }
                finally
                {
                    headedHost.Dispose();
                    app.Shutdown(exitCode);
                }
            }
        };

        application.Run();
        return exitCode;
    }
}
