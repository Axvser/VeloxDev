extern alias wf;

using System.Diagnostics;
using VeloxDev.WorkflowSystem;
using WfViewHosting = wf::VeloxDev.WorkflowSystem.ViewHosting;
using WfAttached = wf::VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace VeloxDev.Adapters.Benchmarks.Headed;

// 一家一个宿主：真实 WinForms 窗体 + 一份场景。控件由选择器现建，摆位走附加属性的同一条约定。
internal sealed class WinFormsHeadedHost : IHeadedBenchmarkHost
{
    private readonly HeadedOptions _options;
    private readonly Form _form;
    private readonly Panel _panel;
    private readonly WfAttached.IWorkflowTemplateSelector _templateSelector;
    private SyntheticWorkflow? _workflow;
    private WfViewHosting.ViewSourcePipeline? _system;

    internal Form Form => _form;

    public WinFormsHeadedHost(HeadedOptions options)
    {
        _options = options;
        _templateSelector = new WinFormsBenchmarkTemplateSelector();
        _panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(0x1E, 0x22, 0x2A),
        };
        _form = new Form
        {
            Text = "VeloxDev view pipeline — WinForms",
            ClientSize = new System.Drawing.Size((int)options.ViewportWidth, (int)options.ViewportHeight),
            StartPosition = FormStartPosition.CenterScreen,
        };
        _form.Controls.Add(_panel);
    }

    public async Task ResetScenarioAsync()
    {
        _system?.Dispose();
        _system = null;
        WfAttached.WorkflowLinkLayer.SetIsEnabled(_panel, false);
        WfAttached.WorkflowLinkLayer.SetWorkflowTree(_panel, null);
        ClearViews();

        _workflow = SyntheticWorkflow.Create(_options);
        IViewPoolPolicy policy = _options.Policy == "adaptive"
            ? new AdaptiveViewPoolPolicy(_options.BatchSize, _options.FrameBudgetMilliseconds)
            : new BenchmarkFixedPoolPolicy(_options.BatchSize, _options.FrameBudgetMilliseconds);
        _system = new WfViewHosting.ViewSourcePipeline(
            _panel,
            _templateSelector,
            _workflow.Tree.GetHelper().VisibleItems,
            policy);

        // Links are no longer pooled: the panel's paint pass draws them in one go (the MAUI-shaped layer).
        WfAttached.WorkflowLinkLayer.SetWorkflowTree(_panel, _workflow.Tree);
        WfAttached.WorkflowLinkLayer.SetThickness(_panel, 2f);
        WfAttached.WorkflowLinkLayer.SetIsEnabled(_panel, true);

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
        WfAttached.WorkflowLinkLayer.SetIsEnabled(_panel, false);
        WfAttached.WorkflowLinkLayer.SetWorkflowTree(_panel, null);
        _system?.Dispose();
        _system = null;
        ClearViews();
        _form.Close();
    }

    // 每次 Reset 都新起一份场景；WinForms 控件持有一个窗口句柄，只 Clear 不 Dispose 会在多轮迭代后耗尽句柄。
    private void ClearViews()
    {
        foreach (var control in _panel.Controls.Cast<Control>().ToArray())
        {
            control.Dispose();
        }

        _panel.Controls.Clear();
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

        // 池的批走 Control.BeginInvoke；消息泵由 Application.Run 驱动，这里只主动抽一次队列。
        for (var spins = 0; spins < 10_000 && system.Pool.PendingViewCount > 0; spins++)
        {
            Application.DoEvents();
            await Task.Yield();
        }

        // 再排一个空操作：它排在所有已排批次之后，返回时布局也已跑过。
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _panel.BeginInvoke(new Action(() =>
        {
            _panel.PerformLayout();
            completion.TrySetResult();
        }));
        await completion.Task;
    }
}
