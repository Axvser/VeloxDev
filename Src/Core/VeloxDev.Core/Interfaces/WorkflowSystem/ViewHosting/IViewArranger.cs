namespace VeloxDev.WorkflowSystem;

/// <summary>
/// The platform layer that applies an item's geometry to its view.
/// </summary>
/// <typeparam name="TItem">The item type the pool tracks.</typeparam>
/// <typeparam name="TView">The platform element used as one item's view.</typeparam>
/// <remarks>
/// Platforms whose templates bind position and size do nothing here; platforms that write
/// <c>Canvas.SetLeft</c>, <c>AbsoluteLayout.SetLayoutBounds</c> or CSS write them here. The pool calls
/// this after <see cref="IViewContainer{THost,TView,TItem}.Attach"/>, so imperative platforms
/// may rely on the view already being in the tree.
/// </remarks>
public interface IViewArranger<TItem, TView>
    where TItem : class
    where TView : class
{
    /// <summary>Applies <paramref name="item"/>'s current geometry to <paramref name="view"/>.</summary>
    void Arrange(TView view, TItem item);
}
