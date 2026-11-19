using System.Collections.Specialized;
using System.Windows.Forms;
using VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace VeloxDev.WorkflowSystem.ViewHosting;

/// <summary>The WinForms closure of the Core view system: it composes the platform layers and starts the pool.</summary>
public sealed class ViewSourcePipeline : ViewSourcePipelineCore<
    Control,
    Control,
    IWorkflowViewModel,
    NonPriority,
    ViewFactory,
    ViewContainer,
    ViewArranger,
    ViewScheduler,
    ViewDiagnostics>
{
    /// <summary>Creates the system for one host control and starts tracking its visible items.</summary>
    /// <param name="host">The control that owns the materialized views.</param>
    /// <param name="templateSelector">The host's item-to-control selector.</param>
    /// <param name="source">The visible-item collection.</param>
    /// <param name="poolPolicy">The batch/recycle policy; defaults to <see cref="AdaptiveViewPoolPolicy"/>.</param>
    public ViewSourcePipeline(
        Control host,
        IWorkflowTemplateSelector templateSelector,
        INotifyCollectionChanged source,
        IViewPoolPolicy? poolPolicy = null)
        : base(
            host,
            new ViewFactory(templateSelector),
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
