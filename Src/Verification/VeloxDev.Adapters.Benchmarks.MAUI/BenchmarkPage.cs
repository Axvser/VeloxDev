using VeloxDev.Adapters.Benchmarks.Headed;

namespace VeloxDev.Adapters.Benchmarks.MAUI;

// 页面只负责一件事：在真实窗口里出现后，把同一份 HeadedBenchmarkCore 跑完然后退出进程。
internal sealed class BenchmarkPage : ContentPage
{
    private readonly HeadedOptions _options;
    private readonly RunLog _log;
    private readonly MauiHeadedHost _host;
    private bool _started;

    internal BenchmarkPage(HeadedOptions options, RunLog log)
    {
        _options = options;
        _log = log;
        _host = new MauiHeadedHost(options, log.Write);

        Title = "VeloxDev view pipeline — MAUI";
        BackgroundColor = Color.FromArgb("#020617");
        Content = _host.Panel;
    }

    /// <inheritdoc />
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_started)
        {
            return;
        }

        _started = true;
        await RunAsync();
    }

    private async Task RunAsync()
    {
        var exitCode = 0;
        try
        {
            // 16ms 调度器与布局回合都要原生元素；等 handler 建好再开跑，等待才有对象可挂。
            for (var spins = 0; spins < 500 && _host.Panel.Handler is null; spins++)
            {
                await Task.Delay(10);
            }

            _log.Write(_host.Panel.Handler is null
                ? "panel handler missing; layout waits fall back to a frame delay"
                : "panel handler attached");

            var result = await HeadedBenchmarkCore.ExecuteAsync(_options, _host);
            _log.Write("report: " + result.MarkdownPath);
            _log.Write("json: " + result.JsonPath);
        }
        catch (Exception exception)
        {
            exitCode = 1;
            _log.Write("benchmark failed: " + exception);
        }
        finally
        {
            _host.Dispose();
            _log.Write($"done (exit {exitCode})");
            _log.Dispose();
        }

        // 这是专用的基准进程：跑完直接结束，别把窗口留在那里等人关。
        Environment.Exit(exitCode);
    }
}
