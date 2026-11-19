using System.Collections.Specialized;

namespace VeloxDev.WorkflowSystem;

/// <summary>
/// The generic composition of a view system: it closes one concrete factory, container, arranger,
/// scheduler and diagnostics implementation over one host element, and starts the shared pool.
/// Adapters derive from it once and expose the closed type under the name their users see.
/// </summary>
/// <typeparam name="THost">The platform element that owns the views.</typeparam>
/// <typeparam name="TView">The platform element used as one item's view.</typeparam>
/// <typeparam name="TItem">The item type the pool tracks.</typeparam>
/// <typeparam name="TPriorityCore">The platform's dispatcher-priority type, or <c>NonPriority</c>.</typeparam>
/// <typeparam name="TFactory">The platform factory implementation.</typeparam>
/// <typeparam name="TContainer">The platform container implementation.</typeparam>
/// <typeparam name="TArranger">The platform arranger implementation.</typeparam>
/// <typeparam name="TScheduler">The platform scheduler implementation.</typeparam>
/// <typeparam name="TDiagnostics">The platform diagnostics implementation.</typeparam>
/// <remarks>
/// Adapters close this type once and expose it to their attached-property entry. Users never name
/// the layer types; the long generic list is the adapter's composition point, matching the way
/// <c>Transition&lt;T&gt;</c> closes the TransitionSystem pipeline.
/// </remarks>
public class ViewSourcePipelineCore<
    THost,
    TView,
    TItem,
    TPriorityCore,
    TFactory,
    TContainer,
    TArranger,
    TScheduler,
    TDiagnostics>
    : ViewHostCore<THost, TView, TItem, TPriorityCore>, IDisposable
    where THost : class
    where TView : class
    where TItem : class
    where TFactory : class, IViewFactory<TItem, TView>
    where TContainer : class, IViewContainer<THost, TView, TItem>
    where TArranger : class, IViewArranger<TItem, TView>
    where TScheduler : class, IViewScheduler<TPriorityCore>
    where TDiagnostics : class, IViewDiagnostics<TItem>
{
    /// <summary>Composes the layer instances and starts tracking <paramref name="source"/>.</summary>
    /// <param name="hostElement">The host element the container materializes views into.</param>
    /// <param name="factory">The platform factory implementation.</param>
    /// <param name="container">The platform container implementation.</param>
    /// <param name="arranger">The platform arranger implementation.</param>
    /// <param name="scheduler">The platform scheduler implementation.</param>
    /// <param name="diagnostics">The platform diagnostics implementation.</param>
    /// <param name="source">The visible-item collection.</param>
    /// <param name="poolPolicy">The batch/recycle policy; defaults to 3 per batch and unlimited recycling.</param>
    /// <param name="layerPolicy">The z-order policy; defaults to a constant zero.</param>
    /// <param name="linkLayer">The layer that renders links itself; defaults to the main pool owning links.</param>
    /// <param name="measureHost">The post-arrange measure hook; defaults to no-op.</param>
    public ViewSourcePipelineCore(
        THost hostElement,
        TFactory factory,
        TContainer container,
        TArranger arranger,
        TScheduler scheduler,
        TDiagnostics diagnostics,
        INotifyCollectionChanged source,
        IViewPoolPolicy? poolPolicy = null,
        IViewLayerPolicy<TItem>? layerPolicy = null,
        IViewLinkLayer<THost, TItem>? linkLayer = null,
        IViewMeasureHost<THost, TView, TItem>? measureHost = null)
        : base(
            factory,
            container,
            arranger,
            scheduler,
            diagnostics,
            poolPolicy,
            layerPolicy,
            linkLayer,
            measureHost)
    {
        Pool = new ViewPool<THost, TView, TItem, TPriorityCore>(hostElement, this, source);
    }

    /// <summary>Gets the running pool; dispose the system to stop it.</summary>
    public ViewPool<THost, TView, TItem, TPriorityCore> Pool { get; }

    /// <summary>Stops tracking the visible set and releases every active view.</summary>
    /// <remarks>An adapter whose layers own disposable resources overrides this and disposes them after the pool.</remarks>
    public virtual void Dispose() => Pool.Dispose();
}
