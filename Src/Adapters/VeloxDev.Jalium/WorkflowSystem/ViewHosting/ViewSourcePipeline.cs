using System.Collections.Specialized;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Threading;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.WorkflowSystem.ViewHosting;

/// <summary>The Jalium closure of the Core view system: it composes the platform layers and starts the pool.</summary>
public sealed class ViewSourcePipeline : ViewSourcePipelineCore<
    Panel,
    FrameworkElement,
    IWorkflowViewModel,
    DispatcherPriority,
    ViewFactory,
    ViewContainer,
    ViewArranger,
    ViewScheduler,
    ViewDiagnostics>
{
    /// <summary>Creates the system for one panel and starts tracking its visible items.</summary>
    /// <param name="host">The panel that owns the materialized views.</param>
    /// <param name="templateSelector">The selector consulted first, or <see langword="null"/> to search resources only.</param>
    /// <param name="source">The visible-item collection.</param>
    /// <param name="poolPolicy">The batch/recycle policy; defaults to <see cref="AdaptiveViewPoolPolicy"/>.</param>
    public ViewSourcePipeline(
        Panel host,
        DataTemplateSelector? templateSelector,
        INotifyCollectionChanged source,
        IViewPoolPolicy? poolPolicy = null)
        : base(
            host,
            new ViewFactory(host, templateSelector),
            new ViewContainer(),
            new ViewArranger(),
            new ViewScheduler(host),
            new ViewDiagnostics(),
            source,
            poolPolicy ?? new AdaptiveViewPoolPolicy(),
            new ViewLayerPolicy(),
            new ViewLinkLayer(),
            new ViewMeasureHost())
    {
    }
}
