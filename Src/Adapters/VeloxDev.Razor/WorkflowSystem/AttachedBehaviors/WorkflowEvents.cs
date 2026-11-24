using System;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

/// <summary>
/// Connects a component's model events to a host's event sink on Razor views.
/// </summary>
/// <remarks>
/// <para>
/// This is the Razor analogue of the XAML adapters' <c>WorkflowEvents</c> attached property: a component calls
/// <see cref="Attach(INodeComponent, INodeEventSink)"/> or its overloads from <c>OnInitialized</c> /
/// <c>OnParametersSet</c>, then disposes the returned subscription when it is finished with the component.
/// </para>
/// <para>
/// The subscription follows the component instance, so a view that is reused for another component must dispose the
/// old subscription and re-attach. A helper that offers no events yields <see langword="null"/> — there is nothing
/// to dispose.
/// </para>
/// </remarks>
/// <seealso cref="WorkflowEventRelay"/>
public static class WorkflowEvents
{
    /// <summary>Relays a node's placement and lifetime events into <paramref name="sink"/>.</summary>
    /// <param name="node">The node to watch.</param>
    /// <param name="sink">The host's handler.</param>
    /// <returns>The subscription, or <see langword="null"/> when the node's Helper offers no events.</returns>
    public static IDisposable? Attach(INodeComponent node, INodeEventSink sink)
        => WorkflowEventRelay.Attach(node, sink);

    /// <summary>Relays a slot's channel events into <paramref name="sink"/>.</summary>
    /// <param name="slot">The slot to watch.</param>
    /// <param name="sink">The host's handler.</param>
    /// <returns>The subscription, or <see langword="null"/> when the slot's Helper offers no events.</returns>
    public static IDisposable? Attach(ISlotComponent slot, ISlotEventSink sink)
        => WorkflowEventRelay.Attach(slot, sink);

    /// <summary>Relays a tree's connection events into <paramref name="sink"/>.</summary>
    /// <param name="tree">The tree to watch.</param>
    /// <param name="sink">The host's handler.</param>
    /// <returns>The subscription, or <see langword="null"/> when the tree's Helper offers no events.</returns>
    public static IDisposable? Attach(ITreeComponent tree, ITreeEventSink sink)
        => WorkflowEventRelay.Attach(tree, sink);
}
