namespace VeloxDev.Adapters.Benchmarks.Headed;

// Jalium 的入口：AppBuilder 负责平台后端与消息循环；窗口在 ConfigureApplication 里建，
// 基准在 Startup 之后跑，结束时先关宿主再 Shutdown，宿主进程随 Run 返回。
internal static class JaliumHeadedProgram
{
    public static int Run(HeadedOptions options)
    {
        var exitCode = 0;
        var builder = Jalium.UI.AppBuilder.CreateBuilder(new Jalium.UI.AppBuilderSettings
        {
            Args = [],
            DisableDefaults = true,
        });

        builder.ConfigureApplication(application =>
        {
            var host = new JaliumHeadedHost(options);
            application.MainWindow = host.Window;
            application.Startup += (_, _) =>
            {
                host.Show();
                _ = RunAsync(application, host);
            };

            async Task RunAsync(Jalium.UI.Application app, JaliumHeadedHost headedHost)
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
        });

        using var app = builder.Build();
        app.Run();
        return exitCode;
    }
}
