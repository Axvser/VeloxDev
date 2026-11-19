using System.Diagnostics;
using VeloxDev.Adapters.Benchmarks.Headed;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.ViewHosting;

namespace VeloxDev.Adapters.Benchmarks.MAUI;

// 与 WPF / WinForms / Avalonia / Jalium 宿主同形：一块真实布局 + 一份场景 + 每步等池排空。
internal sealed class MauiHeadedHost : IHeadedBenchmarkHost
{
    private readonly HeadedOptions _options;
    private readonly Action<string> _progress;
    private readonly AbsoluteLayout _panel;
    private readonly DataTemplateSelector _templateSelector;
    private SyntheticWorkflow? _workflow;
    private ViewSourcePipeline? _system;

    internal MauiHeadedHost(HeadedOptions options, Action<string> progress)
    {
        _options = options;
        _progress = progress;
        _templateSelector = new BenchmarkTemplateSelector();
        _panel = new AbsoluteLayout
        {
            WidthRequest = options.ViewportWidth,
            HeightRequest = options.ViewportHeight,
            HorizontalOptions = LayoutOptions.Start,
            VerticalOptions = LayoutOptions.Start,
            BackgroundColor = Color.FromArgb("#1E222A"),
            IsClippedToBounds = false,
        };
    }

    internal AbsoluteLayout Panel => _panel;

    /// <inheritdoc />
    public async Task ResetScenarioAsync()
    {
        _system?.Dispose();
        _system = null;
        _panel.Children.Clear();

        _workflow = SyntheticWorkflow.Create(_options);
        IViewPoolPolicy policy = _options.Policy == "adaptive"
            ? new AdaptiveViewPoolPolicy(_options.BatchSize, _options.FrameBudgetMilliseconds)
            : new FixedViewPoolPolicy(_options.BatchSize, _options.FrameBudgetMilliseconds);
        _system = new ViewSourcePipeline(
            _panel,
            _templateSelector,
            _workflow.Tree.GetHelper().VisibleItems,
            policy);

        _progress($"reset: {_options.Policy} policy ready");
        await QuiesceAsync();
    }

    /// <inheritdoc />
    public Task<PipelineSnapshot> MeasureInitialAsync()
    {
        _progress("initial");
        return MeasureAsync(() => SetViewport(new Viewport(0d, 0d, _options.ViewportWidth, _options.ViewportHeight)));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PipelineSnapshot>> MeasureScrollAsync()
    {
        var workflow = _workflow ?? throw new InvalidOperationException("ResetScenarioAsync was not called.");
        var maxX = Math.Max(0d, workflow.ContentWidth - _options.ViewportWidth);
        var maxY = Math.Max(0d, workflow.ContentHeight - _options.ViewportHeight);
        var steps = Math.Max(1, _options.ScrollSteps);
        var results = new List<PipelineSnapshot>(steps);
        for (var step = 1; step <= steps; step++)
        {
            _progress($"scroll {step}/{steps}");
            var x = maxX * step / steps;
            var y = maxY * step / steps;
            results.Add(await MeasureAsync(() => SetViewport(new Viewport(x, y, _options.ViewportWidth, _options.ViewportHeight))));
        }

        return results;
    }

    /// <inheritdoc />
    public Task<PipelineSnapshot> MeasureFullAsync()
    {
        var workflow = _workflow ?? throw new InvalidOperationException("ResetScenarioAsync was not called.");
        _progress("full visibility");
        return MeasureAsync(() => SetViewport(new Viewport(0d, 0d, workflow.ContentWidth, workflow.ContentHeight)));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _system?.Dispose();
        _system = null;
        _panel.Children.Clear();
    }

    private void SetViewport(Viewport viewport)
        => (_workflow ?? throw new InvalidOperationException("ResetScenarioAsync was not called."))
            .Tree.GetHelper().Viewport = viewport;

    private async Task<PipelineSnapshot> MeasureAsync(Action action)
    {
        var beforeAllocated = GC.GetTotalAllocatedBytes(precise: true);
        var gen0 = GC.CollectionCount(0);
        var gen1 = GC.CollectionCount(1);
        var gen2 = GC.CollectionCount(2);
        var started = Stopwatch.GetTimestamp();

        action();
        await QuiesceAsync();

        var elapsed = Stopwatch.GetTimestamp() - started;
        var afterAllocated = GC.GetTotalAllocatedBytes(precise: true);
        return Snapshot(
            elapsed,
            afterAllocated - beforeAllocated,
            GC.CollectionCount(0) - gen0,
            GC.CollectionCount(1) - gen1,
            GC.CollectionCount(2) - gen2);
    }

    private PipelineSnapshot Snapshot(long elapsedTicks, long allocatedBytes, int gen0, int gen1, int gen2)
    {
        var pool = (_system ?? throw new InvalidOperationException("ResetScenarioAsync was not called.")).Pool;
        return new PipelineSnapshot(
            elapsedTicks,
            allocatedBytes,
            gen0,
            gen1,
            gen2,
            pool.ActiveViewCount,
            pool.PendingViewCount,
            pool.RecycledViewCount,
            pool.CreatedViewCount,
            pool.ReusedViewCount,
            pool.DestroyedViewCount,
            _workflow?.Tree.GetHelper().VisibleItems.Count ?? 0);
    }

    private async Task QuiesceAsync()
    {
        var system = _system ?? throw new InvalidOperationException("ResetScenarioAsync was not called.");

        // MAUI 没有 dispatcher 优先级：ViewScheduler 把每批放在 16ms 非重复计时器上。
        // 在主线程上等待，让这些 tick 照常出队；批次绝不在本方法里直接调用。
        for (var spins = 0; spins < 10_000 && system.Pool.PendingViewCount > 0; spins++)
        {
            await Task.Delay(1);
        }

        await WaitForLayoutTurnAsync();
    }

    private async Task WaitForLayoutTurnAsync()
    {
#if WINDOWS
        if (_panel.Handler?.PlatformView is Microsoft.UI.Xaml.FrameworkElement element)
        {
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            void OnLayoutUpdated(object? sender, object e)
            {
                element.LayoutUpdated -= OnLayoutUpdated;
                completion.TrySetResult();
            }

            element.LayoutUpdated += OnLayoutUpdated;
            element.InvalidateMeasure();

            // 布局回合一定会来；超时只防没有原生元素时把整轮卡死。
            if (await Task.WhenAny(completion.Task, Task.Delay(250)) != completion.Task)
            {
                element.LayoutUpdated -= OnLayoutUpdated;
            }

            return;
        }
#endif
        // handler 还没建立：退化成等一帧，仍把控制权交回主线程调度器。
        await Task.Delay(16);
    }
}
