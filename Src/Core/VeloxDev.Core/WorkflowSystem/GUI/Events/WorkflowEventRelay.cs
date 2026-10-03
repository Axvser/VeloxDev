using System;

namespace VeloxDev.WorkflowSystem;

/// <summary>
/// Connects a host's <see cref="IWorkflowNodeEventSink"/> / <see cref="IWorkflowSlotEventSink"/> /
/// <see cref="IWorkflowTreeEventSink"/> to a component's Helper, and hands back the subscription to dispose.
///
/// The platforms that can write it declaratively do only this: a XAML attached property takes the sink from a
/// binding and calls <c>Attach</c>; a Razor host subscribes to the Helper itself; the code-only platforms expose
/// <c>protected virtual</c> hooks on their base classes and call <c>Attach</c> with a sink that forwards to them.
/// The forwarding lives here, once, so the seven of them cannot drift apart.
/// </summary>
/// <remarks>
/// A component whose Helper does not offer the events answers <see langword="null"/> — the capability is optional,
/// and a helper that never opted in simply has no events to relay.
/// </remarks>
public static class WorkflowEventRelay
{
    /// <summary>Relays a node's placement and lifetime events into <paramref name="sink"/>.</summary>
    /// <param name="node">The node to watch.</param>
    /// <param name="sink">The host's handler.</param>
    /// <returns>The subscription, or <see langword="null"/> when the node's Helper offers no events.</returns>
    public static IDisposable? Attach(IWorkflowNodeViewModel node, IWorkflowNodeEventSink sink)
    {
        if (node is null || sink is null) return null;
        if (node.GetHelper() is not IWorkflowNodeEvents events) return null;

        void Moving(object? _, NodeMoveEventArgs e) => sink.OnMoving(e);
        void Moved(object? _, NodeMoveEventArgs e) => sink.OnMoved(e);
        void Resizing(object? _, NodeResizeEventArgs e) => sink.OnResizing(e);
        void Resized(object? _, NodeResizeEventArgs e) => sink.OnResized(e);
        void Deleting(object? _, NodeEventArgs e) => sink.OnDeleting(e);
        void Deleted(object? _, NodeEventArgs e) => sink.OnDeleted(e);

        events.Moving += Moving;
        events.Moved += Moved;
        events.Resizing += Resizing;
        events.Resized += Resized;
        events.Deleting += Deleting;
        events.Deleted += Deleted;

        return new Subscription(() =>
        {
            events.Moving -= Moving;
            events.Moved -= Moved;
            events.Resizing -= Resizing;
            events.Resized -= Resized;
            events.Deleting -= Deleting;
            events.Deleted -= Deleted;
        });
    }

    /// <summary>Relays a slot's channel events into <paramref name="sink"/>.</summary>
    /// <param name="slot">The slot to watch.</param>
    /// <param name="sink">The host's handler.</param>
    /// <returns>The subscription, or <see langword="null"/> when the slot's Helper offers no events.</returns>
    public static IDisposable? Attach(IWorkflowSlotViewModel slot, IWorkflowSlotEventSink sink)
    {
        if (slot is null || sink is null) return null;
        if (slot.GetHelper() is not IWorkflowSlotEvents events) return null;

        void Changing(object? _, SlotChannelEventArgs e) => sink.OnChannelChanging(e);
        void Changed(object? _, SlotChannelEventArgs e) => sink.OnChannelChanged(e);

        events.ChannelChanging += Changing;
        events.ChannelChanged += Changed;

        return new Subscription(() =>
        {
            events.ChannelChanging -= Changing;
            events.ChannelChanged -= Changed;
        });
    }

    /// <summary>Relays a tree's connection events into <paramref name="sink"/>.</summary>
    /// <param name="tree">The tree to watch.</param>
    /// <param name="sink">The host's handler.</param>
    /// <returns>The subscription, or <see langword="null"/> when the tree's Helper offers no events.</returns>
    public static IDisposable? Attach(IWorkflowTreeViewModel tree, IWorkflowTreeEventSink sink)
    {
        if (tree is null || sink is null) return null;
        if (tree.GetHelper() is not IWorkflowTreeEvents events) return null;

        void Connecting(object? _, ConnectionEventArgs e) => sink.OnConnecting(e);
        void Connected(object? _, ConnectionEventArgs e) => sink.OnConnected(e);

        events.Connecting += Connecting;
        events.Connected += Connected;

        return new Subscription(() =>
        {
            events.Connecting -= Connecting;
            events.Connected -= Connected;
        });
    }

    // 订阅本身：平台把它存起来，换宿主或摘钩子时 Dispose。
    private sealed class Subscription(Action unsubscribe) : IDisposable
    {
        private Action? _unsubscribe = unsubscribe;

        public void Dispose()
        {
            _unsubscribe?.Invoke();
            _unsubscribe = null;
        }
    }
}
