using System;
using System.Collections.Generic;

namespace VeloxDev.WorkflowSystem;

/// <summary>The parts a workflow surface resolves and, where they carry notifications, keeps subscribed.</summary>
public enum WorkflowSurfacePart
{
    /// <summary>The scroll container that owns the viewport.</summary>
    ScrollViewer,

    /// <summary>The canvas the nodes and links are placed on.</summary>
    Canvas,

    /// <summary>The decorator that draws the grid, rulers and minimap backdrop.</summary>
    GridDecorator,

    /// <summary>The minimap overlay.</summary>
    MinimapOverlay,

    /// <summary>The element that receives the surface's pointer presses.</summary>
    PointerPressSource,
}

/// <summary>
/// Holds the parts of one workflow surface by role, and moves their subscribers when a part is replaced.
/// </summary>
/// <remarks>
/// <para>
/// The registry does not subscribe to anything itself. It is the single place that knows <b>which part is current</b>,
/// so a surface can re-resolve its parts on every pass without leaking or duplicating the handlers a part owns:
/// <see cref="Set"/> compares by reference and reports the change once, and <see cref="SurfacePartBinding{T}"/>
/// turns that report into "detach the old, attach the new".
/// </para>
/// <para>
/// Not thread-safe by design. A surface resolves and updates its parts on its host's UI thread; the registry adds no
/// lock that the callers would have to reason about.
/// </para>
/// <para>
/// Registering the same reference twice is a no-op, so a resolution pass that finds the same controls costs one
/// comparison per part and touches no subscription.
/// </para>
/// </remarks>
public sealed class SurfacePartRegistry
{
    private readonly Dictionary<WorkflowSurfacePart, object> parts = [];
    private readonly Dictionary<WorkflowSurfacePart, List<Action<object?, object?>>> handlers = [];

    /// <summary>Gets the current part for <paramref name="part"/>, or <see langword="null"/> when it is not resolved.</summary>
    /// <param name="part">The role to read.</param>
    public object? Get(WorkflowSurfacePart part)
        => parts.TryGetValue(part, out var value) ? value : null;

    /// <summary>Gets the current part for <paramref name="part"/> as <typeparamref name="T"/>, or <see langword="null"/>.</summary>
    /// <typeparam name="T">The part type the surface resolved.</typeparam>
    /// <param name="part">The role to read.</param>
    public T? Get<T>(WorkflowSurfacePart part) where T : class
        => Get(part) as T;

    /// <summary>Reads the current part when it is present and of type <typeparamref name="T"/>.</summary>
    /// <typeparam name="T">The part type the surface resolved.</typeparam>
    /// <param name="part">The role to read.</param>
    /// <param name="value">The current part, or <see langword="null"/>.</param>
    /// <returns>Whether a part of type <typeparamref name="T"/> is registered.</returns>
    public bool TryGet<T>(WorkflowSurfacePart part, out T? value) where T : class
    {
        value = Get(part) as T;
        return value is not null;
    }

    /// <summary>
    /// Replaces the part for <paramref name="part"/> and notifies its subscribers when the reference changed.
    /// </summary>
    /// <param name="part">The role being resolved.</param>
    /// <param name="value">The resolved part; <see langword="null"/> clears the role.</param>
    /// <returns>The part that was registered before this call, or <see langword="null"/>.</returns>
    /// <remarks>
    /// The same reference is not a change and produces no notification. Subscribers receive the old and the new value
    /// and are responsible for detaching the old before attaching the new — <see cref="SurfacePartBinding{T}"/> does
    /// that in that order.
    /// </remarks>
    public object? Set(WorkflowSurfacePart part, object? value)
    {
        parts.TryGetValue(part, out var previous);
        if (ReferenceEquals(previous, value))
        {
            return previous;
        }

        if (value is null)
        {
            parts.Remove(part);
        }
        else
        {
            parts[part] = value;
        }

        Notify(part, previous, value);
        return previous;
    }

    /// <summary>Clears every registered part, notifying each role once; calling it again does nothing.</summary>
    public void Clear()
    {
        if (parts.Count == 0)
        {
            return;
        }

        // Set 会改字典，先快照。
        var registered = new WorkflowSurfacePart[parts.Count];
        parts.Keys.CopyTo(registered, 0);
        foreach (var part in registered)
        {
            Set(part, null);
        }
    }

    /// <summary>Subscribes to changes of one role.</summary>
    /// <param name="part">The role to watch.</param>
    /// <param name="handler">Receives the previous and the new part; called with <see langword="null"/> as the new value on a clear.</param>
    /// <returns>A subscription that stops the notifications when disposed.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="handler"/> is <see langword="null"/>.</exception>
    public IDisposable OnChanged(WorkflowSurfacePart part, Action<object?, object?> handler)
    {
        if (handler is null) throw new ArgumentNullException(nameof(handler));

        if (!handlers.TryGetValue(part, out var list))
        {
            list = [];
            handlers[part] = list;
        }

        list.Add(handler);
        return new Subscription(() => list.Remove(handler));
    }

    private void Notify(WorkflowSurfacePart part, object? previous, object? current)
    {
        if (!handlers.TryGetValue(part, out var list) || list.Count == 0)
        {
            return;
        }

        if (list.Count == 1)
        {
            list[0](previous, current);
            return;
        }

        // 订阅者可能在回调里退订：遍历快照。
        var snapshot = list.ToArray();
        foreach (var handler in snapshot)
        {
            handler(previous, current);
        }
    }

    private sealed class Subscription : IDisposable
    {
        private Action? release;

        public Subscription(Action release) => this.release = release;

        public void Dispose()
        {
            var action = release;
            release = null;
            action?.Invoke();
        }
    }
}

/// <summary>
/// Binds one part role to its attach and detach callbacks, so the subscriptions a part owns follow the registry.
/// </summary>
/// <typeparam name="T">The part type the surface resolved.</typeparam>
/// <remarks>
/// <para>
/// The binding attaches whatever is already registered when it is created, then keeps <b>at most one</b> part
/// attached: a replacement detaches the old value before attaching the new one, and <see cref="Dispose"/> detaches the
/// current value and stops listening. That is the invariant a surface restart needs — the set of live subscriptions
/// equals the set of resolved parts, whatever order resolution and binding happen in.
/// </para>
/// <para>
/// Must be used on the host's UI thread, like the registry it belongs to.
/// </para>
/// </remarks>
public sealed class SurfacePartBinding<T> : IDisposable where T : class
{
    private readonly SurfacePartRegistry registry;
    private readonly WorkflowSurfacePart part;
    private readonly Action<T> attach;
    private readonly Action<T> detach;
    private IDisposable? subscription;
    private T? current;

    /// <summary>Creates a binding for one role and attaches the part already registered for it, if any.</summary>
    /// <param name="registry">The surface's registry.</param>
    /// <param name="part">The role this binding owns.</param>
    /// <param name="attach">Subscribes to one resolved part.</param>
    /// <param name="detach">Unsubscribes from one resolved part; always the value this binding attached.</param>
    /// <exception cref="ArgumentNullException">Any argument is <see langword="null"/>.</exception>
    public SurfacePartBinding(
        SurfacePartRegistry registry, WorkflowSurfacePart part, Action<T> attach, Action<T> detach)
    {
        this.registry = registry ?? throw new ArgumentNullException(nameof(registry));
        this.part = part;
        this.attach = attach ?? throw new ArgumentNullException(nameof(attach));
        this.detach = detach ?? throw new ArgumentNullException(nameof(detach));

        subscription = registry.OnChanged(part, OnChanged);
        AttachCurrent(registry.Get<T>(part));
    }

    /// <summary>Gets the part this binding currently holds, or <see langword="null"/>.</summary>
    public T? Current => current;

    /// <inheritdoc />
    public void Dispose()
    {
        subscription?.Dispose();
        subscription = null;
        DetachCurrent();
    }

    private void OnChanged(object? oldValue, object? newValue)
    {
        // 以本绑定实际接上的值为准：注册表视角的 oldValue 可能早于绑定创建。
        if (ReferenceEquals(current, newValue))
        {
            return;
        }

        DetachCurrent();
        AttachCurrent(newValue as T);
    }

    private void AttachCurrent(T? value)
    {
        if (value is null)
        {
            return;
        }

        current = value;
        attach(value);
    }

    private void DetachCurrent()
    {
        if (current is null)
        {
            return;
        }

        var value = current;
        current = null;
        detach(value);
    }
}
