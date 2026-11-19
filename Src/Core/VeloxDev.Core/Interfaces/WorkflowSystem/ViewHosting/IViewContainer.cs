namespace VeloxDev.WorkflowSystem;

/// <summary>
/// The platform layer that owns a view's visual-tree membership and its bound state: attach/detach
/// decide whether the view lives in the host, activate/deactivate decide whether it is bound and
/// visible.
/// </summary>
/// <typeparam name="THost">The platform element that owns the views.</typeparam>
/// <typeparam name="TView">The platform element used as one item's view.</typeparam>
/// <typeparam name="TItem">The item type the pool tracks.</typeparam>
/// <remarks>
/// <para>
/// <see cref="Attach"/> must be idempotent; a recycled view may already be a child of the host.
/// </para>
/// <para>
/// <see cref="Detach"/> is not required to remove the view: platforms that keep hidden children
/// (WPF, Avalonia, WinUI, MAUI, Jalium) may do nothing here beyond the hiding done by
/// <see cref="Deactivate"/>. Platforms whose renderer owns the tree (Blazor) may remove the element.
/// </para>
/// </remarks>
public interface IViewContainer<THost, TView, TItem>
    where THost : class
    where TView : class
    where TItem : class
{
    /// <summary>Makes <paramref name="view"/> a child of <paramref name="host"/>; this must be idempotent.</summary>
    void Attach(THost host, TView view);

    /// <summary>Hides or removes a recycled <paramref name="view"/>; the pool may hand it back later.</summary>
    void Detach(THost host, TView view);

    /// <summary>Binds <paramref name="item"/> to <paramref name="view"/> and makes it visible.</summary>
    void Activate(TView view, TItem item);

    /// <summary>Releases the previous item and hides the view without destroying it.</summary>
    void Deactivate(TView view);

    /// <summary>Applies <paramref name="zIndex"/> to <paramref name="view"/>; platforms without a z-order make this a no-op.</summary>
    void SetZOrder(TView view, int zIndex);

    /// <summary>Permanently removes <paramref name="view"/> when the pool policy refuses to keep it.</summary>
    /// <remarks>Unlike <see cref="Detach"/>, this view will not be reused; remove it from the host and let it be collected.</remarks>
    void Destroy(THost host, TView view);
}
