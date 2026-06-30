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
    /// BFS/DFS depth level from the traversal start point.
    /// <see cref="ICompileTimeRouter"/> nodes do NOT increment the depth —
    /// their children inherit the router's depth level, making branch depth
    /// intuitively consistent with the logical graph topology.
    /// </summary>
    public int Depth { get; internal set; }

    /// <summary>
    /// When set, on node failure the execution will redirect to the item with this ID.
    /// Similar to a catch block — the target node will be executed instead.
    /// </summary>
    public int? ErrorRedirectId { get; set; }

    /// <summary>Maximum retry attempts on failure (0 = no retry).</summary>
    public int MaxRetries { get; set; }

    // ── Failure info ────────────────────────────────────────────────────

    /// <summary>
    /// Set by <see cref="SubscribeError"/> when the node's WorkCommand fires
    /// its <c>Failed</c> event. The executor checks this after
    /// <see cref="CompilationResult.ExecuteAsync"/> to decide whether to redirect.
    /// </summary>
    internal Exception? FailureException { get; set; }

    // ── Loop metadata (only set when CycleHandling = Allow and a cycle exists) ──

    /// <summary>
    /// Informational: set by the compiler when <c>CycleHandling.Allow</c> is active
    /// and a graph cycle is detected. Marks the node where execution re-enters the cycle.
    /// Not used by the executor at runtime — each compiled item appears exactly once.
    /// </summary>
    public bool IsLoopEntry { get; internal set; }

    /// <summary>
    /// Informational: the item ID that closes the loop (the last node before
    /// jumping back to the loop entry). Set alongside <see cref="IsLoopEntry"/>.
    /// </summary>
    public int? LoopTailId { get; internal set; }

    // ── Compile-time slot routing ───────────────────────────────────────

    /// <summary>
    /// Pre-compiled route table collected from <see cref="ICompileTimeRouter.GetRouteTable"/>.
    /// Key = condition/selector value, Value = target node.
    /// Null if the node does not implement <see cref="ICompileTimeRouter"/>.
    /// Using direct node references ensures the routing stays valid
    /// even if slot connections are later modified.
    /// </summary>
    public IReadOnlyDictionary<object, IWorkflowNodeViewModel>? RouteTable { get; internal set; }

    /// <summary>
    /// For router nodes (those with <see cref="RouteTable"/>), maps each route key
    /// to the set of item IDs that are exclusively reachable via that branch.
    /// Items in unchosen branches are skipped during execution.
    /// Null if the node is not a router or has no exclusive descendants.
    /// </summary>
    public Dictionary<object, HashSet<int>>? BranchExclusiveItems { get; internal set; }

    internal CompiledItem(int id, IWorkflowNodeViewModel node, int order)
    {
        Id = id;
        Node = node;
        Order = order;
    }

    /// <summary>
    /// Subscribe to the node's WorkCommand.Failed event.
    /// When the command fails, stores the exception in <see cref="FailureException"/>
    /// for the executor to handle asynchronously in the main loop.
    /// The subscription is automatically disposed when <see cref="UnsubscribeError"/> is called.
    /// </summary>
    public void SubscribeError()
    {
        UnsubscribeError();
        FailureException = null;
        var weakItem = new WeakReference<CompiledItem>(this);

        CommandEventHandler handler = null!;
        handler = e =>
        {
            if (weakItem.TryGetTarget(out var item))
            {
                item.FailureException = e.Exception ?? new InvalidOperationException("Node execution failed");
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
