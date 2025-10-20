﻿using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Drawing;
using System.Reflection;
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

            #region Link Helper [ 官方固件 ]
            public class Link : IWorkflowLinkViewModelHelper
            {
                private IWorkflowLinkViewModel? _self;

                public virtual void Initialize(IWorkflowLinkViewModel link) => _self = link;
                public virtual Task CloseAsync() => Task.CompletedTask;
                public virtual void Dispose() { }

                public virtual void Delete()
                {
                    if (_self is null) return;
                    var sender = _self.Sender;
                    var receiver = _self.Receiver;
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
                            if (link == _self)
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
            #endregion

            #region Slot Helper [ 官方固件 ]
            public class Slot : IWorkflowSlotViewModelHelper
            {
                private IWorkflowSlotViewModel? _self;

                #region Simple Components
                public virtual void Initialize(IWorkflowSlotViewModel slot) => _self = slot;
                public virtual Task CloseAsync() => Task.CompletedTask;
                public virtual void Dispose() { }
                public virtual void ApplyConnection()
                {
                    if (_self is null) return;
                    var tree = _self.Parent?.Parent;
                    tree?.GetHelper()?.ApplyConnection(_self);
                }
                public virtual void ReceiveConnection()
                {
                    if (_self is null) return;
                    var tree = _self.Parent?.Parent;
                    tree?.GetHelper().ReceiveConnection(_self);
                }
                public virtual void SetSize(Size size)
                {
                    if (_self is null) return;
                    _self.Size.Width = size.Width;
                    _self.Size.Height = size.Height;
                    UpdateAnchor();
                }
                public virtual void SetOffset(Offset offset)
                {
                    if (_self is null) return;
                    _self.Offset.Left = offset.Left;
                    _self.Offset.Top = offset.Top;
                    UpdateAnchor();
                }
                public virtual void SaveOffset()
                {
                    if (_self is null || _self.Parent?.Parent is null) return;
                    var oldOffset = _self.Offset;
                    var newOffset = new Offset(_self.Offset.Left, _self.Offset.Top);
                    _self.Parent.Parent.GetHelper().Submit(new WorkflowActionPair(
                        () => { _self.Offset = newOffset; },
                        () => { _self.Offset = oldOffset; }
                    ));
                }
                public virtual void SaveSize()
                {
                    if (_self is null || _self.Parent?.Parent is null) return;
                    var oldSize = _self.Size;
                    var newSize = new Size(_self.Size.Width, _self.Size.Height);
                    _self.Parent.Parent.GetHelper().Submit(new WorkflowActionPair(
                        () => { _self.Size = newSize; },
                        () => { _self.Size = oldSize; }
                    ));
                }
                public virtual void UpdateAnchor()
                {
                    if (_self.Parent is null) return;
                    _self.Anchor.Left = _self.Parent.Anchor.Left + _self.Offset.Left + _self.Size.Width / 2;
                    _self.Anchor.Top = _self.Parent.Anchor.Top + _self.Offset.Top + _self.Size.Height / 2;
                }
                public virtual void UpdateState()
                {
                    if (_self is null) return;
                    var tree = _self.Parent.Parent;
                    if (tree is null) return;
                    List<IWorkflowLinkViewModel> links_asSource = [];
                    List<IWorkflowLinkViewModel> links_asTarget = [];
                    foreach (var target in _self.Targets)
                    {
                        if (target?.Parent.Parent != _self.Parent?.Parent) SynchronizationError();
                        if (tree.LinksMap.TryGetValue(_self, out var pair) &&
                           pair.TryGetValue(target, out var link))
                            links_asSource.Add(link);
                    }
                    foreach (var source in _self.Sources)
                    {
                        if (source?.Parent.Parent != _self.Parent?.Parent) SynchronizationError();
                        if (tree.LinksMap.TryGetValue(source, out var pair) &&
                           pair.TryGetValue(_self, out var link))
                            links_asTarget.Add(link);
                    }
                    _self.State = (links_asSource.Count > 0, links_asTarget.Count > 0) switch
                    {
                        (true, false) => SlotState.Sender,
                        (false, true) => SlotState.Receiver,
                        (true, true) => SlotState.Sender | SlotState.Receiver,
                        (false, false) => SlotState.StandBy,
                    };
                }
                public virtual void SetChannel(SlotChannel channel)
                {
                    if (_self is null) return;
                    var tree = _self.Parent.Parent;
                    if (tree is null) return;
                    List<IWorkflowLinkViewModel> links_asSource = [];
                    List<IWorkflowLinkViewModel> links_asTarget = [];
                    foreach (var target in _self.Targets)
                    {
                        if (target?.Parent.Parent != _self.Parent?.Parent) SynchronizationError();
                        if (tree.LinksMap.TryGetValue(_self, out var pair) &&
                           pair.TryGetValue(target, out var link))
                            links_asSource.Add(link);
                    }
                    foreach (var source in _self.Sources)
                    {
                        if (source?.Parent.Parent != _self.Parent?.Parent) SynchronizationError();
                        if (tree.LinksMap.TryGetValue(source, out var pair) &&
                           pair.TryGetValue(_self, out var link))
                            links_asTarget.Add(link);
                    }
                    switch (_self.Channel.HasFlag(SlotChannel.None),
                            _self.Channel.HasFlag(SlotChannel.OneTarget),
                            _self.Channel.HasFlag(SlotChannel.OneSource),
                            _self.Channel.HasFlag(SlotChannel.MultipleTargets),
                            _self.Channel.HasFlag(SlotChannel.MultipleSources))
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
                #endregion

                public virtual void Delete()
                {
                    if (_self is null || _self.Parent is null) return;
                    var tree = _self.Parent.Parent;
                    if (tree is null) return;
                    List<IWorkflowLinkViewModel> links_asSource = [];
                    List<IWorkflowLinkViewModel> links_asTarget = [];
                    foreach (var target in _self.Targets)
                    {
                        if (target?.Parent.Parent != _self.Parent?.Parent) SynchronizationError();
                        if (tree.LinksMap.TryGetValue(_self, out var pair) &&
                           pair.TryGetValue(target, out var link))
                            links_asSource.Add(link);
                    }
                    foreach (var source in _self.Sources)
                    {
                        if (source?.Parent.Parent != _self.Parent?.Parent) SynchronizationError();
                        if (tree.LinksMap.TryGetValue(source, out var pair) &&
                           pair.TryGetValue(_self, out var link))
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
                    var oldParent = _self.Parent;
                    tree.GetHelper().Submit(new WorkflowActionPair(
                    () =>
                    {
                        oldParent.Slots.Remove(_self);
                        _self.Parent = null;
                    },
                    () =>
                    {
                        oldParent.Slots.Add(_self);
                        _self.Parent = oldParent;
                    }));
                }
            }
            #endregion

            #region Node Helper [ 官方固件 ]
            public class Node : IWorkflowNodeViewModelHelper
            {
                private IWorkflowNodeViewModel? _self;

                #region Simple Components
                public virtual void Initialize(IWorkflowNodeViewModel node) => _self = node;
                public virtual async Task CloseAsync()
                {
                    if (_self == null) return;

                    // 锁定所有命令
                    var commands = new[]
                    {
                         _self.SaveAnchorCommand, _self.SaveSizeCommand, _self.SetAnchorCommand,
                         _self.SetSizeCommand, _self.CreateSlotCommand, _self.DeleteCommand,
                         _self.WorkCommand, _self.BroadcastCommand
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
                public virtual Task BroadcastAsync(object? parameter) => Task.CompletedTask;
                public virtual Task WorkAsync(object? parameter) => Task.CompletedTask;
                public virtual void Dispose() { }
                public virtual void Move(Offset offset)
                {
                    if (_self is null) return;
                    _self.Anchor.Left += offset.Left;
                    _self.Anchor.Top += offset.Top;
                    foreach (var slot in _self.Slots)
                    {
                        slot.GetHelper().UpdateAnchor();
                    }
                }
                public virtual void SetAnchor(Anchor anchor)
                {
                    if (_self is null) return;
                    _self.Anchor.Left = anchor.Left;
                    _self.Anchor.Top = anchor.Top;
                    foreach (var slot in _self.Slots)
                    {
                        slot.GetHelper().UpdateAnchor();
                    }
                }
                public virtual void SetSize(Size size)
                {
                    if (_self is null) return;
                    _self.Size.Width = size.Width;
                    _self.Size.Height = size.Height;
                }
                public virtual void SaveAnchor()
                {
                    if (_self is null || _self.Parent is null) return;
                    var oldAnchor = _self.Anchor;
                    var newAnchor = new Anchor(_self.Anchor.Left, _self.Anchor.Top, _self.Anchor.Layer);
                    _self.Parent.GetHelper().Submit(new WorkflowActionPair(
                        () =>
                        {
                            _self.Anchor = newAnchor;
                            SetAnchor(newAnchor);
                        },
                        () =>
                        {
                            _self.Anchor = oldAnchor;
                            SetAnchor(oldAnchor);
                        }));
                }
                public virtual void SaveSize()
                {
                    if (_self is null || _self.Parent is null) return;
                    var oldSize = _self.Size;
                    var newSize = new Size(_self.Size.Width, _self.Size.Height);
                    _self.Parent.GetHelper().Submit(new WorkflowActionPair(
                        () =>
                        {
                            _self.Size = newSize;
                        },
                        () =>
                        {
                            _self.Size = oldSize;
                        }));
                }
                #endregion

                #region Complex Components
                public virtual void CreateSlot(IWorkflowSlotViewModel slot)
                {
                    if (_self is null) return;
                    var oldParent = slot.Parent;
                    var newParent = _self;
                    if (_self.Parent is null)
                    {
                        slot.GetHelper().Delete();
                        slot.Parent = newParent;
                        slot.GetHelper().UpdateAnchor();
                        return;
                    }
                    _self.Parent.GetHelper().Submit(new WorkflowActionPair(
                        () =>
                        {
                            slot.Parent = newParent;
                            slot.GetHelper().UpdateAnchor();
                            _self.Slots.Add(slot);
                        },
                        () =>
                        {
                            slot.GetHelper().Delete();
                            slot.Parent = oldParent;
                            slot.GetHelper().UpdateAnchor();
                            _self.Slots.Remove(slot);
                        }));
                }

                public virtual void Delete()
                {
                    if (_self is null) return;
                    List<IWorkflowSlotViewModel> slots = [.. _self.Slots];
                    foreach (var slot in slots)
                    {
                        if (_self.Parent != slot.Parent?.Parent) SynchronizationError();
                        slot.GetHelper().Delete();
                    }
                    var oldParent = _self.Parent;
                    _self.Parent?.GetHelper()?.Submit(new WorkflowActionPair(
                        () =>
                        {
                            oldParent?.Nodes?.Remove(_self);
                            _self.Parent = null;
                        },
                        () =>
                        {
                            oldParent?.Nodes?.Add(_self);
                            _self.Parent = oldParent;
                        }));
                }
                #endregion
            }
            #endregion

            #region Tree Helper [ 官方固件 ]
            public class Tree : IWorkflowTreeViewModelHelper
            {
                private IWorkflowTreeViewModel? _self = null;

                #region Simple Components
                public virtual void Initialize(IWorkflowTreeViewModel tree) => _self = tree;
                public virtual async Task CloseAsync()
                {
                    if (_self is null) return;

                    // 收集所有需要操作的命令
                    var commandsToLock = new List<IVeloxCommand>();

                    if (_self.Links != null)
                    {
                        foreach (var linkGroup in _self.Links)
                        {
                            commandsToLock.Add(linkGroup.DeleteCommand);
                        }
                    }

                    if (_self.Nodes != null)
                    {
                        foreach (var node in _self.Nodes)
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
                    if (_self is null) return;
                    var oldParent = node.Parent;
                    var newParent = _self;
                    node.GetHelper().Delete();
                    Submit(new WorkflowActionPair(
                        () =>
                        {
                            node.Parent = newParent;
                            _self.Nodes.Add(node);
                        },
                        () =>
                        {
                            node.GetHelper().Delete();
                            node.Parent = oldParent;
                            _self.Nodes.Remove(node);
                        }));
                }
                public virtual void SetPointer(Anchor anchor)
                {
                    _self.VirtualLink.Receiver.Anchor = anchor;
                }
                #endregion

                #region Connection Manager
                private IWorkflowSlotViewModel? _sender = null;
                private IWorkflowSlotViewModel? _receiver = null;
                public virtual void ApplyConnection(IWorkflowSlotViewModel slot)
                {
                    if (_self == null) return;

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
                    _self.VirtualLink.Sender.Anchor = slot.Anchor;
                    _self.VirtualLink.Receiver.Anchor = slot.Anchor;
                    _self.VirtualLink.IsVisible = true;

                    // 4. 更新状态
                    _sender = slot;
                    slot.State = SlotState.PreviewSender;
                    slot.GetHelper().UpdateState();
                }
                public virtual void ReceiveConnection(IWorkflowSlotViewModel slot)
                {
                    if (_self == null || _sender == null) return;

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
                    if (_self == null) return;

                    // 收集所有从这个发送端出发的连接
                    var connectionsToRemove = new List<(IWorkflowSlotViewModel, IWorkflowSlotViewModel, IWorkflowLinkViewModel)>();

                    if (_self.LinksMap.TryGetValue(sender, out var targetLinks))
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
                    if (_self == null) return;

                    // 收集所有指向这个接收端的连接
                    var connectionsToRemove = new List<(IWorkflowSlotViewModel, IWorkflowSlotViewModel, IWorkflowLinkViewModel)>();

                    foreach (var senderDict in _self.LinksMap)
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
                /// 移除指定的连接集合
                /// </summary>
                private void RemoveConnections(List<(IWorkflowSlotViewModel Sender, IWorkflowSlotViewModel Receiver, IWorkflowLinkViewModel Link)> connectionsToRemove)
                {
                    var redoActions = new List<Action>();
                    var undoActions = new List<Action>();

                    foreach (var (sender, receiver, link) in connectionsToRemove)
                    {
                        redoActions.Add(() =>
                        {
                            // 从数据结构中移除
                            _self.Links.Remove(link);
                            if (_self.LinksMap.ContainsKey(sender))
                            {
                                _self.LinksMap[sender].Remove(receiver);
                                if (_self.LinksMap[sender].Count == 0)
                                    _self.LinksMap.Remove(sender);
                            }

                            // 更新连接关系
                            sender.Targets.Remove(receiver);
                            receiver.Sources.Remove(sender);

                            // 更新状态
                            if (sender.Targets.Count == 0)
                                sender.State &= ~SlotState.Sender;
                            if (receiver.Sources.Count == 0)
                                receiver.State &= ~SlotState.Receiver;

                            link.IsVisible = false;
                        });

                        undoActions.Add(() =>
                        {
                            // 恢复连接
                            if (!_self.LinksMap.ContainsKey(sender))
                                _self.LinksMap[sender] = new Dictionary<IWorkflowSlotViewModel, IWorkflowLinkViewModel>();

                            _self.LinksMap[sender][receiver] = link;
                            _self.Links.Add(link);

                            // 恢复连接关系
                            sender.Targets.Add(receiver);
                            receiver.Sources.Add(sender);

                            // 恢复状态
                            sender.State |= SlotState.Sender;
                            receiver.State |= SlotState.Receiver;

                            link.IsVisible = true;
                        });
                    }

                    // 提交批量操作
                    Submit(new WorkflowActionPair(
                        () => { foreach (var action in redoActions) action(); },
                        () => { foreach (var action in undoActions) action(); }
                    ));

                    // 立即更新状态显示
                    foreach (var (sender, receiver, _) in connectionsToRemove)
                    {
                        sender.GetHelper().UpdateState();
                        receiver.GetHelper().UpdateState();
                    }
                }
                private void CreateNewConnection(IWorkflowSlotViewModel sender, IWorkflowSlotViewModel receiver)
                {
                    if (sender == null || receiver == null) return;

                    // 检查是否已存在连接
                    bool connectionExists = _self.LinksMap.TryGetValue(sender, out var existingLinks) &&
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
                            if (!_self.LinksMap.ContainsKey(sender))
                                _self.LinksMap[sender] = new Dictionary<IWorkflowSlotViewModel, IWorkflowLinkViewModel>();

                            _self.LinksMap[sender][receiver] = newLink;
                            _self.Links.Add(newLink);

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
                            _self.Links.Remove(newLink);
                            if (_self.LinksMap.ContainsKey(sender))
                            {
                                _self.LinksMap[sender].Remove(receiver);
                                if (_self.LinksMap[sender].Count == 0)
                                    _self.LinksMap.Remove(sender);
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
                    if (_self is null) return;
                    _self.VirtualLink.Sender.Anchor = new();
                    _self.VirtualLink.Receiver.Anchor = new();
                    _self.VirtualLink.IsVisible = false;

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
            #endregion
        }
    }
}