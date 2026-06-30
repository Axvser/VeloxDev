using VeloxDev.MVVM;

namespace VeloxDev.WorkflowSystem.Compilation;

/// <summary>
/// A single item in the compiled ordered list.
/// Wraps the actual node view model with execution control metadata.
/// </summary>
public sealed class CompiledItem
{
    private IDisposable? _errorSubscription;

    /// <summary>Unique identifier within the compilation result.</summary>
    public int Id { get; }

    /// <summary>The actual workflow node view model.</summary>
    public IWorkflowNodeViewModel Node { get; }

    /// <summary>Execution order (0-based position in the list).</summary>
    public int Order { get; internal set; }

    /// <summary>
    /// When set, on node failure the execution will redirect to the item with this ID.
    /// Similar to a catch block — the target node will be executed instead.
    /// </summary>
    public int? ErrorRedirectId { get; set; }

    /// <summary>Maximum retry attempts on failure (0 = no retry).</summary>
    public int MaxRetries { get; set; }

    // ── Loop support (only set when CycleHandling = Allow and a cycle exists) ──

    /// <summary>Whether this item is the entry point of a detected loop (the node where execution re-enters the cycle).</summary>
    public bool IsLoopEntry { get; internal set; }

    /// <summary>The item ID that closes the loop (the last node before jumping back to the loop entry). Only meaningful on the loop entry item.</summary>
    public int? LoopTailId { get; internal set; }

    /// <summary>Maximum consecutive loop iterations before an exception is thrown. 0 = unlimited.</summary>
    public int MaxLoopCount { get; set; }

    /// <summary>
    /// Optional circuit breaker invoked when the loop exceeds <see cref="MaxLoopCount"/> consecutive iterations.
    /// If null, a default <see cref="InvalidOperationException"/> is thrown.
    /// </summary>
    public Action<CompiledItem>? CircuitBreaker { get; set; }

    // ── Compile-time slot routing ───────────────────────────────────────

    /// <summary>
    /// Pre-compiled route table collected from <see cref="ICompileTimeRouter.GetRouteTable"/>.
    /// Key = condition/selector value, Value = target node.
    /// Null if the node does not implement <see cref="ICompileTimeRouter"/>.
    /// Using direct node references ensures the routing stays valid
    /// even if slot connections are later modified.
    /// </summary>
    public IReadOnlyDictionary<object, IWorkflowNodeViewModel>? RouteTable { get; internal set; }

    internal CompiledItem(int id, IWorkflowNodeViewModel node, int order)
    {
        Id = id;
        Node = node;
        Order = order;
    }

    /// <summary>
    /// Temporarily subscribe to the node's WorkCommand.Failed event to handle errors.
    /// The subscription is automatically disposed when the callback completes or when
    /// <see cref="UnsubscribeError"/> is called.
    /// </summary>
    public void SubscribeError(Action<CompiledItem, Exception> onFailed)
    {
        UnsubscribeError();
        var weakItem = new WeakReference<CompiledItem>(this);
        var weakAction = new WeakReference<Action<CompiledItem, Exception>>(onFailed);

        CommandEventHandler handler = null!;
        handler = e =>
        {
            if (weakItem.TryGetTarget(out var item) && weakAction.TryGetTarget(out var action))
            {
                action(item, e.Exception ?? new InvalidOperationException("Node execution failed"));
            }
        };

        Node.WorkCommand.Failed += handler;
        _errorSubscription = new DisposableAction(() => Node.WorkCommand.Failed -= handler);
    }

    /// <summary>Unsubscribe the error handler attached by <see cref="SubscribeError"/>.</summary>
    public void UnsubscribeError()
    {
        _errorSubscription?.Dispose();
        _errorSubscription = null;
    }

    private sealed class DisposableAction(Action dispose) : IDisposable
    {
        public void Dispose() => dispose();
    }
}
