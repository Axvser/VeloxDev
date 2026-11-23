using System;
using System.Collections.Specialized;
using VeloxDev.WinForms.Xaml.Visuals;
using VeloxDev.WorkflowSystem;
using VisualPanel = VeloxDev.WinForms.Xaml.Visuals.Controls.Panel;

namespace VeloxDev.WorkflowSystem.ViewHosting;

/// <summary>The WinForms closure of the Core view system: it composes the platform layers and starts the pool.</summary>
/// <remarks>
/// Disposing the system releases the views the container attached as well as the Core bookkeeping; the canvas is
/// left in the tree, so a rebuild starts from a clean canvas rather than from the previous system's hidden views.
/// </remarks>
public sealed class ViewSourcePipeline : ViewSourcePipelineCore<
    VisualPanel,
    VisualElement,
    IWorkflowComponent,
    VeloxDev.Threading.NonPriority,
    ViewFactory,
    ViewContainer,
    ViewArranger,
    ViewScheduler,
    ViewDiagnostics>
{
    private readonly VisualPanel _host;

    /// <summary>Creates the system for one canvas and starts tracking its visible items.</summary>
    /// <param name="host">The canvas that owns the materialized views.</param>
    /// <param name="templateSelector">The selector that creates one view per item, or <see langword="null"/>.</param>
    /// <param name="source">The visible-item collection.</param>
    /// <param name="batchCompleted">Invoked after a materialization batch that created or reused views, or <see langword="null"/>.</param>
    /// <param name="poolPolicy">The batch/recycle policy; defaults to the Core adaptive policy.</param>
    public ViewSourcePipeline(
        VisualPanel host,
        VisualTemplateSelector? templateSelector,
        INotifyCollectionChanged source,
        Action? batchCompleted = null,
        IViewPoolPolicy? poolPolicy = null)
        : base(
            host,
            new ViewFactory(templateSelector),
            new ViewContainer(),
            new ViewArranger(),
            new ViewScheduler(host),
            new ViewDiagnostics(),
            source,
            CreatePolicy(poolPolicy, batchCompleted),
            new ViewLayerPolicy(),
            new ViewLinkLayer(),
            new ViewMeasureHost())
    {
        _host = host;
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        base.Dispose();
        ((ViewContainer)Container).ReleaseAll(_host);
    }

    // 批结束通知必须装在策略上：Core 只在 PoolPolicy 同时是 IViewBatchFeedback 时才回调。
    private static IViewPoolPolicy CreatePolicy(IViewPoolPolicy? poolPolicy, Action? batchCompleted)
    {
        poolPolicy ??= new AdaptiveViewPoolPolicy();
        return batchCompleted is null ? poolPolicy : new WinFormsViewPoolPolicy(poolPolicy, batchCompleted);
    }
}

// 把 Core 的批反馈转成一次外部通知；自适应策略仍收到它自己的反馈。
internal sealed class WinFormsViewPoolPolicy : IViewPoolPolicy, IViewBatchFeedback
{
    private readonly IViewPoolPolicy _inner;
    private readonly IViewBatchFeedback? _feedback;
    private readonly Action _batchCompleted;

    internal WinFormsViewPoolPolicy(IViewPoolPolicy inner, Action batchCompleted)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _feedback = inner as IViewBatchFeedback;
        _batchCompleted = batchCompleted ?? throw new ArgumentNullException(nameof(batchCompleted));
    }

    public int BatchSize => _inner.BatchSize;

    public double FrameBudgetMilliseconds => _inner.FrameBudgetMilliseconds;

    public int MaxRecycledViewsPerType => _inner.MaxRecycledViewsPerType;

    public void OnBatchCompleted(int processed, long elapsedTicks, int pending)
    {
        _feedback?.OnBatchCompleted(processed, elapsedTicks, pending);
        if (processed > 0)
        {
            _batchCompleted();
        }
    }
}
