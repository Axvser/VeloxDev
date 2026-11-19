using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Threading;
using System.Collections.Specialized;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.WorkflowSystem.ViewHosting;

/// <summary>The Avalonia closure of the Core view system: it composes the platform layers and starts the pool.</summary>
public sealed class ViewSourcePipeline : ViewSourcePipelineCore<
    Panel,
    Control,
    IWorkflowViewModel,
    DispatcherPriority,
    ViewFactory,
    ViewContainer,
    ViewArranger,
    ViewScheduler,
    ViewDiagnostics>
{
    /// <summary>Creates the system for one panel and starts tracking its visible items.</summary>
    /// <param name="panel">The panel that owns the materialized views.</param>
    /// <param name="templateSelector">The selector consulted first, or <see langword="null"/> to use the panel's templates.</param>
    /// <param name="source">The visible-item collection.</param>
    /// <param name="poolPolicy">The batch/recycle policy; defaults to the Avalonia policy.</param>
    public ViewSourcePipeline(
        Panel panel,
        IDataTemplate? templateSelector,
        INotifyCollectionChanged source,
        IViewPoolPolicy? poolPolicy = null)
        : base(
            panel,
            new ViewFactory(panel, templateSelector),
            new ViewContainer(),
            new ViewArranger(),
            new ViewScheduler(),
            new ViewDiagnostics(),
            source,
            poolPolicy ?? new AdaptiveViewPoolPolicy(),
            new ViewLayerPolicy(),
            new ViewLinkLayer(),
            new ViewMeasureHost())
    {
    }
}
