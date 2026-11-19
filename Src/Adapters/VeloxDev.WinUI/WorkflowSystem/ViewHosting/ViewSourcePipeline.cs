using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Collections.Specialized;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.WorkflowSystem.ViewHosting;

/// <summary>The WinUI closure of the Core view system: it composes the platform layers and starts the pool.</summary>
public sealed class ViewSourcePipeline : ViewSourcePipelineCore<
    Panel,
    FrameworkElement,
    IWorkflowViewModel,
    DispatcherQueuePriority,
    ViewFactory,
    ViewContainer,
    ViewArranger,
    ViewScheduler,
    ViewDiagnostics>
{
    /// <summary>Creates the system for one panel and starts tracking its visible items.</summary>
    /// <param name="panel">The panel that owns the materialized views.</param>
    /// <param name="templateSelector">The selector consulted first, or <see langword="null"/> to use resource keys.</param>
    /// <param name="source">The visible-item collection.</param>
    /// <param name="poolPolicy">The batch/recycle policy; defaults to <see cref="AdaptiveViewPoolPolicy"/>.</param>
    public ViewSourcePipeline(
        Panel panel,
        DataTemplateSelector? templateSelector,
        INotifyCollectionChanged source,
        IViewPoolPolicy? poolPolicy = null)
        : this(panel, templateSelector, source, new ViewLayoutTracker(), poolPolicy)
    {
    }

    // Container 与 Arranger 必须拿到同一份订阅表，才能在 Deactivate 时退掉 Arrange 挂上的处理器。
    private ViewSourcePipeline(
        Panel panel,
        DataTemplateSelector? templateSelector,
        INotifyCollectionChanged source,
        ViewLayoutTracker layout,
        IViewPoolPolicy? poolPolicy)
        : base(
            panel,
            new ViewFactory(panel, templateSelector),
            new ViewContainer(layout),
            new ViewArranger(layout),
            new ViewScheduler(panel.DispatcherQueue),
            new ViewDiagnostics(),
            source,
            poolPolicy ?? new AdaptiveViewPoolPolicy(),
            new ViewLayerPolicy(),
            new ViewLinkLayer(),
            new ViewMeasureHost())
    {
    }
}
