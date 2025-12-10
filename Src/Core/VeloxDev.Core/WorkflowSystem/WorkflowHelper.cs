using System.Collections.Concurrent;
using System.Diagnostics;
using VeloxDev.Core.Interfaces.MVVM;
using VeloxDev.Core.Interfaces.WorkflowSystem;
using VeloxDev.Core.WorkflowSystem.StandardEx;
using VeloxDev.Core.WorkflowSystem.Templates;

namespace VeloxDev.Core.WorkflowSystem
{
    public static class WorkflowHelper
    {
        public static class ViewModel
        {
            /// <summary>
            /// [ Component Helper ] Provide standard supports for Link Component
            /// </summary>
            public class Link : IWorkflowLinkViewModelHelper
            {
                private IWorkflowLinkViewModel? component;
                private IReadOnlyCollection<IVeloxCommand> commands = [];

                public virtual void Install(IWorkflowLinkViewModel link)
                {
                    component = link;
                    commands = link.GetStandardCommands();
                }
                public virtual void Uninstall(IWorkflowLinkViewModel link) { }
                public virtual void Closing() => commands.StandardClosing();
                public virtual async Task CloseAsync() => await commands.StandardCloseAsync();
                public virtual void Closed() => commands.StandardClosed();
                public virtual void Dispose() { GC.SuppressFinalize(this); }

                public virtual void Delete() => component?.StandardDelete();
            }

            /// <summary>
            /// [ Component Helper ] Provide standard supports for Slot Component
            /// </summary>
            public class Slot : IWorkflowSlotViewModelHelper
            {
                private IWorkflowSlotViewModel? component;
                private IReadOnlyCollection<IVeloxCommand> commands = [];

                public virtual void Install(IWorkflowSlotViewModel slot)
                {
                    component = slot;
                    commands = slot.GetStandardCommands();
                }
                public void Uninstall(IWorkflowSlotViewModel slot)
                {

                }
                public virtual void Closing() => commands.StandardClosing();
                public virtual async Task CloseAsync() => await commands.StandardCloseAsync();
                public virtual void Closed() => commands.StandardClosed();
                public virtual void Dispose() { GC.SuppressFinalize(this); }

                public virtual void SetSize(Size size) => component?.StandardSetSize(size);
                public virtual void SetOffset(Offset offset) => component?.StandardSetOffset(offset);
                public virtual void SetChannel(SlotChannel channel) => component?.StandardSetChannel(channel);
                public virtual void SetLayer(int layer) => component?.StandardSetLayer(layer);

                public virtual void UpdateAnchor() => component?.StandardUpdateAnchor();
                public virtual void UpdateState() => component?.StandardUpdateState();

                public virtual void ApplyConnection() => component?.StandardApplyConnection();
                public virtual void ReceiveConnection() => component?.StandardReceiveConnection();

                public virtual void Delete() => component?.StandardDelete();
            }

            /// <summary>
            /// [ Component Helper ] Provide standard supports for Node Component
            /// </summary>
            public class Node : IWorkflowNodeViewModelHelper
            {
                private IWorkflowNodeViewModel? component;
                private IReadOnlyCollection<IVeloxCommand> commands = [];

                public virtual void Install(IWorkflowNodeViewModel node)
                {
                    component = node;
                    commands = node.GetStandardCommands();
                }
                public void Uninstall(IWorkflowNodeViewModel node)
                {

                }
                public virtual void Closing() => commands.StandardClosing();
                public virtual async Task CloseAsync() => await commands.StandardCloseAsync();
                public virtual void Closed() => commands.StandardClosed();
                public virtual void Dispose() { GC.SuppressFinalize(this); }
                public virtual void CreateSlot(IWorkflowSlotViewModel slot) => component?.StandardCreateSlot(slot);

                public virtual async Task BroadcastAsync(
                    object? parameter,
                    CancellationToken ct)
                {
                    if (component is not null) await component.StandardBroadcastAsync(parameter, ct);
                }
                public virtual Task WorkAsync(
                    object? parameter,
                    CancellationToken ct)
                    => Task.CompletedTask;
                public virtual Task<bool> ValidateBroadcastAsync(
                    IWorkflowSlotViewModel sender,
                    IWorkflowSlotViewModel receiver,
                    object? parameter,
                    CancellationToken ct)
                    => Task.FromResult(true);

                public virtual void SetAnchor(Anchor anchor) => component?.StandardSetAnchor(anchor);
                public virtual void SetLayer(int layer) => component?.StandardSetLayer(layer);
                public virtual void SetSize(Size size) => component?.StandardSetSize(size);
                public virtual void Move(Offset offset) => component?.StandardMove(offset);

                public virtual void Delete() => component?.StandardDelete();
            }

            /// <summary>
            /// [ Component Helper ] Provide standard supports for Tree Component
            /// </summary>
            public class Tree : IWorkflowTreeViewModelHelper
            {
                private IWorkflowTreeViewModel? component = null;
                private IReadOnlyCollection<IVeloxCommand> commands = [];

                public virtual void Install(IWorkflowTreeViewModel tree)
                {
                    component = tree;
                    commands = tree.GetStandardCommands();
                }
                public void Uninstall(IWorkflowTreeViewModel tree)
                {
                    
                }
                public virtual void Closing() => commands.StandardClosing();
                public virtual async Task CloseAsync() => await commands.StandardCloseAsync();
                public virtual void Closed() => commands.StandardClosed();
                public virtual void Dispose() { GC.SuppressFinalize(this); }
                public virtual IWorkflowLinkViewModel CreateLink(
                    IWorkflowSlotViewModel sender,
                    IWorkflowSlotViewModel receiver)
                    =>
                    new LinkViewModelBase()
                    {
                        Sender = new SlotViewModelBase() { Anchor = sender.Anchor },
                        Receiver = new SlotViewModelBase() { Anchor = receiver.Anchor },
                    };
                public virtual void CreateNode(IWorkflowNodeViewModel node) => component?.StandardCreateNode(node);
                public virtual void SetPointer(Anchor anchor) => component?.StandardSetPointer(anchor);

                #region Connection Manager
                private IWorkflowSlotViewModel? _sender = null;

                public virtual bool ValidateConnection(
                    IWorkflowSlotViewModel sender,
                    IWorkflowSlotViewModel receiver)
                    => true;

                public virtual void ApplyConnection(IWorkflowSlotViewModel slot)
                {
                    if (component == null) return;

                    // 1. 检查发送端能力
                    bool canBeSender = slot.Channel.HasFlag(SlotChannel.OneTarget) ||
                                       slot.Channel.HasFlag(SlotChannel.MultipleTargets) ||
                                       slot.Channel.HasFlag(SlotChannel.OneBoth) ||
                                       slot.Channel.HasFlag(SlotChannel.MultipleBoth);

                    if (!canBeSender)
                    {
                        ResetVirtualLink();
                        _sender = null;
                        return;
                    }

                    // 2. 根据发送端通道类型智能清理连接
                    SmartCleanupSenderConnections(slot);

                    // 3. 设置虚拟连接
                    component.VirtualLink.Sender.Anchor = slot.Anchor;
                    component.VirtualLink.Receiver.Anchor = slot.Anchor;
                    component.VirtualLink.IsVisible = true;

                    // 4. 更新状态
                    _sender = slot;
                    slot.State = SlotState.PreviewSender;
                    slot.GetHelper().UpdateState();
                }
                public virtual void ReceiveConnection(IWorkflowSlotViewModel slot)
                {
                    if (component == null || _sender == null) return;

                    // 检查接收端能力
                    bool canBeReceiver = slot.Channel.HasFlag(SlotChannel.OneSource) ||
                                        slot.Channel.HasFlag(SlotChannel.MultipleSources) ||
                                        slot.Channel.HasFlag(SlotChannel.OneBoth) ||
                                        slot.Channel.HasFlag(SlotChannel.MultipleBoth);

                    if (!canBeReceiver)
                    {
                        ResetVirtualLink();
                        return;
                    }

                    // 检查用户自定义验证逻辑
                    if (!ValidateConnection(_sender, slot))
                    {
                        ResetVirtualLink();
                        _sender = null;
                        return;
                    }

                    // 检查同节点内连接
                    if (_sender.Parent == slot.Parent)
                    {
                        ResetVirtualLink();
                        _sender = null;
                        return;
                    }

                    // 启用硬性规定：检查并清理同向连接冲突
                    CleanupSameDirectionConnections(_sender, slot);

                    // 根据接收端通道类型智能清理连接
                    SmartCleanupReceiverConnections(slot);

                    // 创建新连接
                    CreateNewConnection(_sender, slot);

                    // 重置状态
                    ResetVirtualLink();
                    _sender = null;
                }

                // 清理两个Node之间的同向连接冲突
                private void CleanupSameDirectionConnections(
                    IWorkflowSlotViewModel newSender,
                    IWorkflowSlotViewModel newReceiver)
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
                        RemoveConnections(sameDirectionConnections);
                    }
                }

                // 智能清理发送端连接（对于OneBoth需要清理所有连接）
                private void SmartCleanupSenderConnections(IWorkflowSlotViewModel sender)
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
                        RemoveConnections(connectionsToRemove);
                    }
                }

                // 智能清理接收端连接（对于OneBoth需要清理所有连接）
                private void SmartCleanupReceiverConnections(IWorkflowSlotViewModel receiver)
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
                        RemoveConnections(connectionsToRemove);
                    }
                }

                // 根据通道类型判断是否需要清理现有连接
                private static bool ShouldCleanupConnections(
                    SlotChannel channel,
                    bool isSender,
                    int existingConnections)
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

                // 移除指定的连接集合
                private void RemoveConnections(
                    List<(IWorkflowSlotViewModel Sender,
                        IWorkflowSlotViewModel Receiver,
                        IWorkflowLinkViewModel Link)> connectionsToRemove)
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
                    Submit(actionPair);
                }

                // 在两个Slot间建立新的连接
                private void CreateNewConnection(
                    IWorkflowSlotViewModel sender,
                    IWorkflowSlotViewModel receiver)
                {
                    if (component is null || sender is null || receiver is null) return;

                    // 检查是否已存在连接（经过清理后应该不存在）
                    bool connectionExists = component.LinksMap.TryGetValue(sender, out var existingLinks) &&
                                           existingLinks.ContainsKey(receiver);

                    if (connectionExists) return;

                    // 创建新连接
                    var newLink = CreateLink(sender, receiver);
                    newLink.IsVisible = true;

                    Submit(new WorkflowActionPair(
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

                public virtual void ResetVirtualLink()
                {
                    if (component is null) return;
                    component.VirtualLink.Sender.Anchor = new();
                    component.VirtualLink.Receiver.Anchor = new();
                    component.VirtualLink.IsVisible = false;

                    if (_sender != null)
                    {
                        _sender.State &= ~SlotState.PreviewSender;
                        _sender.GetHelper().UpdateState();
                    }

                    _sender = null;
                }
                #endregion

                #region Redo & Undo
                private readonly ConcurrentStack<IWorkflowActionPair> _redoStack = new();
                private readonly ConcurrentStack<IWorkflowActionPair> _undoStack = new();
                private readonly object _stackLock = new();
                public virtual void Redo()
                {
                    lock (_stackLock)
                    {
                        if (_redoStack.TryPop(out var pair))
                        {
                            try
                            {
                                pair.Redo.Invoke();
                                _undoStack.Push(pair);
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine(ex);
                            }
                        }
                    }
                }
                public virtual void Submit(IWorkflowActionPair actionPair)
                {
                    lock (_stackLock)
                    {
                        try
                        {
                            actionPair.Redo.Invoke();
                            _undoStack.Push(actionPair);
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine(ex);
                        }
                    }
                }
                public virtual void Undo()
                {
                    lock (_stackLock)
                    {
                        if (_undoStack.TryPop(out var pair))
                        {
                            try
                            {
                                pair.Undo.Invoke();
                                _redoStack.Push(pair);
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine(ex);
                            }
                        }
                    }
                }
                public virtual void ClearHistory()
                {
                    lock (_stackLock)
                    {
                        _redoStack.Clear();
                        _undoStack.Clear();
                    }
                }
                #endregion
            }
        }
    }
}