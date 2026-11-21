using System.Collections.Specialized;
using VeloxDev.Threading;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.WorkflowSystem.ViewHosting;

/// <summary>The MAUI closure of the Core view system: it composes the platform layers and starts the pool.</summary>
public sealed class ViewSourcePipeline : ViewSourcePipelineCore<
    Layout,
    View,
    IWorkflowComponent,
    NonPriority,
    ViewFactory,
    ViewContainer,
    ViewArranger,
    ViewScheduler,
    ViewDiagnostics>
{
    private readonly ViewScheduler _scheduler;
    private readonly ViewArranger _arranger;

    /// <summary>Creates the system for one layout and starts tracking its visible items.</summary>
    /// <param name="host">The layout that owns the materialized views.</param>
    /// <param name="templateSelector">The selector consulted for each item type, or <see langword="null"/> to materialize nothing.</param>
    /// <param name="source">The visible-item collection.</param>
    /// <param name="poolPolicy">
    /// The batch/recycle policy. The default is Core's adaptive policy starting at 32 views over an
    /// 8 ms budget: a phone-sized viewport rarely exposes more than a few dozen nodes at once, and a
    /// smaller first batch keeps a cold scroll from stalling a frame while the policy is still learning
    /// this device's view cost.
    /// </param>
    public ViewSourcePipeline(
        Layout host,
        DataTemplateSelector? templateSelector,
        INotifyCollectionChanged source,
        IViewPoolPolicy? poolPolicy = null)
        : this(host, templateSelector, source, new ViewScheduler(host), new ViewArranger(host), poolPolicy)
    {
    }

    private ViewSourcePipeline(
        Layout host,
        DataTemplateSelector? templateSelector,
        INotifyCollectionChanged source,
        ViewScheduler scheduler,
        ViewArranger arranger,
        IViewPoolPolicy? poolPolicy)
        : base(
            host,
            new ViewFactory(host, templateSelector),
            new ViewContainer(arranger),
            arranger,
            scheduler,
            new ViewDiagnostics(),
            source,
            poolPolicy ?? new AdaptiveViewPoolPolicy(initialBatchSize: 32, initialBudgetMilliseconds: 8d),
            new ViewLayerPolicy(),
            new ViewLinkLayer(),
            new ViewMeasureHost())
    {
        _scheduler = scheduler;
        _arranger = arranger;
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        base.Dispose();
        _arranger.Dispose();
        _scheduler.Dispose();
    }
}
