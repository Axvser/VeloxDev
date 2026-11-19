namespace VeloxDev.WorkflowSystem;

/// <summary>
/// The layer that renders a platform's links itself instead of letting the main pool materialize one
/// view per link.
/// </summary>
/// <typeparam name="THost">The platform element that owns the views.</typeparam>
/// <typeparam name="TItem">The item type the pool tracks.</typeparam>
/// <remarks>
/// <para>
/// Per-link platforms return <see langword="false"/> from <see cref="Handles"/> and let the regular
/// factory/container path materialize links; overlay platforms (a single canvas that draws every
/// visible link) return <see langword="true"/> for links.
/// </para>
/// <para>
/// The pool calls <see cref="Render"/> with the current handled items whenever the visible set
/// changes, including when it becomes empty. Implementations must not mutate the visible collection.
/// </para>
/// </remarks>
public interface IViewLinkLayer<THost, TItem>
    where THost : class
    where TItem : class
{
    /// <summary>Gets whether this layer handles any item at all; false lets the pool skip link rendering entirely.</summary>
    bool IsActive { get; }

    /// <summary>Returns whether this layer renders <paramref name="item"/> itself.</summary>
    bool Handles(TItem item);

    /// <summary>Renders the current set of handled items on <paramref name="host"/>.</summary>
    void Render(THost host, IReadOnlyList<TItem> items);
}
