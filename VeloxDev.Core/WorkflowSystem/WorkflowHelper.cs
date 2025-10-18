using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Drawing;
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
            #region Link Helper [ 官方固件 ]
            public class Link : IWorkflowLinkViewModelHelper
            {
                private IWorkflowLinkViewModel? _self;

                public virtual void Initialize(IWorkflowLinkViewModel link) => _self = link;
                public virtual Task CloseAsync() => Task.CompletedTask;
                public virtual void Dispose() { }

                public virtual void Delete()
                {
                    if (_self?.Sender == null || _self?.Receiver == null || _self?.Sender?.Parent?.Parent == null)
                        return;

                    var tree = _self.Sender.Parent.Parent;
                    var sender = _self.Sender;
                    var receiver = _self.Receiver;
                    var link = _self;

                    // 保存当前状态用于撤销
                    var oldSenderTargets = new List<IWorkflowSlotViewModel>(sender.Targets);
                    var oldReceiverSources = new List<IWorkflowSlotViewModel>(receiver.Sources);
                    var wasSenderConnected = sender.Targets.Contains(receiver);
                    var wasReceiverConnected = receiver.Sources.Contains(sender);
                    var wasLinkInTree = tree.Links.Contains(link);

                    tree.GetHelper().Submit(new WorkflowActionPair(
                        // Redo: 删除连接
                        () =>
                        {
                            // 从集合中移除连接关系
                            if (sender.Targets.Contains(receiver))
                                sender.Targets.Remove(receiver);
                            if (receiver.Sources.Contains(sender))
                                receiver.Sources.Remove(sender);
                            if (tree.Links.Contains(link))
                                tree.Links.Remove(link);

                            // 更新发送方状态
                            if (sender.Targets.Count == 0)
                            {
                                sender.State &= ~SlotState.Sender;
                                if (sender.Sources.Count == 0)
                                    sender.State = SlotState.StandBy;
                                else
                                    sender.State &= ~SlotState.PreviewSender;
                            }

                            // 更新接收方状态
                            if (receiver.Sources.Count == 0)
                            {
                                receiver.State &= ~SlotState.Processor;
                                if (receiver.Targets.Count == 0)
                                    receiver.State = SlotState.StandBy;
                                else
                                    receiver.State &= ~SlotState.PreviewProcessor;
                            }
                        },
                        // Undo: 恢复连接
                        () =>
                        {
                            // 恢复连接关系
                            if (wasSenderConnected && !sender.Targets.Contains(receiver))
                                sender.Targets.Add(receiver);
                            if (wasReceiverConnected && !receiver.Sources.Contains(sender))
                                receiver.Sources.Add(sender);
                            if (wasLinkInTree && !tree.Links.Contains(link))
                                tree.Links.Add(link);

                            // 恢复发送方状态
                            if (wasSenderConnected)
                            {
                                sender.State |= SlotState.Sender;
                                sender.State &= ~SlotState.PreviewSender;
                            }

                            // 恢复接收方状态
                            if (wasReceiverConnected)
                            {
                                receiver.State |= SlotState.Processor;
                                receiver.State &= ~SlotState.PreviewProcessor;
                            }
                        }
                    ));
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
                public void UpdateAnchor()
                {
                    if (_self.Parent is null) return;
                    _self.Anchor.Left = _self.Parent.Anchor.Left + _self.Offset.Left + _self.Size.Width / 2;
                    _self.Anchor.Top = _self.Parent.Anchor.Top + _self.Offset.Top + _self.Size.Height / 2;
                }
                #endregion

                public virtual void Delete()
                {
                    if (_self?.Parent?.Parent == null) return;

                    var tree = _self.Parent.Parent;
                    var slot = _self;

                    // 保存完整状态用于撤销
                    var oldTargets = new List<IWorkflowSlotViewModel>(slot.Targets);
                    var oldSources = new List<IWorkflowSlotViewModel>(slot.Sources);
                    var oldParent = slot.Parent;
                    var wasInParentCollection = oldParent?.Slots.Contains(slot) == true;

                    // 收集所有相关连接
                    var affectedLinks = new List<(IWorkflowLinkViewModel Link, IWorkflowSlotViewModel Sender, IWorkflowSlotViewModel Receiver)>();
                    foreach (var link in tree.Links)
                    {
                        if (link.Sender == slot || link.Receiver == slot)
                        {
                            affectedLinks.Add((link, link.Sender, link.Receiver));
                        }
                    }

                    tree.GetHelper().Submit(new WorkflowActionPair(
                        // Redo: 删除Slot及其所有连接
                        () =>
                        {
                            // 删除所有出站连接（作为发送方）
                            foreach (var target in new List<IWorkflowSlotViewModel>(slot.Targets))
                            {
                                if (slot.Targets.Contains(target))
                                    slot.Targets.Remove(target);
                                if (target.Sources.Contains(slot))
                                    target.Sources.Remove(slot);

                                // 移除对应的连接线
                                var linkToRemove = tree.Links.FirstOrDefault(l => l.Sender == slot && l.Receiver == target);
                                if (linkToRemove != null)
                                    tree.Links.Remove(linkToRemove);

                                // 更新目标Slot状态
                                if (target.Sources.Count == 0)
                                {
                                    target.State &= ~SlotState.Processor;
                                    if (target.Targets.Count == 0)
                                        target.State = SlotState.StandBy;
                                }
                            }

                            // 删除所有入站连接（作为接收方）
                            foreach (var source in new List<IWorkflowSlotViewModel>(slot.Sources))
                            {
                                if (source.Targets.Contains(slot))
                                    source.Targets.Remove(slot);
                                if (slot.Sources.Contains(source))
                                    slot.Sources.Remove(source);

                                // 移除对应的连接线
                                var linkToRemove = tree.Links.FirstOrDefault(l => l.Sender == source && l.Receiver == slot);
                                if (linkToRemove != null)
                                    tree.Links.Remove(linkToRemove);

                                // 更新源Slot状态
                                if (source.Targets.Count == 0)
                                {
                                    source.State &= ~SlotState.Sender;
                                    if (source.Sources.Count == 0)
                                        source.State = SlotState.StandBy;
                                }
                            }

                            // 从父节点移除
                            if (wasInParentCollection)
                            {
                                oldParent.Slots.Remove(slot);
                                slot.Parent = null;
                            }

                            // 清空集合
                            slot.Targets.Clear();
                            slot.Sources.Clear();
                            slot.State = SlotState.StandBy;
                        },
                        // Undo: 恢复Slot及其所有连接
                        () =>
                        {
                            // 恢复父节点关系
                            slot.Parent = oldParent;
                            if (wasInParentCollection && !oldParent.Slots.Contains(slot))
                                oldParent.Slots.Add(slot);

                            // 恢复所有出站连接
                            foreach (var target in oldTargets)
                            {
                                if (!slot.Targets.Contains(target))
                                    slot.Targets.Add(target);
                                if (!target.Sources.Contains(slot))
                                    target.Sources.Add(slot);

                                // 恢复连接线
                                var existingLink = tree.Links.FirstOrDefault(l => l.Sender == slot && l.Receiver == target);
                                if (existingLink == null)
                                {
                                    var newLink = tree.GetHelper().CreateLink(slot, target);
                                    if (!tree.Links.Contains(newLink))
                                        tree.Links.Add(newLink);
                                }

                                // 恢复目标Slot状态
                                target.State |= SlotState.Processor;
                                target.State &= ~SlotState.PreviewProcessor;
                            }

                            // 恢复所有入站连接
                            foreach (var source in oldSources)
                            {
                                if (!source.Targets.Contains(slot))
                                    source.Targets.Add(slot);
                                if (!slot.Sources.Contains(source))
                                    slot.Sources.Add(source);

                                // 恢复连接线
                                var existingLink = tree.Links.FirstOrDefault(l => l.Sender == source && l.Receiver == slot);
                                if (existingLink == null)
                                {
                                    var newLink = tree.GetHelper().CreateLink(source, slot);
                                    if (!tree.Links.Contains(newLink))
                                        tree.Links.Add(newLink);
                                }

                                // 恢复源Slot状态
                                source.State |= SlotState.Sender;
                                source.State &= ~SlotState.PreviewSender;
                            }

                            // 恢复Slot状态
                            if (oldTargets.Count > 0)
                                slot.State |= SlotState.Sender;
                            if (oldSources.Count > 0)
                                slot.State |= SlotState.Processor;
                            if (oldTargets.Count == 0 && oldSources.Count == 0)
                                slot.State = SlotState.StandBy;
                        }
                    ));
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
                    slot.GetHelper().Delete();
                    slot.Parent = newParent;
                    slot.GetHelper().UpdateAnchor();
                    if (_self.Parent is null) return;
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
                    if (_self?.Parent == null) return;

                    var tree = _self.Parent;
                    var node = _self;

                    // 保存完整状态用于撤销
                    var oldSlots = new List<IWorkflowSlotViewModel>(node.Slots);
                    var oldParent = node.Parent;
                    var wasInTreeCollection = tree.Nodes.Contains(node);
                    var oldPosition = new Anchor(node.Anchor.Left, node.Anchor.Top, node.Anchor.Layer);
                    var oldSize = new Size(node.Size.Width, node.Size.Height);

                    // 收集所有Slot的完整状态
                    var slotStates = new List<(
                        IWorkflowSlotViewModel Slot,
                        List<IWorkflowSlotViewModel> Targets,
                        List<IWorkflowSlotViewModel> Sources,
                        List<(IWorkflowLinkViewModel Link, IWorkflowSlotViewModel Sender, IWorkflowSlotViewModel Receiver)> Links
                    )>();

                    foreach (var slot in oldSlots)
                    {
                        var targets = new List<IWorkflowSlotViewModel>(slot.Targets);
                        var sources = new List<IWorkflowSlotViewModel>(slot.Sources);
                        var slotLinks = new List<(IWorkflowLinkViewModel, IWorkflowSlotViewModel, IWorkflowSlotViewModel)>();

                        foreach (var link in tree.Links)
                        {
                            if (link.Sender == slot || link.Receiver == slot)
                            {
                                slotLinks.Add((link, link.Sender, link.Receiver));
                            }
                        }

                        slotStates.Add((slot, targets, sources, slotLinks));
                    }

                    tree.GetHelper().Submit(new WorkflowActionPair(
                        // Redo: 删除节点及其所有内容
                        () =>
                        {
                            // 先删除所有Slot的连接
                            foreach (var slotState in slotStates)
                            {
                                var slot = slotState.Slot;

                                // 删除出站连接
                                foreach (var target in new List<IWorkflowSlotViewModel>(slot.Targets))
                                {
                                    if (slot.Targets.Contains(target))
                                        slot.Targets.Remove(target);
                                    if (target.Sources.Contains(slot))
                                        target.Sources.Remove(slot);

                                    var linkToRemove = tree.Links.FirstOrDefault(l => l.Sender == slot && l.Receiver == target);
                                    if (linkToRemove != null)
                                        tree.Links.Remove(linkToRemove);

                                    // 更新目标Slot状态
                                    if (target.Sources.Count == 0)
                                    {
                                        target.State &= ~SlotState.Processor;
                                        if (target.Targets.Count == 0)
                                            target.State = SlotState.StandBy;
                                    }
                                }

                                // 删除入站连接
                                foreach (var source in new List<IWorkflowSlotViewModel>(slot.Sources))
                                {
                                    if (source.Targets.Contains(slot))
                                        source.Targets.Remove(slot);
                                    if (slot.Sources.Contains(source))
                                        slot.Sources.Remove(source);

                                    var linkToRemove = tree.Links.FirstOrDefault(l => l.Sender == source && l.Receiver == slot);
                                    if (linkToRemove != null)
                                        tree.Links.Remove(linkToRemove);

                                    // 更新源Slot状态
                                    if (source.Targets.Count == 0)
                                    {
                                        source.State &= ~SlotState.Sender;
                                        if (source.Sources.Count == 0)
                                            source.State = SlotState.StandBy;
                                    }
                                }

                                slot.Targets.Clear();
                                slot.Sources.Clear();
                                slot.State = SlotState.StandBy;
                            }

                            // 从父节点移除所有Slot
                            node.Slots.Clear();

                            // 从树中移除节点
                            if (wasInTreeCollection)
                            {
                                tree.Nodes.Remove(node);
                                node.Parent = null;
                            }
                        },
                        // Undo: 恢复节点及其所有内容
                        () =>
                        {
                            // 恢复节点关系
                            node.Parent = oldParent;
                            if (wasInTreeCollection && !tree.Nodes.Contains(node))
                                tree.Nodes.Add(node);

                            // 恢复位置和尺寸
                            node.Anchor = oldPosition;
                            node.Size = oldSize;

                            // 恢复所有Slot到节点
                            foreach (var slotState in slotStates)
                            {
                                var slot = slotState.Slot;
                                slot.Parent = node;
                                if (!node.Slots.Contains(slot))
                                    node.Slots.Add(slot);

                                // 恢复出站连接
                                foreach (var target in slotState.Targets)
                                {
                                    if (!slot.Targets.Contains(target))
                                        slot.Targets.Add(target);
                                    if (!target.Sources.Contains(slot))
                                        target.Sources.Add(slot);

                                    var existingLink = tree.Links.FirstOrDefault(l => l.Sender == slot && l.Receiver == target);
                                    if (existingLink == null)
                                    {
                                        var newLink = tree.GetHelper().CreateLink(slot, target);
                                        if (!tree.Links.Contains(newLink))
                                            tree.Links.Add(newLink);
                                    }

                                    target.State |= SlotState.Processor;
                                    target.State &= ~SlotState.PreviewProcessor;
                                }

                                // 恢复入站连接
                                foreach (var source in slotState.Sources)
                                {
                                    if (!source.Targets.Contains(slot))
                                        source.Targets.Add(slot);
                                    if (!slot.Sources.Contains(source))
                                        slot.Sources.Add(source);

                                    var existingLink = tree.Links.FirstOrDefault(l => l.Sender == source && l.Receiver == slot);
                                    if (existingLink == null)
                                    {
                                        var newLink = tree.GetHelper().CreateLink(source, slot);
                                        if (!tree.Links.Contains(newLink))
                                            tree.Links.Add(newLink);
                                    }

                                    source.State |= SlotState.Sender;
                                    source.State &= ~SlotState.PreviewSender;
                                }

                                // 恢复Slot状态
                                if (slotState.Targets.Count > 0)
                                    slot.State |= SlotState.Sender;
                                if (slotState.Sources.Count > 0)
                                    slot.State |= SlotState.Processor;
                                if (slotState.Targets.Count == 0 && slotState.Sources.Count == 0)
                                    slot.State = SlotState.StandBy;

                                // 更新锚点
                                slot.GetHelper().UpdateAnchor();
                            }
                        }
                    ));
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
                        Sender = new SlotViewModelBase(),
                        Receiver = new SlotViewModelBase(),
                        IsVisible = true
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

                }
                public virtual void ReceiveConnection(IWorkflowSlotViewModel slot)
                {
                    if (_self == null || _sender == null) return;

                }
                public virtual void ResetVirtualLink()
                {
                    _self.VirtualLink.Sender.Anchor = new Anchor();
                    _self.VirtualLink.Receiver.Anchor = new Anchor();
                    _self.VirtualLink.IsVisible = false;
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