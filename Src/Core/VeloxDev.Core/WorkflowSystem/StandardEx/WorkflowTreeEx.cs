using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using VeloxDev.Core.Interfaces.MVVM;
using VeloxDev.Core.Interfaces.WorkflowSystem;

namespace VeloxDev.Core.WorkflowSystem.StandardEx;

#pragma warning disable

public static class WorkflowTreeEx
{
    private static readonly ConditionalWeakTable<IWorkflowTreeViewModel, TreeCache> _cache = new();

    public static IReadOnlyCollection<IVeloxCommand> GetStandardCommands(this IWorkflowTreeViewModel component)
        =>
        [
            component.CreateNodeCommand,
            component.SetPointerCommand,
            component.ResetVirtualLinkCommand,
            component.ApplyConnectionCommand,
            component.ReceiveConnectionCommand,
            component.SubmitCommand,
            component.RedoCommand,
            component.UndoCommand
        ];

    public static void StandardCreateNode(this IWorkflowTreeViewModel component, IWorkflowNodeViewModel node)
    {
        var oldParent = node.Parent;
        var newParent = component;
        node.GetHelper().Delete();
        component.StandardSubmit(new WorkflowActionPair(
            () => CreateNodeRedo(component, node, newParent),
            () => CreateNodeUndo(component, node, oldParent)));
    }

    public static void StandardSetPointer(this IWorkflowTreeViewModel component, Anchor anchor)
    {
        component.VirtualLink.Receiver.Anchor = anchor;
        component.VirtualLink.OnPropertyChanged(nameof(component.VirtualLink.Receiver));
        component.OnPropertyChanged(nameof(component.VirtualLink));
    }

    public static async Task StandardCloseAsync(this IWorkflowTreeViewModel component)
    {
        component.GetHelper().Closing();

        foreach (var node in component.Nodes)
        {
            node.GetHelper().Closing();
            foreach (var slot in node.Slots)
            {
                slot.GetHelper().Closing();
            }
        }
        foreach (var link in component.Links)
        {
            link.GetHelper().Closing();
        }

        foreach (var node in component.Nodes)
        {
            await node.GetHelper().CloseAsync().ConfigureAwait(false);
            foreach (var slot in node.Slots)
            {
                await slot.GetHelper().CloseAsync().ConfigureAwait(false);
            }
        }
        foreach (var link in component.Links)
        {
            await link.GetHelper().CloseAsync().ConfigureAwait(false);
        }

        foreach (var node in component.Nodes)
        {
            node.GetHelper().Closed();
            foreach (var slot in node.Slots)
            {
                slot.GetHelper().Closed();
            }
        }
        foreach (var link in component.Links)
        {
            link.GetHelper().Closed();
        }

        component.GetHelper().Closed();
    }

    #region Connection Manager Extensions

    public static void StandardApplyConnection(this IWorkflowTreeViewModel component, IWorkflowSlotViewModel slot)
    {
        var cache = GetCache(component);

        // 1. 检查发送端能力
        bool canBeSender = slot.StandardCanBeSender();
        if (!canBeSender)
        {
            component.StandardResetVirtualLink();
            cache.CurrentSender = null;
            return;
        }

        // 2. 根据发送端通道类型智能清理连接
        component.StandardSmartCleanupSenderConnections(slot);

        // 3. 设置虚拟连接
        component.VirtualLink.Sender.Anchor = slot.Anchor;
        component.VirtualLink.Receiver.Anchor = slot.Anchor;
        component.VirtualLink.IsVisible = true;

        // 4. 更新状态
        cache.CurrentSender = slot;
        slot.State = SlotState.PreviewSender;
        slot.GetHelper().UpdateState();
    }

    public static void StandardReceiveConnection(this IWorkflowTreeViewModel component, IWorkflowSlotViewModel slot)
    {
        var cache = GetCache(component);
        if (cache.CurrentSender == null) return;

        // 检查接收端能力
        bool canBeReceiver = slot.StandardCanBeReceiver();
        if (!canBeReceiver)
        {
            component.StandardResetVirtualLink();
            return;
        }

        // 检查用户自定义验证逻辑
        if (!component.GetHelper().ValidateConnection(cache.CurrentSender, slot))
        {
            component.StandardResetVirtualLink();
            cache.CurrentSender = null;
            return;
        }

        // 检查同节点内连接
        if (cache.CurrentSender.Parent == slot.Parent)
        {
            component.StandardResetVirtualLink();
            cache.CurrentSender = null;
            return;
        }

        // 启用硬性规定：检查并清理同向连接冲突
        component.StandardCleanupSameDirectionConnections(cache.CurrentSender, slot);

        // 根据接收端通道类型智能清理连接
        component.StandardSmartCleanupReceiverConnections(slot);

        // 创建新连接
        component.StandardCreateNewConnection(cache.CurrentSender, slot);

        // 重置状态
        component.StandardResetVirtualLink();
        cache.CurrentSender = null;
    }

    public static void StandardResetVirtualLink(this IWorkflowTreeViewModel component)
    {
        var cache = GetCache(component);

        component.VirtualLink.Sender.Anchor = new Anchor();
        component.VirtualLink.Receiver.Anchor = new Anchor();
        component.VirtualLink.IsVisible = false;

        if (cache.CurrentSender != null)
        {
            cache.CurrentSender.State &= ~SlotState.PreviewSender;
            cache.CurrentSender.GetHelper().UpdateState();
        }

        cache.CurrentSender = null;
    }
    #endregion

    #region Redo & Undo Extensions
    public static void StandardRedo(this IWorkflowTreeViewModel component)
    {
        var cache = GetCache(component);
        lock (cache.StackLock)
        {
            if (cache.RedoStack.TryPop(out var pair))
            {
                try
                {
                    pair.Redo.Invoke();
                    cache.UndoStack.Push(pair);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex);
                }
            }
        }
    }

    public static void StandardSubmit(this IWorkflowTreeViewModel component, IWorkflowActionPair actionPair)
    {
        var cache = GetCache(component);
        lock (cache.StackLock)
        {
            try
            {
                actionPair.Redo.Invoke();
                cache.UndoStack.Push(actionPair);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
            }
        }
    }

    public static void StandardUndo(this IWorkflowTreeViewModel component)
    {
        var cache = GetCache(component);
        lock (cache.StackLock)
        {
            if (cache.UndoStack.TryPop(out var pair))
            {
                try
                {
                    pair.Undo.Invoke();
                    cache.RedoStack.Push(pair);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex);
                }
            }
        }
    }

    public static void StandardClearHistory(this IWorkflowTreeViewModel component)
    {
        var cache = GetCache(component);
        lock (cache.StackLock)
        {
            cache.RedoStack.Clear();
            cache.UndoStack.Clear();
        }
    }
    #endregion

    #region Private Helper Methods
    private static void CreateNodeRedo(IWorkflowTreeViewModel component, IWorkflowNodeViewModel node, IWorkflowTreeViewModel newParent)
    {
        node.Parent = newParent;
        component.Nodes.Add(node);
    }

    private static void CreateNodeUndo(IWorkflowTreeViewModel component, IWorkflowNodeViewModel node, IWorkflowTreeViewModel? oldParent)
    {
        node.GetHelper().Delete();
        node.Parent = oldParent;
        component.Nodes.Remove(node);
    }

    private static void StandardSmartCleanupSenderConnections(this IWorkflowTreeViewModel component, IWorkflowSlotViewModel sender)
    {
        if (component == null) return;

        // 收集所有需要清理的连接
        var connectionsToRemove = new List<(IWorkflowSlotViewModel, IWorkflowSlotViewModel, IWorkflowLinkViewModel)>();

        // 1. 清理发送端作为源头的连接（发送到目标的连接）
        if (component.LinksMap.TryGetValue(sender, out var targetLinks))
        {
            foreach (var kvp in targetLinks)
            {
                connectionsToRemove.Add((sender, kvp.Key, kvp.Value));
            }
        }

        // 2. 对于OneBoth通道，还需要清理发送端作为目标的连接（从其他Slot接收的连接）
        if (sender.Channel.HasFlag(SlotChannel.OneBoth))
        {
            // 查找所有以这个Slot为目标的连接
            foreach (var sourceDict in component.LinksMap)
            {
                if (sourceDict.Value.TryGetValue(sender, out var link))
                {
                    connectionsToRemove.Add((sourceDict.Key, sender, link));
                }
            }
        }

        // 根据发送端通道类型决定是否清理
        bool shouldCleanup = ShouldCleanupConnections(sender.Channel, isSender: true, existingConnections: connectionsToRemove.Count);

        if (shouldCleanup && connectionsToRemove.Count > 0)
        {
            component.StandardRemoveConnections(connectionsToRemove);
        }
    }

    private static void StandardSmartCleanupReceiverConnections(this IWorkflowTreeViewModel component, IWorkflowSlotViewModel receiver)
    {
        if (component == null) return;

        // 收集所有需要清理的连接
        var connectionsToRemove = new List<(IWorkflowSlotViewModel, IWorkflowSlotViewModel, IWorkflowLinkViewModel)>();

        // 1. 清理接收端作为目标的连接（从其他Slot接收的连接）
        foreach (var senderDict in component.LinksMap)
        {
            if (senderDict.Value.TryGetValue(receiver, out var link))
            {
                connectionsToRemove.Add((senderDict.Key, receiver, link));
            }
        }

        // 2. 对于OneBoth通道，还需要清理接收端作为源头的连接（发送到其他Slot的连接）
        if (receiver.Channel.HasFlag(SlotChannel.OneBoth))
        {
            // 查找所有从这个Slot出发的连接
            if (component.LinksMap.TryGetValue(receiver, out var targetLinks))
            {
                foreach (var kvp in targetLinks)
                {
                    connectionsToRemove.Add((receiver, kvp.Key, kvp.Value));
                }
            }
        }

        // 根据接收端通道类型决定是否清理
        bool shouldCleanup = ShouldCleanupConnections(receiver.Channel, isSender: false, existingConnections: connectionsToRemove.Count);

        if (shouldCleanup && connectionsToRemove.Count > 0)
        {
            component.StandardRemoveConnections(connectionsToRemove);
        }
    }

    private static void StandardCleanupSameDirectionConnections(this IWorkflowTreeViewModel component,
        IWorkflowSlotViewModel newSender, IWorkflowSlotViewModel newReceiver)
    {
        if (component == null || newSender.Parent == null || newReceiver.Parent == null) return;

        var senderNode = newSender.Parent;
        var receiverNode = newReceiver.Parent;

        // 收集需要清理的同向连接
        var sameDirectionConnections = new List<(IWorkflowSlotViewModel Sender, IWorkflowSlotViewModel Receiver, IWorkflowLinkViewModel Link)>();

        // 查找所有从senderNode到receiverNode的连接
        foreach (var potentialSender in senderNode.Slots)
        {
            if (component.LinksMap.TryGetValue(potentialSender, out var targetLinks))
            {
                foreach (var potentialReceiver in receiverNode.Slots)
                {
                    if (targetLinks.TryGetValue(potentialReceiver, out var existingLink))
                    {
                        // 排除当前正在创建的新连接
                        if (!(potentialSender == newSender && potentialReceiver == newReceiver))
                        {
                            sameDirectionConnections.Add((potentialSender, potentialReceiver, existingLink));
                        }
                    }
                }
            }
        }

        // 执行清理（保留最新的连接）
        if (sameDirectionConnections.Count > 0)
        {
            component.StandardRemoveConnections(sameDirectionConnections);
        }
    }

    private static void StandardCreateNewConnection(this IWorkflowTreeViewModel component,
        IWorkflowSlotViewModel sender, IWorkflowSlotViewModel receiver)
    {
        if (component is null || sender is null || receiver is null) return;

        // 检查是否已存在连接（经过清理后应该不存在）
        bool connectionExists = component.LinksMap.TryGetValue(sender, out var existingLinks) &&
                               existingLinks.ContainsKey(receiver);

        if (connectionExists) return;

        // 创建新连接
        var newLink = component.GetHelper().CreateLink(sender, receiver);
        newLink.IsVisible = true;

        component.StandardSubmit(new WorkflowActionPair(
            () =>
            {
                if (!component.LinksMap.ContainsKey(sender))
                    component.LinksMap[sender] = [];

                component.LinksMap[sender][receiver] = newLink;
                component.Links.Add(newLink);

                if (!sender.Targets.Contains(receiver))
                    sender.Targets.Add(receiver);
                if (!receiver.Sources.Contains(sender))
                    receiver.Sources.Add(sender);

                sender.GetHelper().UpdateState();
                receiver.GetHelper().UpdateState();

                newLink.IsVisible = true;
            },
            () =>
            {
                component.Links.Remove(newLink);
                if (component.LinksMap.ContainsKey(sender))
                {
                    component.LinksMap[sender].Remove(receiver);
                    if (component.LinksMap[sender].Count == 0)
                        component.LinksMap.Remove(sender);
                }

                sender.Targets.Remove(receiver);
                receiver.Sources.Remove(sender);

                sender.GetHelper().UpdateState();
                receiver.GetHelper().UpdateState();

                newLink.IsVisible = false;
            }
        ));
    }

    private static void StandardRemoveConnections(this IWorkflowTreeViewModel component,
        List<(IWorkflowSlotViewModel Sender, IWorkflowSlotViewModel Receiver, IWorkflowLinkViewModel Link)> connectionsToRemove)
    {
        if (component is null || connectionsToRemove.Count == 0)
            return;

        var redoActions = new List<Action>();
        var undoActions = new List<Action>();

        // 收集所有受影响的插槽，用于统一状态更新
        var affectedSlots = new HashSet<IWorkflowSlotViewModel>();

        foreach (var (sender, receiver, link) in connectionsToRemove)
        {
            affectedSlots.Add(sender);
            affectedSlots.Add(receiver);

            redoActions.Add(() =>
            {
                // 从 Links 集合中移除连接
                component.Links.Remove(link);

                // 从 LinksMap 中移除连接映射
                if (component.LinksMap.TryGetValue(sender, out var receiverLinks))
                {
                    receiverLinks.Remove(receiver);
                    if (receiverLinks.Count == 0)
                    {
                        component.LinksMap.Remove(sender);
                    }
                }

                // 更新发送端的目标集合
                sender.Targets.Remove(receiver);

                // 更新接收端的源集合
                receiver.Sources.Remove(sender);

                // 隐藏连接线
                link.IsVisible = false;
            });

            undoActions.Add(() =>
            {
                // 确保 LinksMap 中存在发送端的字典
                if (!component.LinksMap.ContainsKey(sender))
                {
                    component.LinksMap[sender] = [];
                }

                // 恢复连接映射
                component.LinksMap[sender][receiver] = link;

                // 恢复 Links 集合中的连接
                component.Links.Add(link);

                // 恢复发送端的目标集合
                if (!sender.Targets.Contains(receiver))
                {
                    sender.Targets.Add(receiver);
                }

                // 恢复接收端的源集合
                if (!receiver.Sources.Contains(sender))
                {
                    receiver.Sources.Add(sender);
                }

                // 显示连接线
                link.IsVisible = true;
            });
        }

        // 添加状态更新操作到 redo/undo 中
        redoActions.Add(() =>
        {
            foreach (var slot in affectedSlots)
            {
                slot?.GetHelper().UpdateState();
            }
        });

        undoActions.Add(() =>
        {
            foreach (var slot in affectedSlots)
            {
                slot?.GetHelper().UpdateState();
            }
        });

        // 创建完整的操作对
        var actionPair = new WorkflowActionPair(
            () => { foreach (var action in redoActions) action(); },
            () => { foreach (var action in undoActions) action(); }
        );

        // 提交到撤销/重做栈
        component.StandardSubmit(actionPair);
    }

    private static bool ShouldCleanupConnections(SlotChannel channel, bool isSender, int existingConnections)
    {
        if (channel.HasFlag(SlotChannel.None))
            return false;

        // 发送端逻辑
        if (isSender)
        {
            if (channel.HasFlag(SlotChannel.OneTarget) && existingConnections > 0)
                return true;

            if (channel.HasFlag(SlotChannel.OneBoth) && existingConnections > 0)
                return true;

            return false;
        }
        // 接收端逻辑
        else
        {
            if (channel.HasFlag(SlotChannel.OneSource) && existingConnections > 0)
                return true;

            if (channel.HasFlag(SlotChannel.OneBoth) && existingConnections > 0)
                return true;

            return false;
        }
    }
    #endregion

    #region Cache Management
    private class TreeCache
    {
        public IWorkflowSlotViewModel? CurrentSender { get; set; }
        public ConcurrentStack<IWorkflowActionPair> RedoStack { get; } = new();
        public ConcurrentStack<IWorkflowActionPair> UndoStack { get; } = new();
        public object StackLock { get; } = new object();
    }

    private static TreeCache GetCache(IWorkflowTreeViewModel component)
    {
        return _cache.GetValue(component, _ => new TreeCache());
    }
    #endregion
}