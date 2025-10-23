﻿using System.Collections.Concurrent;
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
                private List<IVeloxCommand> commands = [];

                public virtual void Initialize(IWorkflowLinkViewModel link)
                {
                    component = link;
                    commands = [link.DeleteCommand];
                }
                public virtual void Closing()
                {
                    if (component is null) return;
                    foreach (var command in commands)
                    {
                        command.Lock();
                    }
                }
                public virtual async Task CloseAsync()
                {
                    if (component is null) return;
                    foreach (var command in commands)
                    {
                        command.InterruptAsync();
                    }
                }
                public virtual void Closed()
                {
                    if (component is null) return;
                    foreach (var command in commands)
                    {
                        command.UnLock();
                    }
                }
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
                private List<IVeloxCommand> commands = [];

                #region Simple Components
                public virtual void Initialize(IWorkflowSlotViewModel slot)
                {
                    component = slot;
                    commands =
                        [
                            slot.SaveOffsetCommand,
                            slot.SaveSizeCommand,
                            slot.SetOffsetCommand,
                            slot.SetSizeCommand,
                            slot.ApplyConnectionCommand,
                            slot.ReceiveConnectionCommand,
                            slot.DeleteCommand
                        ];
                }
                public virtual void Closing()
                {
                    if (component is null) return;
                    foreach (var command in commands)
                    {
                        command.Lock();
                    }
                }
                public virtual async Task CloseAsync()
                {
                    if (component is null) return;
                    foreach (var command in commands)
                    {
                        await command.InterruptAsync();
                    }
                }
                public virtual void Closed()
                {
                    if (component is null) return;
                    foreach (var command in commands)
                    {
                        command.UnLock();
                    }
                }
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
                    var action = () =>
                    {
                        component.Anchor.Layer = layer;
                        component.OnPropertyChanged(nameof(component.Anchor));
                    };
                    component.Parent.Parent.GetHelper().Submit(new WorkflowActionPair(action, action));
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
                private List<IVeloxCommand> commands = [];

                #region Simple Components
                public virtual void Initialize(IWorkflowNodeViewModel node)
                {
                    component = node;
                    commands =
                        [
                            node.SaveAnchorCommand,
                            node.SaveSizeCommand,
                            node.SetAnchorCommand,
                            node.SetSizeCommand,
                            node.CreateSlotCommand,
                            node.DeleteCommand,
                            node.WorkCommand,
                            node.BroadcastCommand
                        ];
                }
                public virtual void Closing()
                {
                    if (component is null) return;
                    foreach (var command in commands)
                    {
                        command.Lock();
                    }
                }
                public virtual async Task CloseAsync()
                {
                    try
                    {
                        foreach (var cmd in commands)
                        {
                            await cmd.InterruptAsync();
                        }
                    }
                    catch { }
                }
                public virtual void Closed()
                {
                    if (component is null) return;
                    foreach (var command in commands)
                    {
                        command.UnLock();
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
                    component.Anchor.Layer = anchor.Layer;
                    component.OnPropertyChanged(nameof(component.Anchor));
                    foreach (var slot in component.Slots)
                    {
                        slot.GetHelper().UpdateAnchor();
                    }
                }
                public virtual void SetLayer(int layer)
                {
                    if (component is null) return;
                    component.Anchor.Layer = layer;
                    component.OnPropertyChanged(nameof(component.Anchor));
                }
                public virtual void SetSize(Size size)
                {
                    if (component is null) return;
                    component.Size.Width = size.Width;
                    component.Size.Height = size.Height;
                    component.OnPropertyChanged(nameof(component.Size));
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
                public virtual void SaveLayer()
                {
                    if (component is null || component.Parent is null) return;
                    var layer = component.Anchor.Layer;
                    var action = () =>
                    {
                        component.Anchor.Layer = layer;
                        component.OnPropertyChanged(nameof(component.Anchor));
                    };
                    component.Parent.GetHelper().Submit(new WorkflowActionPair(action, action));
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
                private List<IVeloxCommand> commands = [];

                #region Simple Components
                public virtual void Initialize(IWorkflowTreeViewModel tree)
                {
                    component = tree;
                    commands =
                        [
                            tree.CreateNodeCommand,
                            tree.SetPointerCommand,
                            tree.ResetVirtualLinkCommand,
                            tree.ApplyConnectionCommand,
                            tree.ReceiveConnectionCommand,
                            tree.SubmitCommand,
                            tree.RedoCommand,
                            tree.UndoCommand
                        ];
                }
                public virtual void Closing()
                {
                    if (component is null) return;
                    foreach (var command in commands)
                    {
                        command.Lock();
                    }
                }
                public virtual async Task CloseAsync()
                {
                    if (component is null) return;

                    // 收集所有 Helper
                    var helpers = new List<IWorkflowHelper>();
                    foreach (var linkGroup in component.Links)
                    {
                        helpers.Add(linkGroup.GetHelper());
                    }
                    foreach (var node in component.Nodes)
                    {
                        helpers.Add(node.GetHelper());
                        foreach (var slot in node.Slots)
                        {
                            helpers.Add(slot.GetHelper());
                        }
                    }

                    Closing();
                    foreach (var helper in helpers)
                    {
                        helper.Closing();
                    }

                    foreach (var helper in helpers)
                    {
                        await helper.CloseAsync();
                    }

                    foreach (var helper in helpers)
                    {
                        helper.Closed();
                    }
                    Closed();
                }
                public virtual void Closed()
                {
                    if (component is null) return;
                    foreach (var command in commands)
                    {
                        command.UnLock();
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
                /// 智能清理发送端连接（对于OneBoth需要清理所有连接）
                /// </summary>
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
                /// <summary>
                /// 智能清理接收端连接（对于OneBoth需要清理所有连接）
                /// </summary>
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
                /// <summary>
                /// 根据通道类型判断是否需要清理现有连接
                /// </summary>
                private bool ShouldCleanupConnections(SlotChannel channel, bool isSender, int existingConnections)
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
                /// <summary>
                /// 移除指定的连接集合
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
                                component.LinksMap[sender] = new Dictionary<IWorkflowSlotViewModel, IWorkflowLinkViewModel>();
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
                            if (slot != null)
                            {
                                slot.GetHelper().UpdateState();
                            }
                        }
                    });

                    undoActions.Add(() =>
                    {
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
                        () => { foreach (var action in redoActions) action(); },
                        () => { foreach (var action in undoActions) action(); }
                    );

                    // 提交到撤销/重做栈
                    Submit(actionPair);
                }
                /// <summary>
                /// 在两个Slot间建立新的连接
                /// </summary>
                private void CreateNewConnection(IWorkflowSlotViewModel sender, IWorkflowSlotViewModel receiver)
                {
                    if (sender == null || receiver == null) return;

                    // 检查是否已存在连接
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
                                component.LinksMap[sender] = new Dictionary<IWorkflowSlotViewModel, IWorkflowLinkViewModel>();

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