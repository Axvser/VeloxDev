using System.Diagnostics;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using VeloxDev.Adapters.Benchmarks.Headed;
using VeloxDev.WorkflowSystem;
using WinUiViewHosting = VeloxDev.WorkflowSystem.ViewHosting;

namespace VeloxDev.Adapters.Benchmarks.WinUI;

// 一家一个宿主：真实 WinUI 窗口 + 一份场景。模板只给外观，几何由适配器的 ViewArranger 写进 Canvas。
internal sealed class WinUiHeadedHost : IHeadedBenchmarkHost
{
    private readonly HeadedOptions _options;
    private readonly Window _window;
    private readonly Canvas _panel;
    private readonly DataTemplateSelector _templateSelector;
    private SyntheticWorkflow? _workflow;
    private WinUiViewHosting.ViewSourcePipeline? _system;

    internal WinUiHeadedHost(HeadedOptions options)
    {
        _options = options;
        _templateSelector = new WinUiBenchmarkTemplateSelector();
        _panel = new Canvas
        {
            Width = options.ViewportWidth,
            Height = options.ViewportHeight,
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
        };
        _window = new Window
        {
            Title = "VeloxDev view pipeline — WinUI",
            Content = _panel,
        };
    }

    internal Window Window => _window;

    internal void Show()
    {
        _window.Activate();
        ResizeToViewport();
        // 首次 Loaded 之后 XamlRoot 才有 DPI，按真实缩放再调一次；基准量的是 DIP 视口，窗口尺寸只影响可见画面。
        _panel.Loaded += (_, _) => ResizeToViewport();
    }

    /// <inheritdoc />
    public async Task ResetScenarioAsync()
    {
        _system?.Dispose();
        _system = null;
        _panel.Children.Clear();

        _workflow = SyntheticWorkflow.Create(_options);
        IViewPoolPolicy policy = _options.Policy == "adaptive"
            ? new AdaptiveViewPoolPolicy(_options.BatchSize, _options.FrameBudgetMilliseconds)
            : new WinUiBenchmarkFixedPoolPolicy(_options.BatchSize, _options.FrameBudgetMilliseconds);
        _system = new WinUiViewHosting.ViewSourcePipeline(
            _panel,
            _templateSelector,
            _workflow.Tree.GetHelper().VisibleItems,
            policy);

        await QuiesceAsync();
    }

    /// <inheritdoc />
    public Task<PipelineSnapshot> MeasureInitialAsync()
        => MeasureAsync(() => SetViewport(new Viewport(0d, 0d, _options.ViewportWidth, _options.ViewportHeight)));

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
        return MeasureAsync(() => SetViewport(new Viewport(0d, 0d, workflow.ContentWidth, workflow.ContentHeight)));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _system?.Dispose();
        _system = null;
        _panel.Children.Clear();
        _window.Close();
    }

    private void ResizeToViewport()
    {
        var scale = _panel.XamlRoot?.RasterizationScale ?? 1d;
        _window.AppWindow.Resize(new Windows.Graphics.SizeInt32(
            (int)Math.Ceiling(_options.ViewportWidth * scale),
            (int)Math.Ceiling(_options.ViewportHeight * scale)));
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
        var queue = _panel.DispatcherQueue
            ?? throw new InvalidOperationException("The panel is not attached to a dispatcher queue.");

        // 池的批排在 DispatcherQueuePriority.Low；同优先级空操作排在已排批次之后，返回时该批已执行。
        for (var spins = 0; spins < 10_000 && system.Pool.PendingViewCount > 0; spins++)
        {
            await EnqueueAsync(queue, DispatcherQueuePriority.Low);
        }

        await WaitForFrameAsync();
    }

    private static Task EnqueueAsync(DispatcherQueue queue, DispatcherQueuePriority priority)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!queue.TryEnqueue(priority, () => completion.TrySetResult()))
        {
            completion.TrySetResult();
        }

        return completion.Task;
    }

    // Rendering 只在组合器真正出帧时触发；窗口被遮挡或处于无桌面会话时它不会到来，宁可报错也不挂死。
    private static async Task WaitForFrameAsync()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler<object>? handler = null;
        handler = (_, _) =>
        {
            CompositionTarget.Rendering -= handler;
            completion.TrySetResult();
        };
        CompositionTarget.Rendering += handler;

        using var cancellation = new CancellationTokenSource();
        var frame = completion.Task;
        var timeout = Task.Delay(TimeSpan.FromSeconds(10), cancellation.Token);
        var finished = await Task.WhenAny(frame, timeout);
        cancellation.Cancel();
        if (!ReferenceEquals(finished, frame))
        {
            CompositionTarget.Rendering -= handler;
            throw new TimeoutException(
                "The WinUI composition target produced no frame in 10 s; the window is not rendering.");
        }
    }
}
