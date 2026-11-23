namespace VeloxDev.Adapters.Benchmarks.Headed;

// 有头基准的分发口：一次进程只选一家，别家宿主不初始化。
internal static class HeadedBenchmarkRunner
{
    public static void Run(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        HeadedOptions options;
        try
        {
            options = HeadedOptions.Parse(args);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            Environment.ExitCode = 2;
            return;
        }

        try
        {
            Environment.ExitCode = options.Adapter switch
            {
                "avalonia" => AvaloniaHeadedProgram.Run(options),
                "wpf" => WpfHeadedProgram.Run(options),
                "jalium" => JaliumHeadedProgram.Run(options),
                _ => UnknownAdapter(options.Adapter),
            };
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            Environment.ExitCode = 1;
        }
    }

    private static int UnknownAdapter(string adapter)
    {
        Console.Error.WriteLine(
            $"No headed host is registered for '{adapter}'. Available: avalonia, wpf, jalium.\n" +
            "WinUI, MAUI and Razor need their own executable host that references VeloxDev.Adapters.Benchmarks.Headed.\n" +
            HeadedOptions.Usage);
        return 2;
    }
}
