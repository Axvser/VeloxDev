namespace VeloxDev.WorkflowSystem;

/// <summary>
/// The optional post-arrange measure hook: a platform that must read back a view's measured geometry
/// after it has been attached and arranged does so here.
/// </summary>
/// <typeparam name="THost">The platform element that owns the views.</typeparam>
/// <typeparam name="TView">The platform element used as one item's view.</typeparam>
/// <typeparam name="TItem">The item type the pool tracks.</typeparam>
/// <remarks>
/// <para>
/// Most platforms do not need this: bindings, layout passes and DOM measurement already deliver the
/// geometry. Returning without doing anything is a valid implementation.
/// </para>
/// <para>
/// This is not a replacement for platform layout batching. An implementation that must defer
/// measurement should queue it on its own layout signal and return; the pool does not wait.
/// </para>
/// </remarks>
public interface IViewMeasureHost<THost, TView, TItem>
    where THost : class
    where TView : class
    where TItem : class
{
    /// <summary>Measures or schedules measurement for <paramref name="view"/> after it has been arranged.</summary>
    void Measure(THost host, TView view, TItem item);
}
