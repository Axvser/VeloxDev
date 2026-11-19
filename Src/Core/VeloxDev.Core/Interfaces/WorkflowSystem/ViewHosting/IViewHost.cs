namespace VeloxDev.WorkflowSystem;

/// <summary>
/// The composition of the platform layers and policies used by one view pool: factory, container,
/// arranger, scheduler, diagnostics, pool policy, layer policy, link layer and measure host.
/// </summary>
/// <typeparam name="THost">The platform element that owns the views.</typeparam>
/// <typeparam name="TView">The platform element used as one item's view.</typeparam>
/// <typeparam name="TItem">The item type the pool tracks.</typeparam>
/// <typeparam name="TPriorityCore">The platform's dispatcher-priority type, or <c>NonPriority</c>.</typeparam>
/// <remarks>
/// <para>
/// A host is stateful: one instance serves one host element and owns that element's template lookup,
/// visual-tree membership and dispatcher. The pool never creates a host itself and never calls
/// platform APIs directly.
/// </para>
/// <para>
/// Each layer has its own contract so a platform can supply them independently; this interface only
/// groups them so the pool has one object to ask.
/// </para>
/// </remarks>
public interface IViewHost<THost, TView, TItem, TPriorityCore>
    where THost : class
    where TView : class
    where TItem : class
{
    /// <summary>Gets the layer that turns items into views.</summary>
    IViewFactory<TItem, TView> Factory { get; }

    /// <summary>Gets the layer that owns visual-tree membership and bound state.</summary>
    IViewContainer<THost, TView, TItem> Container { get; }

    /// <summary>Gets the layer that applies item geometry.</summary>
    IViewArranger<TItem, TView> Arranger { get; }

    /// <summary>Gets the layer that carries batches to the host thread.</summary>
    IViewScheduler<TPriorityCore> Scheduler { get; }

    /// <summary>Gets the layer that reports failed view operations.</summary>
    IViewDiagnostics<TItem> Diagnostics { get; }

    /// <summary>Gets the policy that bounds batches and recycled views.</summary>
    IViewPoolPolicy PoolPolicy { get; }

    /// <summary>Gets the policy that decides z-order.</summary>
    IViewLayerPolicy<TItem> LayerPolicy { get; }

    /// <summary>Gets the layer that renders links itself, when the platform does not use one view per link.</summary>
    IViewLinkLayer<THost, TItem> LinkLayer { get; }

    /// <summary>Gets the optional post-arrange measure hook.</summary>
    IViewMeasureHost<THost, TView, TItem> MeasureHost { get; }
}
