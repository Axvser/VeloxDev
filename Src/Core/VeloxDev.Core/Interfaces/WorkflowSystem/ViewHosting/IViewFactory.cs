namespace VeloxDev.WorkflowSystem;

/// <summary>
/// The platform layer that turns one visible workflow item into a view, and decides whether this
/// system materializes that item at all.
/// </summary>
/// <typeparam name="TItem">The item type the pool tracks.</typeparam>
/// <typeparam name="TView">The platform element used as one item's view.</typeparam>
/// <remarks>
/// A factory is stateful: it owns the template lookup and any template cache. Returning
/// <see langword="null"/> means this item type has no view here; the pool remembers that answer by
/// runtime type until the system is disposed.
/// </remarks>
public interface IViewFactory<TItem, TView>
    where TItem : class
    where TView : class
{
    /// <summary>Returns whether <paramref name="item"/> should be materialized by this system.</summary>
    /// <remarks>
    /// A platform that draws some item kinds itself — for example a link overlay — returns
    /// <see langword="false"/> for those kinds and keeps no view for them.
    /// </remarks>
    bool WantsItem(TItem item);

    /// <summary>Creates a view for <paramref name="item"/>, or returns <see langword="null"/> when no template matches it.</summary>
    TView? CreateView(TItem item);
}
