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
                private IWorkflowSlotViewModel? _pendingSender = null;

                public virtual void ApplyConnection(IWorkflowSlotViewModel slot)
                {
                    if (_self == null) return;

                }

                public virtual void ReceiveConnection(IWorkflowSlotViewModel slot)
                {
                    if (_self == null || _pendingSender == null) return;

                }

                public virtual void ResetVirtualLink()
                {
                    if (_self is null) return;

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
/*
                    switch (_self.Channel.HasFlag(SlotChannel.None),
                            _self.Channel.HasFlag(SlotChannel.OneTarget),
                            _self.Channel.HasFlag(SlotChannel.OneSource),
                            _self.Channel.HasFlag(SlotChannel.MultipleTargets),
                            _self.Channel.HasFlag(SlotChannel.MultipleSources))
                    {
                        case (true, false, false, false, false):
                            foreach(var link in links_asSource)
                            {
                                link.GetHelper().Delete();
                            }
                            foreach(var link in links_asTarget)
                            {
                                link.GetHelper().Delete(); 
                            }
                            break;
                        case (_, true, false, false, false):
                            foreach (var link in links_asSource)
                            {
                                link.GetHelper().Delete();
                            }
                            break;
                        case (_, false, true, false, false):

                            break;
                        case (_, true, true, false, false):

                            break;
                        case (_, _, _, true, false):

                            break;
                        case (_, _, _, false, true):

                            break;
                        case (_, _, _, true, true):

                            break;
                        default:
                            throw new InvalidOperationException("An unknown situation occurred when handling the Channel");
                    }
 */