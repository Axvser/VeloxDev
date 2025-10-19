using System.Collections.Concurrent;
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

                    // 2. 强制清理发送端的所有现有连接（根据通道限制）
                    ForceCleanupAllSenderConnections(slot);

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

                    // 3. 强制清理接收端的所有现有连接（无论通道类型）
                    ForceCleanupAllReceiverConnections(slot);

                    // 4. 创建新连接
                    CreateNewConnection(_sender, slot);

                    // 5. 重置状态
                    ResetVirtualLink();
                    _sender = null;
                    _receiver = null;
                }

                /// <summary>
                /// 强制清理发送端的所有现有连接（根据通道限制）
                /// </summary>
                private void ForceCleanupAllSenderConnections(IWorkflowSlotViewModel sender)
                {
                    if (_self == null) return;

                    // 收集所有从这个发送端出发的连接
                    var connectionsToRemove = new List<(IWorkflowSlotViewModel, IWorkflowSlotViewModel, IWorkflowLinkViewModel)>();

                    // 查找所有以当前sender为发送端的连接
                    if (_self.LinksMap.TryGetValue(sender, out var targetLinks))
                    {
                        foreach (var kvp in targetLinks)
                        {
                            connectionsToRemove.Add((sender, kvp.Key, kvp.Value));
                        }
                    }

                    // 根据发送端通道限制决定是否清理
                    bool shouldCleanup = false;

                    switch (sender.Channel)
                    {
                        case SlotChannel.OneTarget:
                        case SlotChannel.OneBoth:
                            // OneTarget和OneBoth在建立新连接前必须清理旧连接
                            shouldCleanup = connectionsToRemove.Count > 0;
                            break;

                        case SlotChannel.MultipleTargets:
                        case SlotChannel.MultipleBoth:
                            // Multiple类型允许保留现有连接
                            shouldCleanup = false;
                            break;

                        default:
                            shouldCleanup = false;
                            break;
                    }

                    // 执行清理
                    if (shouldCleanup && connectionsToRemove.Count > 0)
                    {
                        var redoActions = new List<Action>();
                        var undoActions = new List<Action>();

                        foreach (var (sndr, receiver, link) in connectionsToRemove)
                        {
                            redoActions.Add(() =>
                            {
                                // 从数据结构中移除
                                _self.Links.Remove(link);
                                if (_self.LinksMap.ContainsKey(sndr))
                                {
                                    _self.LinksMap[sndr].Remove(receiver);
                                    if (_self.LinksMap[sndr].Count == 0)
                                        _self.LinksMap.Remove(sndr);
                                }

                                // 更新连接关系
                                sndr.Targets.Remove(receiver);
                                receiver.Sources.Remove(sndr);

                                // 更新状态
                                if (sndr.Targets.Count == 0)
                                    sndr.State &= ~SlotState.Sender;
                                if (receiver.Sources.Count == 0)
                                    receiver.State &= ~SlotState.Receiver;

                                link.IsVisible = false;
                            });

                            undoActions.Add(() =>
                            {
                                // 恢复连接
                                if (!_self.LinksMap.ContainsKey(sndr))
                                    _self.LinksMap[sndr] = new Dictionary<IWorkflowSlotViewModel, IWorkflowLinkViewModel>();

                                _self.LinksMap[sndr][receiver] = link;
                                _self.Links.Add(link);

                                // 恢复连接关系
                                sndr.Targets.Add(receiver);
                                receiver.Sources.Add(sndr);

                                // 恢复状态
                                sndr.State |= SlotState.Sender;
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
                        foreach (var (sndr, receiver, _) in connectionsToRemove)
                        {
                            sndr.GetHelper().UpdateState();
                            receiver.GetHelper().UpdateState();
                        }
                    }
                }
                /// <summary>
                /// 强制清理接收端的所有现有连接
                /// </summary>
                private void ForceCleanupAllReceiverConnections(IWorkflowSlotViewModel receiver)
                {
                    if (_self == null) return;

                    // 收集所有指向这个接收端的连接
                    var connectionsToRemove = new List<(IWorkflowSlotViewModel, IWorkflowSlotViewModel, IWorkflowLinkViewModel)>();

                    // 查找所有以当前receiver为接收端的连接
                    foreach (var senderDict in _self.LinksMap)
                    {
                        if (senderDict.Value.TryGetValue(receiver, out var link))
                        {
                            connectionsToRemove.Add((senderDict.Key, receiver, link));
                        }
                    }

                    // 执行清理
                    if (connectionsToRemove.Count > 0)
                    {
                        var redoActions = new List<Action>();
                        var undoActions = new List<Action>();

                        foreach (var (sender, recv, link) in connectionsToRemove)
                        {
                            redoActions.Add(() =>
                            {
                                // 从数据结构中移除
                                _self.Links.Remove(link);
                                if (_self.LinksMap.ContainsKey(sender))
                                {
                                    _self.LinksMap[sender].Remove(recv);
                                    if (_self.LinksMap[sender].Count == 0)
                                        _self.LinksMap.Remove(sender);
                                }

                                // 更新连接关系
                                sender.Targets.Remove(recv);
                                recv.Sources.Remove(sender);

                                // 更新状态
                                if (sender.Targets.Count == 0)
                                    sender.State &= ~SlotState.Sender;
                                if (recv.Sources.Count == 0)
                                    recv.State &= ~SlotState.Receiver;

                                link.IsVisible = false;
                            });

                            undoActions.Add(() =>
                            {
                                // 恢复连接
                                if (!_self.LinksMap.ContainsKey(sender))
                                    _self.LinksMap[sender] = new Dictionary<IWorkflowSlotViewModel, IWorkflowLinkViewModel>();

                                _self.LinksMap[sender][recv] = link;
                                _self.Links.Add(link);

                                // 恢复连接关系
                                sender.Targets.Add(recv);
                                recv.Sources.Add(sender);

                                // 恢复状态
                                sender.State |= SlotState.Sender;
                                recv.State |= SlotState.Receiver;

                                link.IsVisible = true;
                            });
                        }

                        // 提交批量操作
                        Submit(new WorkflowActionPair(
                            () => { foreach (var action in redoActions) action(); },
                            () => { foreach (var action in undoActions) action(); }
                        ));

                        // 立即更新状态显示
                        foreach (var (sender, recv, _) in connectionsToRemove)
                        {
                            sender.GetHelper().UpdateState();
                            recv.GetHelper().UpdateState();
                        }
                    }
                }
                /// <summary>
                /// 强制清理接收端上的现有连接（针对接收端通道限制）
                /// </summary>
                private void ForceCleanupExistingConnectionsForReceiver(IWorkflowSlotViewModel receiver)
                {
                    if (_self == null) return;

                    // 收集需要清理的连接（所有指向这个接收端的连接）
                    var connectionsToRemove = new List<(IWorkflowSlotViewModel, IWorkflowSlotViewModel, IWorkflowLinkViewModel)>();

                    // 查找所有以当前receiver为接收端的连接
                    foreach (var senderDict in _self.LinksMap)
                    {
                        if (senderDict.Value.TryGetValue(receiver, out var link))
                        {
                            connectionsToRemove.Add((senderDict.Key, receiver, link));
                        }
                    }

                    // 根据接收端通道限制决定是否清理
                    bool shouldCleanup = false;

                    switch (receiver.Channel)
                    {
                        case SlotChannel.OneSource:
                        case SlotChannel.OneBoth:
                            // OneSource和OneBoth在建立新连接前必须清理旧连接
                            shouldCleanup = connectionsToRemove.Count > 0;
                            break;

                        case SlotChannel.MultipleSources:
                        case SlotChannel.MultipleBoth:
                            // Multiple类型允许保留现有连接
                            shouldCleanup = false;
                            break;

                        default:
                            shouldCleanup = false;
                            break;
                    }

                    // 执行清理
                    if (shouldCleanup && connectionsToRemove.Count > 0)
                    {
                        var redoActions = new List<Action>();
                        var undoActions = new List<Action>();

                        foreach (var (sender, recv, link) in connectionsToRemove)
                        {
                            redoActions.Add(() =>
                            {
                                // 从数据结构中移除
                                _self.Links.Remove(link);
                                if (_self.LinksMap.ContainsKey(sender))
                                {
                                    _self.LinksMap[sender].Remove(recv);
                                    if (_self.LinksMap[sender].Count == 0)
                                        _self.LinksMap.Remove(sender);
                                }

                                // 更新连接关系
                                sender.Targets.Remove(recv);
                                recv.Sources.Remove(sender);

                                // 更新状态
                                if (sender.Targets.Count == 0)
                                    sender.State &= ~SlotState.Sender;
                                if (recv.Sources.Count == 0)
                                    recv.State &= ~SlotState.Receiver;

                                link.IsVisible = false;
                            });

                            undoActions.Add(() =>
                            {
                                // 恢复连接
                                if (!_self.LinksMap.ContainsKey(sender))
                                    _self.LinksMap[sender] = new Dictionary<IWorkflowSlotViewModel, IWorkflowLinkViewModel>();

                                _self.LinksMap[sender][recv] = link;
                                _self.Links.Add(link);

                                // 恢复连接关系
                                sender.Targets.Add(recv);
                                recv.Sources.Add(sender);

                                // 恢复状态
                                sender.State |= SlotState.Sender;
                                recv.State |= SlotState.Receiver;

                                link.IsVisible = true;
                            });
                        }

                        // 提交批量操作
                        Submit(new WorkflowActionPair(
                            () => { foreach (var action in redoActions) action(); },
                            () => { foreach (var action in undoActions) action(); }
                        ));

                        // 立即更新状态显示
                        foreach (var (sender, recv, _) in connectionsToRemove)
                        {
                            sender.GetHelper().UpdateState();
                            recv.GetHelper().UpdateState();
                        }
                    }
                }
                /// <summary>
                /// 强制清理现有连接（立即执行，不通过撤销/重做栈）
                /// </summary>
                private void ForceCleanupExistingConnections(IWorkflowSlotViewModel slot)
                {
                    // 收集需要清理的连接
                    var connectionsToRemove = new List<(IWorkflowSlotViewModel, IWorkflowSlotViewModel, IWorkflowLinkViewModel)>();

                    // 查找所有以当前slot为发送端的连接
                    if (_self.LinksMap.TryGetValue(slot, out var targetLinks))
                    {
                        foreach (var kvp in targetLinks)
                        {
                            connectionsToRemove.Add((slot, kvp.Key, kvp.Value));
                        }
                    }

                    // 根据通道限制决定是否清理
                    bool shouldCleanup = false;

                    switch (slot.Channel)
                    {
                        case SlotChannel.OneTarget:
                        case SlotChannel.OneBoth:
                            // OneTarget和OneBoth在建立新连接前必须清理旧连接
                            shouldCleanup = connectionsToRemove.Count > 0;
                            break;

                        case SlotChannel.MultipleTargets:
                        case SlotChannel.MultipleBoth:
                            // Multiple类型允许保留现有连接
                            shouldCleanup = false;
                            break;

                        default:
                            shouldCleanup = false;
                            break;
                    }

                    // 执行清理
                    if (shouldCleanup && connectionsToRemove.Count > 0)
                    {
                        // 批量提交到撤销/重做栈
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
                }
                private bool IsConnectionAllowed(IWorkflowSlotViewModel sender, IWorkflowSlotViewModel receiver)
                {
                    // 检查发送端限制
                    bool senderAllowsNewConnection = true;
                    if (sender.Channel.HasFlag(SlotChannel.OneTarget) && sender.Targets.Count > 0)
                        senderAllowsNewConnection = false;
                    if (sender.Channel.HasFlag(SlotChannel.OneBoth) && (sender.Targets.Count > 0 || sender.Sources.Count > 0))
                        senderAllowsNewConnection = false;
                    if (sender.Channel.HasFlag(SlotChannel.None))
                        senderAllowsNewConnection = false;

                    // 检查接收端限制  
                    bool receiverAllowsNewConnection = true;
                    if (receiver.Channel.HasFlag(SlotChannel.OneSource) && receiver.Sources.Count > 0)
                        receiverAllowsNewConnection = false;
                    if (receiver.Channel.HasFlag(SlotChannel.OneBoth) && (receiver.Sources.Count > 0 || receiver.Targets.Count > 0))
                        receiverAllowsNewConnection = false;
                    if (receiver.Channel.HasFlag(SlotChannel.None))
                        receiverAllowsNewConnection = false;

                    return senderAllowsNewConnection && receiverAllowsNewConnection;
                }
                private void CreateNewConnection(IWorkflowSlotViewModel sender, IWorkflowSlotViewModel receiver)
                {
                    if (sender == null || receiver == null) return;

                    // 检查是否已存在连接
                    bool connectionExists = _self.LinksMap.TryGetValue(sender, out var existingLinks) &&
                                           existingLinks.ContainsKey(receiver);

                    if (connectionExists) return;

                    // 创建新连接
                    var newLink = CreateLink(sender, receiver);
                    newLink.IsVisible = true;

                    // 更新数据结构
                    if (!_self.LinksMap.ContainsKey(sender))
                        _self.LinksMap[sender] = new Dictionary<IWorkflowSlotViewModel, IWorkflowLinkViewModel>();

                    _self.LinksMap[sender][receiver] = newLink;
                    _self.Links.Add(newLink);

                    // 更新连接关系
                    if (!sender.Targets.Contains(receiver))
                        sender.Targets.Add(receiver);
                    if (!receiver.Sources.Contains(sender))
                        receiver.Sources.Add(sender);

                    // 更新状态
                    sender.State |= SlotState.Sender;
                    sender.State &= ~SlotState.PreviewSender;
                    receiver.State |= SlotState.Receiver;

                    // 提交到撤销/重做栈
                    Submit(new WorkflowActionPair(
                        () =>
                        {
                            _self.Links.Add(newLink);
                            _self.LinksMap[sender][receiver] = newLink;
                            sender.Targets.Add(receiver);
                            receiver.Sources.Add(sender);
                            sender.State |= SlotState.Sender;
                            receiver.State |= SlotState.Receiver;
                            newLink.IsVisible = true;

                            // 更新状态显示
                            sender.GetHelper().UpdateState();
                            receiver.GetHelper().UpdateState();
                        },
                        () =>
                        {
                            _self.Links.Remove(newLink);
                            _self.LinksMap[sender].Remove(receiver);
                            if (_self.LinksMap[sender].Count == 0) _self.LinksMap.Remove(sender);
                            sender.Targets.Remove(receiver);
                            receiver.Sources.Remove(sender);

                            // 更新状态
                            if (sender.Targets.Count == 0) sender.State &= ~SlotState.Sender;
                            if (receiver.Sources.Count == 0) receiver.State &= ~SlotState.Receiver;
                            newLink.IsVisible = false;

                            // 更新状态显示
                            sender.GetHelper().UpdateState();
                            receiver.GetHelper().UpdateState();
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