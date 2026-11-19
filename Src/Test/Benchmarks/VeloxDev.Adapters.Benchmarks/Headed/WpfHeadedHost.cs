extern alias wpf;

using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using VeloxDev.WorkflowSystem;
using WpfViewHosting = wpf::VeloxDev.WorkflowSystem.ViewHosting;
using WpfColor = System.Windows.Media.Color;
using WpfBrushes = System.Windows.Media.Brushes;

namespace VeloxDev.Adapters.Benchmarks.Headed;

// 一家一个宿主：真实 WPF 窗口 + 一份场景。几何由模板绑定，池只负责物化与回收。
internal sealed class WpfHeadedHost : IHeadedBenchmarkHost
{
    private readonly HeadedOptions _options;
    private readonly Window _window;
    private readonly Canvas _panel;
    private readonly DataTemplateSelector _templateSelector;
    private SyntheticWorkflow? _workflow;
    private WpfViewHosting.ViewSourcePipeline? _system;

    internal Window Window => _window;

    public WpfHeadedHost(HeadedOptions options)
    {
        _options = options;
        _templateSelector = new WpfBenchmarkTemplateSelector();
        _panel = new Canvas
        {
            Width = options.ViewportWidth,
            Height = options.ViewportHeight,
            Background = WpfBrushes.Transparent,
        };
        _window = new Window
        {
            Title = "VeloxDev view pipeline — WPF",
            Width = options.ViewportWidth,
            Height = options.ViewportHeight,
            Content = _panel,
        };
    }

    internal void Show() => _window.Show();

    public async Task ResetScenarioAsync()
    {
        _system?.Dispose();
        _system = null;
        _panel.Children.Clear();

        _workflow = SyntheticWorkflow.Create(_options);
        IViewPoolPolicy policy = _options.Policy == "adaptive"
            ? new AdaptiveViewPoolPolicy(_options.BatchSize, _options.FrameBudgetMilliseconds)
            : new BenchmarkFixedPoolPolicy(_options.BatchSize, _options.FrameBudgetMilliseconds);
        _system = new WpfViewHosting.ViewSourcePipeline(
            _panel,
            _templateSelector,
            _workflow.Tree.GetHelper().VisibleItems,
            policy);

        await QuiesceAsync();
    }

    public Task<PipelineSnapshot> MeasureInitialAsync()
        => MeasureAsync(() => SetViewport(new Viewport(0d, 0d, _options.ViewportWidth, _options.ViewportHeight)));

    public async Task<IReadOnlyList<PipelineSnapshot>> MeasureScrollAsync()
    {
        var workflow = _workflow ?? throw new InvalidOperationException("ResetScenarioAsync was not called.");
        var maxX = Math.Max(0d, workflow.ContentWidth - _options.ViewportWidth);
        var maxY = Math.Max(0d, workflow.ContentHeight - _options.ViewportHeight);
        var steps = Math.Max(1, _options.ScrollSteps);
        var results = new List<PipelineSnapshot>(steps);
        for (var step = 1; step <= steps; step++)
        {
            var x = maxX * step / steps;
            var y = maxY * step / steps;
            results.Add(await MeasureAsync(() => SetViewport(new Viewport(x, y, _options.ViewportWidth, _options.ViewportHeight))));
        }

        return results;
    }

    public Task<PipelineSnapshot> MeasureFullAsync()
    {
        var workflow = _workflow ?? throw new InvalidOperationException("ResetScenarioAsync was not called.");
        return MeasureAsync(() => SetViewport(new Viewport(0d, 0d, workflow.ContentWidth, workflow.ContentHeight)));
    }

    public void Dispose()
    {
        _system?.Dispose();
        _system = null;
        _panel.Children.Clear();
        _window.Close();
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
        var dispatcher = _panel.Dispatcher;

        // 批由适配器自己的 DispatcherPriority.Background 排队；用同优先级的空操作让出消息泵。
        for (var spins = 0; spins < 10_000 && system.Pool.PendingViewCount > 0; spins++)
        {
            await dispatcher.InvokeAsync(static () => { }, DispatcherPriority.Background);
        }

        // 绑定驱动的平台不会在批次里 Arrange；这里补一次真实布局，再让出一次渲染优先级。
        _panel.UpdateLayout();
        await dispatcher.InvokeAsync(static () => { }, DispatcherPriority.Render);
    }
}
