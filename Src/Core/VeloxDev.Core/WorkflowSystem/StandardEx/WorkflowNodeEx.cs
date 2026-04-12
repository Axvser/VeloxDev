using VeloxDev.Core.MVVM;

namespace VeloxDev.Core.WorkflowSystem.StandardEx;

public static class WorkflowNodeEx
{
    private static readonly AsyncLocal<WorkflowBroadcastContext?> CurrentBroadcastContext = new();
    private static readonly AsyncLocal<WorkflowBroadcastContext?> CurrentReverseBroadcastContext = new();

    public static bool IsOrderedBroadcastInProgress()
        => CurrentBroadcastContext.Value is not null;

    public static bool IsOrderedReverseBroadcastInProgress()
        => CurrentReverseBroadcastContext.Value is not null;

    public static WorkflowBroadcastMode ResolveConfiguredBroadcastMode(this IWorkflowNodeViewModel component, WorkflowBroadcastMode inheritedMode)
        => component?.BroadcastMode ?? inheritedMode;

    public static WorkflowBroadcastMode ResolveConfiguredReverseBroadcastMode(this IWorkflowNodeViewModel component, WorkflowBroadcastMode inheritedMode)
        => component?.ReverseBroadcastMode ?? inheritedMode;

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
            slot.GetHelper().Delete();
            slot.Parent = newParent;
            slot.GetHelper().UpdateLayout();
            return;
        }
        component.Parent.GetHelper().Submit(new WorkflowActionPair(
            () =>
            {
                slot.Parent = newParent;
                slot.GetHelper().UpdateLayout();
                component.Slots.Add(slot);
            },
            () =>
            {
                slot.GetHelper().Delete();
                slot.Parent = oldParent;
                slot.GetHelper().UpdateLayout();
                component.Slots.Remove(slot);
            }));
    }

    public static void StandardSetAnchor(this IWorkflowNodeViewModel component, Anchor anchor)
    {
        if (component is null) return;
        component.Anchor.Left = anchor.Left;
        component.Anchor.Top = anchor.Top;
        component.Anchor.Layer = anchor.Layer;
        component.OnPropertyChanged(nameof(component.Anchor));
        foreach (var slot in component.Slots)
        {
            slot.GetHelper().UpdateLayout();
        }
    }

    public static async Task StandardReverseBroadcastAsync(this IWorkflowNodeViewModel component, object? parameter, WorkflowBroadcastMode mode = WorkflowBroadcastMode.Parallel, CancellationToken ct = default)
    {
        if (component is null || component.Parent is null) return;

        var previous = CurrentReverseBroadcastContext.Value;
        var effectiveMode = component.ResolveConfiguredReverseBroadcastMode(mode);

        if (previous is null && effectiveMode == WorkflowBroadcastMode.Parallel)
        {
            await StandardReverseBroadcastParallelAsync(component, parameter, ct).ConfigureAwait(false);
            return;
        }

        var isRoot = previous is null;
        var context = previous ?? new WorkflowBroadcastContext();

        if (isRoot)
        {
            CurrentReverseBroadcastContext.Value = context;
        }

        try
        {
            if (isRoot)
            {
                if (!context.TryVisit(component)) return;
                await ExecuteReverseNodeAsync(component, parameter, effectiveMode, context, ct).ConfigureAwait(false);
                return;
            }

            var sources = await GetValidSourceNodesAsync(component, parameter, context, ct).ConfigureAwait(false);
            await DispatchReverseNodesAsync(sources, parameter, effectiveMode, context, ct).ConfigureAwait(false);
        }
        finally
        {
            if (isRoot)
            {
                CurrentReverseBroadcastContext.Value = null;
            }
        }
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
        foreach (var slot in component.Slots)
        {
            slot.GetHelper().UpdateLayout();
        }
    }

    private static async Task StandardReverseBroadcastParallelAsync(IWorkflowNodeViewModel component, object? parameter, CancellationToken ct)
    {
        if (component is null || component.Parent is null) return;

        var validationTasks = new List<Task<(IWorkflowNodeViewModel Node, bool IsValid)>>();
        foreach (var receiver in component.Slots.ToArray())
        {
            foreach (var sender in receiver.Sources.ToArray())
            {
                var senderNode = sender.Parent;
                if (senderNode is null)
                {
                    continue;
                }

                validationTasks.Add(
                    senderNode.GetHelper().ValidateBroadcastAsync(sender, receiver, parameter, ct)
                        .ContinueWith(t => (senderNode, t.Result)));
            }
        }

        var validationResults = await Task.WhenAll(validationTasks).ConfigureAwait(false);
        foreach (var (node, isValid) in validationResults)
        {
            if (isValid)
            {
                node.WorkCommand.Execute(parameter);
            }
        }
    }
    public static void StandardMove(this IWorkflowNodeViewModel component, Offset offset)
    {
        if (component is null) return;
        component.Anchor.Left += offset.Left;
        component.Anchor.Top += offset.Top;
        component.OnPropertyChanged(nameof(component.Anchor));
        foreach (var slot in component.Slots)
        {
            slot.GetHelper().UpdateLayout();
        }
    }

    public static async Task StandardBroadcastAsync(this IWorkflowNodeViewModel component, object? parameter, WorkflowBroadcastMode mode = WorkflowBroadcastMode.Parallel, CancellationToken ct = default)
    {
        if (component?.GetHelper() is null) throw new ArgumentException($"Failed to obtain the Helper instance.");

        var previous = CurrentBroadcastContext.Value;
        var effectiveMode = component.ResolveConfiguredBroadcastMode(mode);

        if (previous is null && effectiveMode == WorkflowBroadcastMode.Parallel)
        {
            await StandardBroadcastParallelAsync(component, parameter, ct).ConfigureAwait(false);
            return;
        }

        var isRoot = previous is null;
        var context = previous ?? new WorkflowBroadcastContext();

        if (isRoot)
        {
            CurrentBroadcastContext.Value = context;
        }

        try
        {
            if (isRoot)
            {
                if (!context.TryVisit(component)) return;
                await ExecuteForwardNodeAsync(component, parameter, effectiveMode, context, ct).ConfigureAwait(false);
                return;
            }

            var receivers = await GetValidReceiverNodesAsync(component, parameter, context, ct).ConfigureAwait(false);
            await DispatchForwardNodesAsync(receivers, parameter, effectiveMode, context, ct).ConfigureAwait(false);
        }
        finally
        {
            if (isRoot)
            {
                CurrentBroadcastContext.Value = null;
            }
        }
    }

    private static async Task StandardBroadcastParallelAsync(IWorkflowNodeViewModel component, object? parameter, CancellationToken ct)
    {
        if (component is null || component.Parent is null) return;

        var senders = component.Slots.ToArray();
        var validationTasks = new List<Task<(IWorkflowSlotViewModel Receiver, bool IsValid)>>();

        // 创建所有 ValidateBroadcastAsync 任务
        foreach (var sender in senders)
        {
            var receivers = sender.Targets.ToArray();
            foreach (var receiver in receivers)
            {
                // 记录 receiver，以便后续执行 WorkCommand
                validationTasks.Add(
                    component.GetHelper().ValidateBroadcastAsync(sender, receiver, parameter, ct)
                        .ContinueWith(t => (receiver, t.Result))
                );
            }
        }

        // 等待所有验证任务完成
        var validationResults = await Task.WhenAll(validationTasks);

        // 对每个通过验证的 receiver 执行 WorkCommand
        foreach (var (receiver, isValid) in validationResults)
        {
            if (isValid)
            {
                receiver.Parent?.WorkCommand.Execute(parameter);
            }
        }
    }

    private static async Task<List<IWorkflowNodeViewModel>> GetValidReceiverNodesAsync(
        IWorkflowNodeViewModel component,
        object? parameter,
        WorkflowBroadcastContext context,
        CancellationToken ct)
    {
        List<IWorkflowNodeViewModel> result = [];
        foreach (var sender in component.Slots.ToArray())
        {
            foreach (var receiver in sender.Targets.ToArray())
            {
                var receiverNode = receiver.Parent;
                if (receiverNode is null)
                {
                    continue;
                }

                if (!await component.GetHelper().ValidateBroadcastAsync(sender, receiver, parameter, ct).ConfigureAwait(false))
                {
                    continue;
                }

                if (context is not null && context.HasVisited(receiverNode))
                {
                    continue;
                }

                result.Add(receiverNode);
            }
        }

        return result;
    }

    private static async Task<List<IWorkflowNodeViewModel>> GetValidSourceNodesAsync(
        IWorkflowNodeViewModel component,
        object? parameter,
        WorkflowBroadcastContext context,
        CancellationToken ct)
    {
        List<IWorkflowNodeViewModel> result = [];
        foreach (var receiver in component.Slots.ToArray())
        {
            foreach (var sender in receiver.Sources.ToArray())
            {
                var senderNode = sender.Parent;
                if (senderNode is null)
                {
                    continue;
                }

                if (!await senderNode.GetHelper().ValidateBroadcastAsync(sender, receiver, parameter, ct).ConfigureAwait(false))
                {
                    continue;
                }

                if (context is not null && context.HasVisited(senderNode))
                {
                    continue;
                }

                result.Add(senderNode);
            }
        }

        return result;
    }

    private static async Task ExecuteForwardNodeAsync(
        IWorkflowNodeViewModel node,
        object? parameter,
        WorkflowBroadcastMode inheritedMode,
        WorkflowBroadcastContext context,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        await ExecuteQueuedWorkAsync(node, parameter, ct).ConfigureAwait(false);
        var nextMode = node.ResolveConfiguredBroadcastMode(inheritedMode);
        var receivers = await GetValidReceiverNodesAsync(node, parameter, context, ct).ConfigureAwait(false);
        await DispatchForwardNodesAsync(receivers, parameter, nextMode, context, ct).ConfigureAwait(false);
    }

    private static async Task ExecuteReverseNodeAsync(
        IWorkflowNodeViewModel node,
        object? parameter,
        WorkflowBroadcastMode inheritedMode,
        WorkflowBroadcastContext context,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        await ExecuteQueuedWorkAsync(node, parameter, ct).ConfigureAwait(false);
        var nextMode = node.ResolveConfiguredReverseBroadcastMode(inheritedMode);
        var sources = await GetValidSourceNodesAsync(node, parameter, context, ct).ConfigureAwait(false);
        await DispatchReverseNodesAsync(sources, parameter, nextMode, context, ct).ConfigureAwait(false);
    }

    private static async Task DispatchForwardNodesAsync(
        IReadOnlyList<IWorkflowNodeViewModel> nodes,
        object? parameter,
        WorkflowBroadcastMode mode,
        WorkflowBroadcastContext context,
        CancellationToken ct)
    {
        var scheduled = ScheduleNodes(nodes, mode, context);
        if (scheduled.Count == 0)
        {
            return;
        }

        switch (mode)
        {
            case WorkflowBroadcastMode.Parallel:
                await Task.WhenAll(scheduled.Select(item => ExecuteForwardNodeAsync(item.Node, parameter, item.Mode, context, ct))).ConfigureAwait(false);
                break;
            case WorkflowBroadcastMode.BreadthFirst:
                await ProcessBreadthFirstForwardAsync(scheduled, parameter, context, ct).ConfigureAwait(false);
                break;
            case WorkflowBroadcastMode.DepthFirst:
                await ProcessDepthFirstForwardAsync(scheduled, parameter, context, ct).ConfigureAwait(false);
                break;
        }
    }

    private static async Task DispatchReverseNodesAsync(
        IReadOnlyList<IWorkflowNodeViewModel> nodes,
        object? parameter,
        WorkflowBroadcastMode mode,
        WorkflowBroadcastContext context,
        CancellationToken ct)
    {
        var scheduled = ScheduleNodes(nodes, mode, context);
        if (scheduled.Count == 0)
        {
            return;
        }

        switch (mode)
        {
            case WorkflowBroadcastMode.Parallel:
                await Task.WhenAll(scheduled.Select(item => ExecuteReverseNodeAsync(item.Node, parameter, item.Mode, context, ct))).ConfigureAwait(false);
                break;
            case WorkflowBroadcastMode.BreadthFirst:
                await ProcessBreadthFirstReverseAsync(scheduled, parameter, context, ct).ConfigureAwait(false);
                break;
            case WorkflowBroadcastMode.DepthFirst:
                await ProcessDepthFirstReverseAsync(scheduled, parameter, context, ct).ConfigureAwait(false);
                break;
        }
    }

    private static async Task ProcessBreadthFirstForwardAsync(
        IReadOnlyList<WorkflowDispatchItem> initialItems,
        object? parameter,
        WorkflowBroadcastContext context,
        CancellationToken ct)
    {
        var queue = new Queue<WorkflowDispatchItem>(initialItems);
        while (queue.Count > 0)
        {
            var item = queue.Dequeue();
            await ExecuteQueuedWorkAsync(item.Node, parameter, ct).ConfigureAwait(false);
            var nextMode = item.Node.ResolveConfiguredBroadcastMode(item.Mode);
            var receivers = await GetValidReceiverNodesAsync(item.Node, parameter, context, ct).ConfigureAwait(false);
            var scheduled = ScheduleNodes(receivers, nextMode, context);
            if (scheduled.Count == 0)
            {
                continue;
            }

            switch (nextMode)
            {
                case WorkflowBroadcastMode.Parallel:
                    await Task.WhenAll(scheduled.Select(next => ExecuteForwardNodeAsync(next.Node, parameter, next.Mode, context, ct))).ConfigureAwait(false);
                    break;
                case WorkflowBroadcastMode.BreadthFirst:
                    foreach (var next in scheduled)
                    {
                        queue.Enqueue(next);
                    }
                    break;
                case WorkflowBroadcastMode.DepthFirst:
                    await ProcessDepthFirstForwardAsync(scheduled, parameter, context, ct).ConfigureAwait(false);
                    break;
            }
        }
    }

    private static async Task ProcessDepthFirstForwardAsync(
        IReadOnlyList<WorkflowDispatchItem> initialItems,
        object? parameter,
        WorkflowBroadcastContext context,
        CancellationToken ct)
    {
        var stack = new Stack<WorkflowDispatchItem>();
        PushItems(stack, initialItems);
        while (stack.Count > 0)
        {
            var item = stack.Pop();
            await ExecuteQueuedWorkAsync(item.Node, parameter, ct).ConfigureAwait(false);
            var nextMode = item.Node.ResolveConfiguredBroadcastMode(item.Mode);
            var receivers = await GetValidReceiverNodesAsync(item.Node, parameter, context, ct).ConfigureAwait(false);
            var scheduled = ScheduleNodes(receivers, nextMode, context);
            if (scheduled.Count == 0)
            {
                continue;
            }

            switch (nextMode)
            {
                case WorkflowBroadcastMode.Parallel:
                    await Task.WhenAll(scheduled.Select(next => ExecuteForwardNodeAsync(next.Node, parameter, next.Mode, context, ct))).ConfigureAwait(false);
                    break;
                case WorkflowBroadcastMode.BreadthFirst:
                    await ProcessBreadthFirstForwardAsync(scheduled, parameter, context, ct).ConfigureAwait(false);
                    break;
                case WorkflowBroadcastMode.DepthFirst:
                    PushItems(stack, scheduled);
                    break;
            }
        }
    }

    private static async Task ProcessBreadthFirstReverseAsync(
        IReadOnlyList<WorkflowDispatchItem> initialItems,
        object? parameter,
        WorkflowBroadcastContext context,
        CancellationToken ct)
    {
        var queue = new Queue<WorkflowDispatchItem>(initialItems);
        while (queue.Count > 0)
        {
            var item = queue.Dequeue();
            await ExecuteQueuedWorkAsync(item.Node, parameter, ct).ConfigureAwait(false);
            var nextMode = item.Node.ResolveConfiguredReverseBroadcastMode(item.Mode);
            var sources = await GetValidSourceNodesAsync(item.Node, parameter, context, ct).ConfigureAwait(false);
            var scheduled = ScheduleNodes(sources, nextMode, context);
            if (scheduled.Count == 0)
            {
                continue;
            }

            switch (nextMode)
            {
                case WorkflowBroadcastMode.Parallel:
                    await Task.WhenAll(scheduled.Select(next => ExecuteReverseNodeAsync(next.Node, parameter, next.Mode, context, ct))).ConfigureAwait(false);
                    break;
                case WorkflowBroadcastMode.BreadthFirst:
                    foreach (var next in scheduled)
                    {
                        queue.Enqueue(next);
                    }
                    break;
                case WorkflowBroadcastMode.DepthFirst:
                    await ProcessDepthFirstReverseAsync(scheduled, parameter, context, ct).ConfigureAwait(false);
                    break;
            }
        }
    }

    private static async Task ProcessDepthFirstReverseAsync(
        IReadOnlyList<WorkflowDispatchItem> initialItems,
        object? parameter,
        WorkflowBroadcastContext context,
        CancellationToken ct)
    {
        var stack = new Stack<WorkflowDispatchItem>();
        PushItems(stack, initialItems);
        while (stack.Count > 0)
        {
            var item = stack.Pop();
            await ExecuteQueuedWorkAsync(item.Node, parameter, ct).ConfigureAwait(false);
            var nextMode = item.Node.ResolveConfiguredReverseBroadcastMode(item.Mode);
            var sources = await GetValidSourceNodesAsync(item.Node, parameter, context, ct).ConfigureAwait(false);
            var scheduled = ScheduleNodes(sources, nextMode, context);
            if (scheduled.Count == 0)
            {
                continue;
            }

            switch (nextMode)
            {
                case WorkflowBroadcastMode.Parallel:
                    await Task.WhenAll(scheduled.Select(next => ExecuteReverseNodeAsync(next.Node, parameter, next.Mode, context, ct))).ConfigureAwait(false);
                    break;
                case WorkflowBroadcastMode.BreadthFirst:
                    await ProcessBreadthFirstReverseAsync(scheduled, parameter, context, ct).ConfigureAwait(false);
                    break;
                case WorkflowBroadcastMode.DepthFirst:
                    PushItems(stack, scheduled);
                    break;
            }
        }
    }

    private static List<WorkflowDispatchItem> ScheduleNodes(
        IEnumerable<IWorkflowNodeViewModel> nodes,
        WorkflowBroadcastMode mode,
        WorkflowBroadcastContext context)
    {
        var scheduled = new List<WorkflowDispatchItem>();
        foreach (var node in nodes)
        {
            if (!context.TryVisit(node))
            {
                continue;
            }

            scheduled.Add(new WorkflowDispatchItem(node, mode));
        }

        return scheduled;
    }

    private static void PushItems(Stack<WorkflowDispatchItem> stack, IReadOnlyList<WorkflowDispatchItem> items)
    {
        for (var i = items.Count - 1; i >= 0; i--)
        {
            stack.Push(items[i]);
        }
    }

    private static async Task ExecuteQueuedWorkAsync(IWorkflowNodeViewModel node, object? parameter, CancellationToken ct)
    {
        var command = node.WorkCommand;
        var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationTokenRegistration registration = default;
        var terminalRaised = false;

        CommandEventHandler? onCompleted = null;
        CommandEventHandler? onFailed = null;
        CommandEventHandler? onCanceled = null;
        CommandEventHandler? onExited = null;

        void Cleanup()
        {
            command.Completed -= onCompleted!;
            command.Failed -= onFailed!;
            command.Canceled -= onCanceled!;
            command.Exited -= onExited!;
            registration.Dispose();
        }

        onCompleted = _ =>
        {
            terminalRaised = true;
            Cleanup();
            tcs.TrySetResult(null);
        };
        onFailed = e =>
        {
            terminalRaised = true;
            Cleanup();
            tcs.TrySetException(e.Exception ?? new InvalidOperationException("The workflow node work command failed."));
        };
        onCanceled = _ =>
        {
            terminalRaised = true;
            Cleanup();
            tcs.TrySetCanceled(ct);
        };
        onExited = _ =>
        {
            if (terminalRaised)
            {
                return;
            }

            Cleanup();
            tcs.TrySetResult(null);
        };

        command.Completed += onCompleted;
        command.Failed += onFailed;
        command.Canceled += onCanceled;
        command.Exited += onExited;

        if (ct.CanBeCanceled)
        {
            registration = ct.Register(() =>
            {
                Cleanup();
                tcs.TrySetCanceled(ct);
            });
        }

        _ = command.ExecuteAsync(parameter);
        await tcs.Task.ConfigureAwait(false);
    }

    private sealed class WorkflowDispatchItem(IWorkflowNodeViewModel node, WorkflowBroadcastMode mode)
    {
        public IWorkflowNodeViewModel Node { get; } = node;
        public WorkflowBroadcastMode Mode { get; } = mode;
    }

    private sealed class WorkflowBroadcastContext
    {
        private readonly object _syncRoot = new();
        private readonly HashSet<IWorkflowNodeViewModel> _visited = [];

        public bool HasVisited(IWorkflowNodeViewModel node)
        {
            lock (_syncRoot)
            {
                return _visited.Contains(node);
            }
        }

        public bool TryVisit(IWorkflowNodeViewModel node)
        {
            lock (_syncRoot)
            {
                return _visited.Add(node);
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
