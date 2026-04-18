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
            component.WorkCommand,
            component.BroadcastCommand,
            component.ReverseBroadcastCommand
        ];

    public static void StandardCreateSlot(this IWorkflowNodeViewModel component, IWorkflowSlotViewModel slot)
    {
        if (component is null) return;
        var oldParent = slot.Parent;
        var newParent = component;
        if (component.Parent is null)
        {
            slot.Parent = newParent;
            component.Slots.Add(slot);
            return;
        }
        component.Parent.GetHelper().Submit(new WorkflowActionPair(
            () =>
            {
                slot.Parent = newParent;
                component.Slots.Add(slot);
            },
            () =>
            {
                slot.GetHelper().Delete();
                slot.Parent = oldParent;
                component.Slots.Remove(slot);
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

        List<IWorkflowNodeViewModel> nodes = [];
        foreach (var sender in component.Slots.ToArray())
        {
            ct.ThrowIfCancellationRequested();

            foreach (var receiver in sender.Targets.ToArray())
            {
                ct.ThrowIfCancellationRequested();

                var receiverNode = receiver.Parent;
                if (receiverNode is null)
                {
                    continue;
                }

                if (!await helper.ValidateBroadcastAsync(sender, receiver, parameter, ct).ConfigureAwait(false))
                {
                    continue;
                }

                nodes.Add(receiverNode);
            }
        }

        foreach (var node in nodes)
        {
            ct.ThrowIfCancellationRequested();
            node.WorkCommand.Execute(parameter);
        }
    }

    public static async Task StandardReverseBroadcastAsync(this IWorkflowNodeViewModel component, object? parameter, CancellationToken ct = default)
    {
        var helper = component?.GetHelper() ?? throw new ArgumentException($"Failed to obtain the Helper instance.");

        List<IWorkflowNodeViewModel> nodes = [];
        foreach (var receiver in component.Slots.ToArray())
        {
            ct.ThrowIfCancellationRequested();

            foreach (var sender in receiver.Sources.ToArray())
            {
                ct.ThrowIfCancellationRequested();

                var senderNode = sender.Parent;
                if (senderNode is null)
                {
                    continue;
                }

                if (!await helper.ValidateBroadcastAsync(sender, receiver, parameter, ct).ConfigureAwait(false))
                {
                    continue;
                }

                nodes.Add(senderNode);
            }
        }

        foreach (var node in nodes)
        {
            ct.ThrowIfCancellationRequested();
            node.WorkCommand.Execute(parameter);
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
        visited ??= new HashSet<IWorkflowNodeViewModel> { start };
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
        if (component is null || component.Parent is null) return;

        var tree = component.Parent;
        var oldParent = component.Parent;

        // 方案2核心修改：只收集"有效"的连接（两端节点都存在的连接）
        var connectionsToRemove = new List<IWorkflowLinkViewModel>();
        var slotConnections = new Dictionary<IWorkflowSlotViewModel, (HashSet<IWorkflowSlotViewModel> Targets, HashSet<IWorkflowSlotViewModel> Sources)>();

        foreach (var slot in component.Slots)
        {
            var validTargets = new HashSet<IWorkflowSlotViewModel>();
            var validSources = new HashSet<IWorkflowSlotViewModel>();

            // 只收集目标节点存在且在同一Tree中的连接
            foreach (var target in slot.Targets)
            {
                if (target.Parent?.Parent == tree) // 关键检查：确保目标节点在同一个Tree中且存在
                {
                    if (tree.LinksMap.TryGetValue(slot, out var dic) && dic.TryGetValue(target, out var link))
                    {
                        connectionsToRemove.Add(link);
                        validTargets.Add(target);
                    }
                }
            }

            // 只收集源节点存在且在同一Tree中的连接
            foreach (var source in slot.Sources)
            {
                if (source.Parent?.Parent == tree) // 关键检查：确保源节点在同一个Tree中且存在
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

        // 去重连接
        var distinctConnections = connectionsToRemove.Distinct().ToList();

        // 使用单个原子操作处理所有删除
        tree.GetHelper().Submit(new WorkflowActionPair(
            // Redo: 执行删除
            () =>
            {
                ExecuteNodeDeletion(tree, component, distinctConnections);
            },
            // Undo: 撤销删除
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
        // 第一阶段：解除所有连接关系
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

        // 第二阶段：解除Slot的父子关系
        foreach (var slot in node.Slots.ToArray())
        {
            slot.Parent = null;
        }

        // 第三阶段：删除Node自身
        tree.Nodes.Remove(node);
        node.Parent = null;

        // 第四阶段：批量更新所有受影响组件状态
        UpdateAllAffectedStates(connections, node.Slots);
    }

    private static void RestoreNode(
        IWorkflowTreeViewModel tree,
        IWorkflowNodeViewModel node,
        IWorkflowTreeViewModel oldParent,
        List<IWorkflowLinkViewModel> connections,
        Dictionary<IWorkflowSlotViewModel, (HashSet<IWorkflowSlotViewModel> Targets, HashSet<IWorkflowSlotViewModel> Sources)> slotConnections)
    {
        // 第一阶段：恢复Node自身
        node.Parent = oldParent;
        if (!tree.Nodes.Contains(node))
        {
            tree.Nodes.Add(node);
        }

        // 第二阶段：恢复Slot的父子关系
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

        // 第三阶段：恢复所有连接（现在可以安全恢复，因为都是有效连接）
        foreach (var link in connections)
        {
            var sender = link.Sender;
            var receiver = link.Receiver;

            // 恢复映射关系
            if (!tree.LinksMap.ContainsKey(sender))
            {
                tree.LinksMap[sender] = [];
            }
            tree.LinksMap[sender][receiver] = link;

            // 恢复集合
            if (!tree.Links.Contains(link))
            {
                tree.Links.Add(link);
            }

            // 恢复双向关系（避免重复添加）
            if (!sender.Targets.Contains(receiver))
            {
                sender.Targets.Add(receiver);
            }
            if (!receiver.Sources.Contains(sender))
            {
                receiver.Sources.Add(sender);
            }

            // 显示连接
            link.IsVisible = true;
        }

        // 第四阶段：批量更新所有受影响组件状态
        UpdateAllAffectedStates(connections, node.Slots);

        // 触发属性变更通知
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
