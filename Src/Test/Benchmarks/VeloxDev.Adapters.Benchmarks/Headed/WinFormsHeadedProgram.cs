namespace VeloxDev.Adapters.Benchmarks.Headed;

// WinForms 的入口：Application.Run 吃掉消息泵，基准在窗体第一次 Shown 后跑在 UI 线程上。
internal static class WinFormsHeadedProgram
{
    public static int Run(HeadedOptions options)
    {
        var exitCode = 0;
        var host = new WinFormsHeadedHost(options);
        host.Form.Shown += async (_, _) =>
        {
            try
            {
                await HeadedBenchmarkCore.ExecuteAsync(options, host);
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
                exitCode = 1;
            }
            finally
            {
                host.Dispose();
            }
        };

        Application.Run(host.Form);
        return exitCode;
    }
}
