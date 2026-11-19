using VeloxDev.Adapters.Benchmarks.Headed;

namespace VeloxDev.Adapters.Benchmarks.MAUI;

// 进程级的一次运行：解析命令行、选默认适配器名、把日志落在与报告同一个目录。
internal static class BenchmarkSession
{
    internal static HeadedOptions? Options { get; private set; }

    internal static RunLog? Log { get; private set; }

    internal static void Initialize(string[] args)
    {
        var hasAdapter = args.Any(static argument => argument == "--adapter");
        try
        {
            var options = HeadedOptions.Parse(args);

            // 本进程只宿主 MAUI；未显式指定时报告写 maui，而不是 Parse 的 avalonia 默认值。
            options.Adapter = hasAdapter ? options.Adapter : "maui";
            Options = options;
            Log = RunLog.Create(options, $"headed-{options.Adapter}");
            Log.Write(
                $"adapter={options.Adapter} policy={options.Policy} nodes={options.Nodes} links={options.Links} " +
                $"viewport={options.ViewportWidth}x{options.ViewportHeight} iterations={options.Iterations} " +
                $"warmup={options.WarmupIterations} steps={options.ScrollSteps}");
        }
        catch (Exception exception)
        {
            Options = null;
            Log = RunLog.Create(null, "headed-maui-startup");
            Log.Write("invalid command line: " + exception.Message);
            Log.Write(HeadedOptions.Usage);
        }
    }
}
