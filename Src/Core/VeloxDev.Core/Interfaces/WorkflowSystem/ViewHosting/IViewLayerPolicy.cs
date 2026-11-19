namespace VeloxDev.WorkflowSystem;

/// <summary>
/// The policy that decides an item's z-order before the container applies it.
/// </summary>
/// <typeparam name="TItem">The item type the pool tracks.</typeparam>
/// <remarks>
/// Platforms with a real z-order property (WinUI, MAUI, WinForms) use this to keep links below nodes;
/// platforms whose templates already set z-order return a constant and make
/// <see cref="IViewContainer{THost,TView,TItem}.SetZOrder"/> a no-op.
/// </remarks>
public interface IViewLayerPolicy<TItem>
    where TItem : class
{
    /// <summary>Gets the z-order the container should apply to <paramref name="item"/>'s view.</summary>
    int GetZOrder(TItem item);
}
