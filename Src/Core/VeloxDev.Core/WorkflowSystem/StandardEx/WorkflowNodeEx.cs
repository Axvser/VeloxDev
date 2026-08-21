using System.Linq;
using VeloxDev.MVVM;

namespace VeloxDev.WorkflowSystem.StandardEx;

public static class WorkflowNodeEx
{
    public static IReadOnlyCollection<IVeloxCommand> GetStandardCommands
        (this IWorkflowNodeViewModel component)
        =>
        [
            component.SetAnchorCommand,
            component.SetSizeCommand,
            component.CreateSlotCommand,
            component.DeleteCommand,
            component.ReceiveCommand,
            component.BroadcastCommand,
            component.ReverseBroadcastCommand
        ];

    public static void StandardCreateSlot(this IWorkflowNodeViewModel component, IWorkflowSlotViewModel slot)
    {
        if (component is null) return;

        // Idempotency guard: a slot already registered with this node (same reference) is a
        // re-dispatch of a slot that construction or a prior CreateSlot already created. Without
        // this, a late deferred dispatch would take the attached branch below and push a phantom
        // undo entry whose Undo would then tear the slot out of the node.
        if (component.Slots.Any(s => ReferenceEquals(s, slot)))
            return;

        var oldParent = slot.Parent;
        var newParent = component;
        if (component.Parent is null)
        {
            slot.Parent = newParent;
            // Use reference-equality check instead of EqualityComparer<T>.Default
            // to guarantee no duplicate entries even if slot types override Equals.
            if (!component.Slots.Any(s => ReferenceEquals(s, slot)))
                component.Slots.Add(slot);
            return;
        }
        component.Parent.GetHelper().Submit(new WorkflowActionPair(
            () =>
            {
                slot.Parent = newParent;
                if (!component.Slots.Any(s => ReferenceEquals(s, slot)))
                    component.Slots.Add(slot);
            },
            () =>
            {
                component.Slots.Remove(slot);
                slot.Parent = oldParent;
            }));
    }

    public static void StandardSetAnchor(this IWorkflowNodeViewModel component, Anchor anchor)
    {
        if (component is null) return;
        component.Anchor.Horizontal = anchor.Horizontal;
        component.Anchor.Vertical = anchor.Vertical;
        component.Anchor.Layer = anchor.Layer;
        component.OnPropertyChanged(nameof(component.Anchor));
    }

    public static void StandardSetLayer(this IWorkflowNodeViewModel component, int layer)
    {
        if (component is null) return;
        component.Anchor.Layer = layer;
        component.OnPropertyChanged(nameof(component.Anchor));
    }
    public static void StandardSetSize(this IWorkflowNodeViewModel component, Size size)
    {
        if (component is null) return;
        component.Size.Width = size.Width;
        component.Size.Height = size.Height;
        component.OnPropertyChanged(nameof(component.Size));
    }

    public static void StandardMove(this IWorkflowNodeViewModel component, Offset offset)
    {
        if (component is null) return;
        component.Anchor.Horizontal += offset.Horizontal;
        component.Anchor.Vertical += offset.Vertical;
        component.OnPropertyChanged(nameof(component.Anchor));
    }

    public static async Task StandardBroadcastAsync(this IWorkflowNodeViewModel component, object? parameter, CancellationToken ct = default)
    {
        var helper = component?.GetHelper() ?? throw new ArgumentException($"Failed to obtain the Helper instance.");

        List<(IWorkflowNodeViewModel Node, ITaskContext Context)> nodes = [];
        foreach (var sender in component.Slots.ToArray())
        {
            ct.ThrowIfCancellationRequested();

            foreach (var receiver in sender.Targets.ToArray())
            {
                ct.ThrowIfCancellationRequested();

                var receiverNode = receiver.Parent;
                if (receiverNode is null) continue;

                // First build the task context for this delivery, then use it for runtime real-time validation (a failed validation is treated as unconnected).
                var ctx = new TaskContext(parameter, sender, receiver);
                if (!await helper.AccessAsync(ctx, ct).ConfigureAwait(false))
                    continue;

                nodes.Add((receiverNode, ctx));
            }
        }

        foreach (var (node, ctx) in nodes)
        {
            ct.ThrowIfCancellationRequested();
            node.ReceiveCommand.Execute(ctx);
        }
    }

    public static async Task StandardReverseBroadcastAsync(this IWorkflowNodeViewModel component, object? parameter, CancellationToken ct = default)
    {
        var helper = component?.GetHelper() ?? throw new ArgumentException($"Failed to obtain the Helper instance.");

        List<(IWorkflowNodeViewModel Node, ITaskContext Context)> nodes = [];
        foreach (var receiver in component.Slots.ToArray())
        {
            ct.ThrowIfCancellationRequested();

            foreach (var sender in receiver.Sources.ToArray())
            {
                ct.ThrowIfCancellationRequested();

                var senderNode = sender.Parent;
                if (senderNode is null) continue;

                // First build the task context for this delivery, then use it for runtime real-time validation (a failed validation is treated as unconnected).
                var ctx = new TaskContext(parameter, sender, receiver);
                if (!await helper.AccessAsync(ctx, ct).ConfigureAwait(false))
                    continue;

                nodes.Add((senderNode, ctx));
            }
        }

        foreach (var (node, ctx) in nodes)
        {
            ct.ThrowIfCancellationRequested();
            node.ReceiveCommand.Execute(ctx);
        }
    }

    /// <summary>
    /// Search downstream (forward) nodes along the propagation chain starting from the given node.
    /// Uses BFS with depth limiting and an optional predicate filter.
    /// </summary>
    /// <param name="component">The starting node.</param>
    /// <param name="predicate">Optional filter predicate. If null, all reachable nodes are returned.</param>
    /// <param name="maxDepth">Maximum search depth. 0 means unlimited.</param>
    /// <returns>An enumerable of matching downstream nodes (excluding the starting node).</returns>
    public static IEnumerable<IWorkflowNodeViewModel> SearchForwardNodes(
        this IWorkflowNodeViewModel component,
        Func<IWorkflowNodeViewModel, bool>? predicate = null,
        int maxDepth = 0)
    {
        return SearchRelativeNodesCore(component, forward: true, predicate, maxDepth);
    }

    /// <summary>
    /// Search upstream (reverse) nodes along the propagation chain starting from the given node.
    /// Uses BFS with depth limiting and an optional predicate filter.
    /// </summary>
    /// <param name="component">The starting node.</param>
    /// <param name="predicate">Optional filter predicate. If null, all reachable nodes are returned.</param>
    /// <param name="maxDepth">Maximum search depth. 0 means unlimited.</param>
    /// <returns>An enumerable of matching upstream nodes (excluding the starting node).</returns>
    public static IEnumerable<IWorkflowNodeViewModel> SearchReverseNodes(
        this IWorkflowNodeViewModel component,
        Func<IWorkflowNodeViewModel, bool>? predicate = null,
        int maxDepth = 0)
    {
        return SearchRelativeNodesCore(component, forward: false, predicate, maxDepth);
    }

    /// <summary>
    /// Search both forward and reverse nodes simultaneously along the propagation chain.
    /// </summary>
    /// <param name="component">The starting node.</param>
    /// <param name="predicate">Optional filter predicate. If null, all reachable nodes are returned.</param>
    /// <param name="maxDepth">Maximum search depth. 0 means unlimited.</param>
    /// <returns>An enumerable of matching nodes from both directions (excluding the starting node).</returns>
    public static IEnumerable<IWorkflowNodeViewModel> SearchAllRelativeNodes(
        this IWorkflowNodeViewModel component,
        Func<IWorkflowNodeViewModel, bool>? predicate = null,
        int maxDepth = 0)
    {
        var visited = new HashSet<IWorkflowNodeViewModel> { component };
        foreach (var node in SearchRelativeNodesCore(component, true, null, maxDepth, visited))
        {
            if (predicate is null || predicate(node)) yield return node;
        }
        foreach (var node in SearchRelativeNodesCore(component, false, null, maxDepth, visited))
        {
            if (predicate is null || predicate(node)) yield return node;
        }
    }

    private static IEnumerable<IWorkflowNodeViewModel> SearchRelativeNodesCore(
        IWorkflowNodeViewModel start,
        bool forward,
        Func<IWorkflowNodeViewModel, bool>? predicate,
        int maxDepth,
        HashSet<IWorkflowNodeViewModel>? visited = null)
    {
        visited ??= [start];
        var queue = new Queue<(IWorkflowNodeViewModel Node, int Depth)>();
        queue.Enqueue((start, 0));

        while (queue.Count > 0)
        {
            var (current, depth) = queue.Dequeue();

            if (maxDepth > 0 && depth >= maxDepth) continue;

            foreach (var slot in current.Slots)
            {
                var neighbors = forward ? slot.Targets : slot.Sources;
                foreach (var neighbor in neighbors)
                {
                    var neighborNode = neighbor.Parent;
                    if (neighborNode is null || !visited.Add(neighborNode)) continue;

                    if (predicate is null || predicate(neighborNode))
                    {
                        yield return neighborNode;
                    }

                    queue.Enqueue((neighborNode, depth + 1));
                }
            }
        }
    }

    public static void StandardDelete(this IWorkflowNodeViewModel component)
    {
        if (component is null) return;
        if (component.Parent is null)
        {
            WorkflowGuard.Fail("The node is not attached to a tree; deletion cannot be performed.");
            return;
        }

        var tree = component.Parent;
        var oldParent = component.Parent;

        // Core change of approach 2: collect only "valid" connections (connections where both endpoint nodes exist)
        var connectionsToRemove = new List<IWorkflowLinkViewModel>();
        var slotConnections = new Dictionary<IWorkflowSlotViewModel, (HashSet<IWorkflowSlotViewModel> Targets, HashSet<IWorkflowSlotViewModel> Sources)>();

        foreach (var slot in component.Slots)
        {
            var validTargets = new HashSet<IWorkflowSlotViewModel>();
            var validSources = new HashSet<IWorkflowSlotViewModel>();

            // Only collect connections whose target node exists in the same tree
            foreach (var target in slot.Targets)
            {
                if (target.Parent?.Parent == tree) // key check: ensure the target node exists in the same tree
                {
                    if (tree.LinksMap.TryGetValue(slot, out var dic) && dic.TryGetValue(target, out var link))
                    {
                        connectionsToRemove.Add(link);
                        validTargets.Add(target);
                    }
                }
            }

            // Only collect connections whose source node exists in the same tree
            foreach (var source in slot.Sources)
            {
                if (source.Parent?.Parent == tree) // key check: ensure the source node exists in the same tree
                {
                    if (tree.LinksMap.TryGetValue(source, out var dic) && dic.TryGetValue(slot, out var link))
                    {
                        connectionsToRemove.Add(link);
                        validSources.Add(source);
                    }
                }
            }

            slotConnections[slot] = (validTargets, validSources);
        }

        // Deduplicate the connections
        var distinctConnections = connectionsToRemove.Distinct().ToList();

        // Handle all deletions with a single atomic operation
        tree.GetHelper().Submit(new WorkflowActionPair(
            // Redo: perform the deletion
            () =>
            {
                ExecuteNodeDeletion(tree, component, distinctConnections);
            },
            // Undo: undo the deletion
            () =>
            {
                RestoreNode(tree, component, oldParent, distinctConnections, slotConnections);
            }
        ));
    }

    private static void ExecuteNodeDeletion(
        IWorkflowTreeViewModel tree,
        IWorkflowNodeViewModel node,
        List<IWorkflowLinkViewModel> connections)
    {
        // Phase 1: break all connection relations
        foreach (var link in connections)
        {
            var sender = link.Sender;
            var receiver = link.Receiver;

            if (tree.LinksMap.TryGetValue(sender, out var receiverDict))
            {
                receiverDict.Remove(receiver);
                if (receiverDict.Count == 0)
                {
                    tree.LinksMap.Remove(sender);
                }
            }

            tree.Links.Remove(link);
            sender.Targets.Remove(receiver);
            receiver.Sources.Remove(sender);
            link.IsVisible = false;
        }

        // Phase 2: break the slots' parent-child relations
        foreach (var slot in node.Slots.ToArray())
        {
            slot.Parent = null;
        }

        // Phase 3: delete the node itself
        tree.Nodes.Remove(node);
        node.Parent = null;

        // Phase 4: batch-update the state of all affected components
        UpdateAllAffectedStates(connections, node.Slots);
    }

    private static void RestoreNode(
        IWorkflowTreeViewModel tree,
        IWorkflowNodeViewModel node,
        IWorkflowTreeViewModel oldParent,
        List<IWorkflowLinkViewModel> connections,
        Dictionary<IWorkflowSlotViewModel, (HashSet<IWorkflowSlotViewModel> Targets, HashSet<IWorkflowSlotViewModel> Sources)> slotConnections)
    {
        // Phase 1: restore the node itself
        node.Parent = oldParent;
        if (!tree.Nodes.Contains(node))
        {
            tree.Nodes.Add(node);
        }

        // Phase 2: restore the slots' parent-child relations
        foreach (var slot in node.Slots)
        {
            slot.Parent = node;

            if (slotConnections.TryGetValue(slot, out var connectionsInfo))
            {
                if (connectionsInfo.Targets != null)
                {
                    foreach (var target in connectionsInfo.Targets)
                    {
                        if (!slot.Targets.Contains(target))
                        {
                            slot.Targets.Add(target);
                        }
                    }
                }

                if (connectionsInfo.Sources != null)
                {
                    foreach (var source in connectionsInfo.Sources)
                    {
                        if (!slot.Sources.Contains(source))
                        {
                            slot.Sources.Add(source);
                        }
                    }
                }
            }
        }

        // Phase 3: restore all connections (now safe because they are all valid)
        foreach (var link in connections)
        {
            var sender = link.Sender;
            var receiver = link.Receiver;

            // Restore the mapping
            if (!tree.LinksMap.ContainsKey(sender))
            {
                tree.LinksMap[sender] = [];
            }
            tree.LinksMap[sender][receiver] = link;

            // Restore the collections
            if (!tree.Links.Contains(link))
            {
                tree.Links.Add(link);
            }

            // Restore the bidirectional relations (avoiding duplicates)
            if (!sender.Targets.Contains(receiver))
            {
                sender.Targets.Add(receiver);
            }
            if (!receiver.Sources.Contains(sender))
            {
                receiver.Sources.Add(sender);
            }

            // Show the link
            link.IsVisible = true;
        }

        // Phase 4: batch-update the state of all affected components
        UpdateAllAffectedStates(connections, node.Slots);

        // Raise property-changed notifications
        node.OnPropertyChanged(nameof(node.Slots));
        foreach (var slot in node.Slots)
        {
            slot.OnPropertyChanged(nameof(slot.Targets));
            slot.OnPropertyChanged(nameof(slot.Sources));
            slot.GetHelper().UpdateState();
        }
    }

    private static void UpdateAllAffectedStates(
        List<IWorkflowLinkViewModel> connections,
        IList<IWorkflowSlotViewModel> slots)
    {
        var allAffectedSlots = new HashSet<IWorkflowSlotViewModel>(slots);

        foreach (var link in connections)
        {
            allAffectedSlots.Add(link.Sender);
            allAffectedSlots.Add(link.Receiver);
        }

        foreach (var slot in allAffectedSlots)
        {
            slot.GetHelper().UpdateState();
        }
    }
}
