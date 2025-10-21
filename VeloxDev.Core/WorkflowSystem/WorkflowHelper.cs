using System.Collections.Concurrent;
using VeloxDev.Core.Interfaces.MVVM;
using VeloxDev.Core.Interfaces.WorkflowSystem;
using VeloxDev.Core.WorkflowSystem.Templates;

#pragma warning disable

namespace VeloxDev.Core.WorkflowSystem
{
    public static class WorkflowHelper
    {
        public static class ViewModel
        {
            private static void SynchronizationError() => throw new InvalidOperationException("Synchronization Error : You are attempting to operate the workflow component across domains, but an unexpected incident occurred somewhere, causing the workflow trees to which the two components belong to be inconsistent");

            /// <summary>
            /// [ Component Helper ] Provide standard supports for Link Component
            /// </summary>
            public class Link : IWorkflowLinkViewModelHelper
            {
                private IWorkflowLinkViewModel? component;

                public virtual void Initialize(IWorkflowLinkViewModel link) => component = link;
                public virtual Task CloseAsync() => Task.CompletedTask;
                public virtual void Dispose() { }

                public virtual void Delete()
                {
                    if (component is null) return;
                    var sender = component.Sender;
                    var receiver = component.Receiver;
                    if (sender.Parent?.Parent is null ||
                         receiver.Parent?.Parent is null ||
                         sender.Parent.Parent != receiver.Parent.Parent)
                    {
                        SynchronizationError();
                    }
                    var tree = sender.Parent.Parent;
                    if (tree.LinksMap.TryGetValue(sender, out var linkPai))
                    {
                        if (linkPai.TryGetValue(receiver, out var link))
                        {
                            if (link == component)
                            {   // 唯一正确的分支,可以推送重做和撤销 
                                tree.GetHelper().Submit(new WorkflowActionPair(
                                    () =>
                                    {
                                        linkPai.Remove(receiver);
                                        tree.Links.Remove(link);
                                        sender.Targets.Remove(receiver);
                                        receiver.Sources.Remove(sender);
                                    },
                                    () =>
                                    {
                                        linkPai.Add(receiver, link);
                                        tree.Links.Add(link);
                                        sender.Targets.Add(receiver);
                                        receiver.Sources.Add(sender);
                                    }));
                            }
                            else
                            {
                                linkPai.Remove(receiver);
                                tree.Links.Remove(link);
                                sender.Targets.Remove(receiver);
                                receiver.Sources.Remove(sender);
                            }
                        }
                        else
                        {
                            sender.Targets.Remove(receiver);
                            receiver.Sources.Remove(sender);
                        }
                    }
                    else
                    {
                        sender.Targets.Remove(receiver);
                        receiver.Sources.Remove(sender);
                    }
                    sender.GetHelper().UpdateState();
                    receiver.GetHelper().UpdateState();
                }
            }

            /// <summary>
            /// [ Component Helper ] Provide standard supports for Slot Component
            /// </summary>
            public class Slot : IWorkflowSlotViewModelHelper
            {
                private IWorkflowSlotViewModel? component;

                #region Simple Components
                public virtual void Initialize(IWorkflowSlotViewModel slot) => component = slot;
                public virtual Task CloseAsync() => Task.CompletedTask;
                public virtual void Dispose() { }
                public virtual void ApplyConnection()
                {
                    if (component is null) return;
                    var tree = component.Parent?.Parent;
                    tree?.GetHelper()?.ApplyConnection(component);
                }
                public virtual void ReceiveConnection()
                {
                    if (component is null) return;
                    var tree = component.Parent?.Parent;
                    tree?.GetHelper().ReceiveConnection(component);
                }
                public virtual void SetSize(Size size)
                {
                    if (component is null) return;
                    component.Size.Width = size.Width;
                    component.Size.Height = size.Height;
                    component.OnPropertyChanged(nameof(component.Size));
                    UpdateAnchor();
                }
                public virtual void SetOffset(Offset offset)
                {
                    if (component is null) return;
                    component.Offset.Left = offset.Left;
                    component.Offset.Top = offset.Top;
                    component.OnPropertyChanged(nameof(component.Offset));
                    UpdateAnchor();
                }
                public virtual void SetChannel(SlotChannel channel)
                {
                    if (component is null) return;
                    var tree = component.Parent.Parent;
                    if (tree is null) return;
                    List<IWorkflowLinkViewModel> links_asSource = [];
                    List<IWorkflowLinkViewModel> links_asTarget = [];
                    foreach (var target in component.Targets)
                    {
                        if (target?.Parent.Parent != component.Parent?.Parent) SynchronizationError();
                        if (tree.LinksMap.TryGetValue(component, out var pair) &&
                           pair.TryGetValue(target, out var link))
                            links_asSource.Add(link);
                    }
                    foreach (var source in component.Sources)
                    {
                        if (source?.Parent.Parent != component.Parent?.Parent) SynchronizationError();
                        if (tree.LinksMap.TryGetValue(source, out var pair) &&
                           pair.TryGetValue(component, out var link))
                            links_asTarget.Add(link);
                    }
                    switch (component.Channel.HasFlag(SlotChannel.None),
                            component.Channel.HasFlag(SlotChannel.OneTarget),
                            component.Channel.HasFlag(SlotChannel.OneSource),
                            component.Channel.HasFlag(SlotChannel.MultipleTargets),
                            component.Channel.HasFlag(SlotChannel.MultipleSources))
                    {
                        case (true, false, false, false, false):
                            foreach (var link in links_asSource)
                            {
                                link.GetHelper().Delete();
                            }
                            foreach (var link in links_asTarget)
                            {
                                link.GetHelper().Delete();
                            }
                            break;
                        case (_, true, false, false, false):
                            foreach (var link in links_asTarget)
                            {
                                link.GetHelper().Delete();
                            }
                            break;
                        case (_, false, true, false, false):
                            foreach (var link in links_asSource)
                            {
                                link.GetHelper().Delete();
                            }
                            break;
                        case (_, true, _, false, true):
                            foreach (var link in links_asTarget)
                            {
                                link.GetHelper().Delete();
                            }
                            break;
                        case (_, _, true, true, false):
                            foreach (var link in links_asSource)
                            {
                                link.GetHelper().Delete();
                            }
                            break;
                    }
                }
                public virtual void SetLayer(int layer)
                {
                    if (component is null) return;
                    component.Anchor.Layer = layer;
                    component.OnPropertyChanged(nameof(component.Anchor));
                }
                public virtual void SaveOffset()
                {
                    if (component is null || component.Parent?.Parent is null) return;
                    var oldOffset = component.Offset;
                    var newOffset = new Offset(component.Offset.Left, component.Offset.Top);
                    component.Parent.Parent.GetHelper().Submit(new WorkflowActionPair(
                        () => { component.Offset = newOffset; },
                        () => { component.Offset = oldOffset; }
                    ));
                }
                public virtual void SaveSize()
                {
                    if (component is null || component.Parent?.Parent is null) return;
                    var oldSize = component.Size;
                    var newSize = new Size(component.Size.Width, component.Size.Height);
                    component.Parent.Parent.GetHelper().Submit(new WorkflowActionPair(
                        () => { component.Size = newSize; },
                        () => { component.Size = oldSize; }
                    ));
                }
                public virtual void SaveLayer()
                {
                    if (component is null || component.Parent?.Parent is null) return;
                    var layer = component.Anchor.Layer;
                    component.Parent.Parent.GetHelper().Submit(new WorkflowActionPair(
                        () => { component.Anchor.Layer = layer; },
                        () => { component.Anchor.Layer = layer; }
                    ));
                }
                public virtual void UpdateAnchor()
                {
                    if (component.Parent is null) return;
                    component.Anchor.Left = component.Parent.Anchor.Left + component.Offset.Left + component.Size.Width / 2;
                    component.Anchor.Top = component.Parent.Anchor.Top + component.Offset.Top + component.Size.Height / 2;
                    component.OnPropertyChanged(nameof(component.Anchor));
                }
                public virtual void UpdateState()
                {
                    if (component is null) return;
                    var tree = component.Parent.Parent;
                    if (tree is null) return;
                    List<IWorkflowLinkViewModel> links_asSource = [];
                    List<IWorkflowLinkViewModel> links_asTarget = [];
                    foreach (var target in component.Targets)
                    {
                        if (target?.Parent.Parent != component.Parent?.Parent) SynchronizationError();
                        if (tree.LinksMap.TryGetValue(component, out var pair) &&
                           pair.TryGetValue(target, out var link))
                            links_asSource.Add(link);
                    }
                    foreach (var source in component.Sources)
                    {
                        if (source?.Parent.Parent != component.Parent?.Parent) SynchronizationError();
                        if (tree.LinksMap.TryGetValue(source, out var pair) &&
                           pair.TryGetValue(component, out var link))
                            links_asTarget.Add(link);
                    }
                    component.State = (links_asSource.Count > 0, links_asTarget.Count > 0) switch
                    {
                        (true, false) => SlotState.Sender,
                        (false, true) => SlotState.Receiver,
                        (true, true) => SlotState.Sender | SlotState.Receiver,
                        (false, false) => SlotState.StandBy,
                    };
                }
                #endregion

                public virtual void Delete()
                {
                    if (component is null || component.Parent is null) return;
                    var tree = component.Parent.Parent;
                    if (tree is null) return;
                    List<IWorkflowLinkViewModel> links_asSource = [];
                    List<IWorkflowLinkViewModel> links_asTarget = [];
                    foreach (var target in component.Targets)
                    {
                        if (target?.Parent.Parent != component.Parent?.Parent) SynchronizationError();
                        if (tree.LinksMap.TryGetValue(component, out var pair) &&
                           pair.TryGetValue(target, out var link))
                            links_asSource.Add(link);
                    }
                    foreach (var source in component.Sources)
                    {
                        if (source?.Parent.Parent != component.Parent?.Parent) SynchronizationError();
                        if (tree.LinksMap.TryGetValue(source, out var pair) &&
                           pair.TryGetValue(component, out var link))
                            links_asTarget.Add(link);
                    }
                    foreach (var link in links_asSource)
                    {
                        link.GetHelper().Delete();
                    }
                    foreach (var link in links_asTarget)
                    {
                        link.GetHelper().Delete();
                    }
                    var oldParent = component.Parent;
                    tree.GetHelper().Submit(new WorkflowActionPair(
                    () =>
                    {
                        oldParent.Slots.Remove(component);
                        component.Parent = null;
                    },
                    () =>
                    {
                        oldParent.Slots.Add(component);
                        component.Parent = oldParent;
                    }));
                }
            }

            /// <summary>
            /// [ Component Helper ] Provide standard supports for Node Component
            /// </summary>
            public class Node : IWorkflowNodeViewModelHelper
            {
                private IWorkflowNodeViewModel? component;

                #region Simple Components
                public virtual void Initialize(IWorkflowNodeViewModel node) => component = node;
                public virtual async Task CloseAsync()
                {
                    if (component == null) return;

                    // 锁定所有命令
                    var commands = new[]
                    {
                         component.SaveAnchorCommand, component.SaveSizeCommand, component.SetAnchorCommand,
                         component.SetSizeCommand, component.CreateSlotCommand, component.DeleteCommand,
                         component.WorkCommand, component.BroadcastCommand
                    };

                    foreach (var cmd in commands)
                    {
                        cmd.Lock();
                    }

                    // 中断可能正在执行的工作
                    try
                    {
                        foreach (var cmd in commands)
                        {
                            await cmd.InterruptAsync();
                        }
                    }
                    finally
                    {
                        foreach (var cmd in commands)
                        {
                            cmd.UnLock();
                        }
                    }
                }
                public virtual async Task BroadcastAsync(object? parameter, CancellationToken ct)
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
                            if (receiver.Parent?.Parent != component.Parent) SynchronizationError();

                            // 记录 receiver，以便后续执行 WorkCommand
                            validationTasks.Add(
                                ValidateBroadcastAsync(sender, receiver, parameter, ct)
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
                public virtual Task WorkAsync(object? parameter, CancellationToken ct) => Task.CompletedTask;
                public virtual Task<bool> ValidateBroadcastAsync(IWorkflowSlotViewModel sender, IWorkflowSlotViewModel receiver, object? parameter, CancellationToken ct) => Task.FromResult(true);
                public virtual void Dispose() { }
                public virtual void Move(Offset offset)
                {
                    if (component is null) return;
                    component.Anchor.Left += offset.Left;
                    component.Anchor.Top += offset.Top;
                    foreach (var slot in component.Slots)
                    {
                        slot.GetHelper().UpdateAnchor();
                    }
                }
                public virtual void SetAnchor(Anchor anchor)
                {
                    if (component is null) return;
                    component.Anchor.Left = anchor.Left;
                    component.Anchor.Top = anchor.Top;
                    foreach (var slot in component.Slots)
                    {
                        slot.GetHelper().UpdateAnchor();
                    }
                }
                public virtual void SetSize(Size size)
                {
                    if (component is null) return;
                    component.Size.Width = size.Width;
                    component.Size.Height = size.Height;
                }
                public virtual void SaveAnchor()
                {
                    if (component is null || component.Parent is null) return;
                    var oldAnchor = component.Anchor;
                    var newAnchor = new Anchor(component.Anchor.Left, component.Anchor.Top, component.Anchor.Layer);
                    component.Parent.GetHelper().Submit(new WorkflowActionPair(
                        () =>
                        {
                            component.Anchor = newAnchor;
                            SetAnchor(newAnchor);
                        },
                        () =>
                        {
                            component.Anchor = oldAnchor;
                            SetAnchor(oldAnchor);
                        }));
                }
                public virtual void SaveSize()
                {
                    if (component is null || component.Parent is null) return;
                    var oldSize = component.Size;
                    var newSize = new Size(component.Size.Width, component.Size.Height);
                    component.Parent.GetHelper().Submit(new WorkflowActionPair(
                        () =>
                        {
                            component.Size = newSize;
                        },
                        () =>
                        {
                            component.Size = oldSize;
                        }));
                }
                #endregion

                #region Complex Components
                public virtual void CreateSlot(IWorkflowSlotViewModel slot)
                {
                    if (component is null) return;
                    var oldParent = slot.Parent;
                    var newParent = component;
                    if (component.Parent is null)
                    {
                        slot.GetHelper().Delete();
                        slot.Parent = newParent;
                        slot.GetHelper().UpdateAnchor();
                        return;
                    }
                    component.Parent.GetHelper().Submit(new WorkflowActionPair(
                        () =>
                        {
                            slot.Parent = newParent;
                            slot.GetHelper().UpdateAnchor();
                            component.Slots.Add(slot);
                        },
                        () =>
                        {
                            slot.GetHelper().Delete();
                            slot.Parent = oldParent;
                            slot.GetHelper().UpdateAnchor();
                            component.Slots.Remove(slot);
                        }));
                }
                public virtual void Delete()
                {
                    if (component is null) return;
                    List<IWorkflowSlotViewModel> slots = [.. component.Slots];
                    foreach (var slot in slots)
                    {
                        if (component.Parent != slot.Parent?.Parent) SynchronizationError();
                        slot.GetHelper().Delete();
                    }
                    var oldParent = component.Parent;
                    component.Parent?.GetHelper()?.Submit(new WorkflowActionPair(
                        () =>
                        {
                            oldParent?.Nodes?.Remove(component);
                            component.Parent = null;
                        },
                        () =>
                        {
                            oldParent?.Nodes?.Add(component);
                            component.Parent = oldParent;
                        }));
                }
                #endregion
            }

            /// <summary>
            /// [ Component Helper ] Provide standard supports for Tree Component
            /// </summary>
            public class Tree : IWorkflowTreeViewModelHelper
            {
                private IWorkflowTreeViewModel? component = null;

                #region Simple Components
                public virtual void Initialize(IWorkflowTreeViewModel tree) => component = tree;
                public virtual async Task CloseAsync()
                {
                    if (component is null) return;

                    // 收集所有需要操作的命令
                    var commandsToLock = new List<IVeloxCommand>();

                    if (component.Links != null)
                    {
                        foreach (var linkGroup in component.Links)
                        {
                            commandsToLock.Add(linkGroup.DeleteCommand);
                        }
                    }

                    if (component.Nodes != null)
                    {
                        foreach (var node in component.Nodes)
                        {
                            var nodeCommands = new[]
                            {
                                 node.SaveAnchorCommand, node.SaveSizeCommand, node.SetAnchorCommand,
                                 node.SetSizeCommand, node.CreateSlotCommand, node.DeleteCommand,
                                 node.WorkCommand, node.BroadcastCommand
                            };
                            commandsToLock.AddRange(nodeCommands);

                            if (node.Slots != null)
                            {
                                foreach (var slot in node.Slots)
                                {
                                    var slotCommands = new[]
                                    {
                                         slot.SaveOffsetCommand, slot.SaveSizeCommand, slot.SetOffsetCommand,
                                         slot.SetSizeCommand, slot.ApplyConnectionCommand, slot.ReceiveConnectionCommand,
                                         slot.DeleteCommand
                                    };
                                    commandsToLock.AddRange(slotCommands);
                                }
                            }
                        }
                    }

                    // 锁定所有命令
                    foreach (var cmd in commandsToLock)
                    {
                        cmd.Lock();
                    }

                    try
                    {
                        // 中断所有可能正在执行的操作
                        foreach (var cmd in commandsToLock)
                        {
                            await cmd.InterruptAsync();
                        }
                    }
                    finally
                    {
                        // 解锁所有命令
                        foreach (var cmd in commandsToLock)
                        {
                            cmd.UnLock();
                        }
                    }
                }
                public virtual void Dispose() { }
                public virtual IWorkflowLinkViewModel CreateLink(IWorkflowSlotViewModel sender, IWorkflowSlotViewModel receiver)
                    =>
                    new LinkViewModelBase()
                    {
                        Sender = new SlotViewModelBase() { Anchor = sender.Anchor },
                        Receiver = new SlotViewModelBase() { Anchor = receiver.Anchor },
                    };
                public virtual void CreateNode(IWorkflowNodeViewModel node)
                {
                    if (component is null) return;
                    var oldParent = node.Parent;
                    var newParent = component;
                    node.GetHelper().Delete();
                    Submit(new WorkflowActionPair(
                        () =>
                        {
                            node.Parent = newParent;
                            component.Nodes.Add(node);
                        },
                        () =>
                        {
                            node.GetHelper().Delete();
                            node.Parent = oldParent;
                            component.Nodes.Remove(node);
                        }));
                }
                public virtual void SetPointer(Anchor anchor)
                {
                    component.VirtualLink.Receiver.Anchor = anchor;
                    component.VirtualLink.OnPropertyChanged(nameof(component.VirtualLink.Receiver));
                    component.OnPropertyChanged(nameof(component.VirtualLink));
                }
                #endregion

                #region Connection Manager
                private IWorkflowSlotViewModel? _sender = null;
                private IWorkflowSlotViewModel? _receiver = null;
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
                        _receiver = null;
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

                    // 1. 检查接收端能力
                    bool canBeReceiver = slot.Channel.HasFlag(SlotChannel.OneSource) ||
                                        slot.Channel.HasFlag(SlotChannel.MultipleSources) ||
                                        slot.Channel.HasFlag(SlotChannel.OneBoth) ||
                                        slot.Channel.HasFlag(SlotChannel.MultipleBoth);

                    if (!canBeReceiver)
                    {
                        ResetVirtualLink();
                        _receiver = null;
                        return;
                    }

                    // 2. 检查同节点内连接
                    if (_sender.Parent == slot.Parent)
                    {
                        ResetVirtualLink();
                        _sender = null;
                        _receiver = null;
                        return;
                    }

                    // 3. 根据接收端通道类型智能清理连接
                    SmartCleanupReceiverConnections(slot);

                    // 4. 创建新连接
                    CreateNewConnection(_sender, slot);

                    // 5. 重置状态
                    ResetVirtualLink();
                    _sender = null;
                    _receiver = null;
                }

                /// <summary>
                /// 智能清理发送端连接（根据通道类型决定）
                /// </summary>
                private void SmartCleanupSenderConnections(IWorkflowSlotViewModel sender)
                {
                    if (component == null) return;

                    // 收集所有从这个发送端出发的连接
                    var connectionsToRemove = new List<(IWorkflowSlotViewModel, IWorkflowSlotViewModel, IWorkflowLinkViewModel)>();

                    if (component.LinksMap.TryGetValue(sender, out var targetLinks))
                    {
                        foreach (var kvp in targetLinks)
                        {
                            connectionsToRemove.Add((sender, kvp.Key, kvp.Value));
                        }
                    }

                    // 根据发送端通道类型决定是否清理
                    bool shouldCleanup = ShouldCleanupConnections(sender.Channel, isSender: true, existingConnections: connectionsToRemove.Count);

                    // 执行清理
                    if (shouldCleanup && connectionsToRemove.Count > 0)
                    {
                        RemoveConnections(connectionsToRemove);
                    }
                }
                /// <summary>
                /// 智能清理接收端连接（根据通道类型决定）
                /// </summary>
                private void SmartCleanupReceiverConnections(IWorkflowSlotViewModel receiver)
                {
                    if (component == null) return;

                    // 收集所有指向这个接收端的连接
                    var connectionsToRemove = new List<(IWorkflowSlotViewModel, IWorkflowSlotViewModel, IWorkflowLinkViewModel)>();

                    foreach (var senderDict in component.LinksMap)
                    {
                        if (senderDict.Value.TryGetValue(receiver, out var link))
                        {
                            connectionsToRemove.Add((senderDict.Key, receiver, link));
                        }
                    }

                    // 根据接收端通道类型决定是否清理
                    bool shouldCleanup = ShouldCleanupConnections(receiver.Channel, isSender: false, existingConnections: connectionsToRemove.Count);

                    // 执行清理
                    if (shouldCleanup && connectionsToRemove.Count > 0)
                    {
                        RemoveConnections(connectionsToRemove);
                    }
                }
                /// <summary>
                /// 根据通道类型判断是否需要清理现有连接
                /// </summary>
                private bool ShouldCleanupConnections(SlotChannel channel, bool isSender, int existingConnections)
                {
                    if (channel.HasFlag(SlotChannel.None))
                        return false; // 无通道，不允许任何连接

                    // 发送端逻辑
                    if (isSender)
                    {
                        if (channel.HasFlag(SlotChannel.OneTarget) && existingConnections > 0)
                            return true; // OneTarget 且已有连接，需要清理

                        if (channel.HasFlag(SlotChannel.OneBoth) && existingConnections > 0)
                            return true; // OneBoth 且已有连接，需要清理

                        // MultipleTargets 和 MultipleBoth 允许保留现有连接
                        return false;
                    }
                    // 接收端逻辑
                    else
                    {
                        if (channel.HasFlag(SlotChannel.OneSource) && existingConnections > 0)
                            return true; // OneSource 且已有连接，需要清理

                        if (channel.HasFlag(SlotChannel.OneBoth) && existingConnections > 0)
                            return true; // OneBoth 且已有连接，需要清理

                        // MultipleSources 和 MultipleBoth 允许保留现有连接
                        return false;
                    }
                }
                /// <summary>
                /// 移除指定的连接集合（所有操作都通过 Submit 入栈）
                /// </summary>
                private void RemoveConnections(List<(IWorkflowSlotViewModel Sender, IWorkflowSlotViewModel Receiver, IWorkflowLinkViewModel Link)> connectionsToRemove)
                {
                    if (connectionsToRemove == null || connectionsToRemove.Count == 0)
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
                            // 1. 从 Links 集合中移除连接
                            component.Links.Remove(link);

                            // 2. 从 LinksMap 中移除连接映射
                            if (component.LinksMap.TryGetValue(sender, out var receiverLinks))
                            {
                                receiverLinks.Remove(receiver);
                                if (receiverLinks.Count == 0)
                                {
                                    component.LinksMap.Remove(sender);
                                }
                            }

                            // 3. 更新发送端的目标集合
                            sender.Targets.Remove(receiver);

                            // 4. 更新接收端的源集合
                            receiver.Sources.Remove(sender);

                            // 5. 隐藏连接线
                            link.IsVisible = false;
                        });

                        undoActions.Add(() =>
                        {
                            // 1. 确保 LinksMap 中存在发送端的字典
                            if (!component.LinksMap.ContainsKey(sender))
                            {
                                component.LinksMap[sender] = new Dictionary<IWorkflowSlotViewModel, IWorkflowLinkViewModel>();
                            }

                            // 2. 恢复连接映射
                            component.LinksMap[sender][receiver] = link;

                            // 3. 恢复 Links 集合中的连接
                            component.Links.Add(link);

                            // 4. 恢复发送端的目标集合
                            if (!sender.Targets.Contains(receiver))
                            {
                                sender.Targets.Add(receiver);
                            }

                            // 5. 恢复接收端的源集合
                            if (!receiver.Sources.Contains(sender))
                            {
                                receiver.Sources.Add(sender);
                            }

                            // 6. 显示连接线
                            link.IsVisible = true;
                        });
                    }

                    // 添加状态更新操作到 redo/undo 中
                    redoActions.Add(() =>
                    {
                        // 统一更新所有受影响插槽的状态
                        foreach (var slot in affectedSlots)
                        {
                            if (slot != null)
                            {
                                slot.GetHelper().UpdateState();
                            }
                        }
                    });

                    undoActions.Add(() =>
                    {
                        // 统一更新所有受影响插槽的状态
                        foreach (var slot in affectedSlots)
                        {
                            if (slot != null)
                            {
                                slot.GetHelper().UpdateState();
                            }
                        }
                    });

                    // 创建完整的操作对
                    var actionPair = new WorkflowActionPair(
                        () =>
                        {
                            // 执行所有 redo 操作
                            foreach (var action in redoActions)
                            {
                                action();
                            }
                        },
                        () =>
                        {
                            // 执行所有 undo 操作
                            foreach (var action in undoActions)
                            {
                                action();
                            }
                        }
                    );

                    // 提交到撤销/重做栈（这是唯一的数据操作入口）
                    Submit(actionPair);
                }
                /// <summary>
                /// 在两个Slot间建立新的连接（包含数据创建与集合变更）
                /// </summary>
                private void CreateNewConnection(IWorkflowSlotViewModel sender, IWorkflowSlotViewModel receiver)
                {
                    if (sender == null || receiver == null) return;

                    // 检查是否已存在连接
                    bool connectionExists = component.LinksMap.TryGetValue(sender, out var existingLinks) &&
                                           existingLinks.ContainsKey(receiver);

                    if (connectionExists) return;

                    // 创建新连接（但不立即添加到数据结构中）
                    var newLink = CreateLink(sender, receiver);
                    newLink.IsVisible = true;

                    // 所有数据修改都必须在 Submit 的 Action 中执行
                    Submit(new WorkflowActionPair(
                        () =>
                        {
                            // 重做：执行连接创建
                            if (!component.LinksMap.ContainsKey(sender))
                                component.LinksMap[sender] = new Dictionary<IWorkflowSlotViewModel, IWorkflowLinkViewModel>();

                            component.LinksMap[sender][receiver] = newLink;
                            component.Links.Add(newLink);

                            if (!sender.Targets.Contains(receiver))
                                sender.Targets.Add(receiver);
                            if (!receiver.Sources.Contains(sender))
                                receiver.Sources.Add(sender);

                            // 状态更新
                            sender.GetHelper().UpdateState();
                            receiver.GetHelper().UpdateState();

                            newLink.IsVisible = true;
                        },
                        () =>
                        {
                            // 撤销：回滚连接创建
                            component.Links.Remove(newLink);
                            if (component.LinksMap.ContainsKey(sender))
                            {
                                component.LinksMap[sender].Remove(receiver);
                                if (component.LinksMap[sender].Count == 0)
                                    component.LinksMap.Remove(sender);
                            }

                            sender.Targets.Remove(receiver);
                            receiver.Sources.Remove(sender);

                            // 状态更新
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

                    // 重置发送端状态
                    if (_sender != null)
                    {
                        _sender.State &= ~SlotState.PreviewSender;
                        _sender.GetHelper().UpdateState();
                    }

                    _sender = null;
                    _receiver = null;
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
                                // 记录错误但继续执行
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
                                // 记录错误但继续执行
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