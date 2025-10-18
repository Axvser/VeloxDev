using System.Collections.Concurrent;
using System.Collections.ObjectModel;
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
            public static bool TryFindParentTree(object viewModel, out IWorkflowTreeViewModel? value)
            {
                switch (viewModel)
                {
                    case IWorkflowLinkViewModel link:
                        if (link.Sender is not null)
                        {
                            value = link.Sender.Parent?.Parent;
                            return value is not null;
                        }
                        else if (link.Receiver is not null)
                        {
                            value = link.Receiver.Parent?.Parent;
                            return value is not null;
                        }
                        else
                        {
                            value = null;
                            return false;
                        }
                    case IWorkflowLinkGroupViewModel linkGroup:
                        {
                            value = linkGroup.Parent;
                            return value is not null;
                        }
                    case IWorkflowSlotViewModel slot:
                        {
                            value = slot.Parent?.Parent;
                            return value is not null;
                        }
                    case IWorkflowNodeViewModel node:
                        {
                            value = node.Parent;
                            return value is not null;
                        }
                    default:
                        value = null;
                        return false;
                }
            }
            public static bool TryFindParentTree(IWorkflowLinkViewModel link, out IWorkflowTreeViewModel? value)
            {
                if (link.Sender is not null)
                {
                    value = link.Sender.Parent?.Parent;
                    return value is not null;
                }
                else if (link.Receiver is not null)
                {
                    value = link.Receiver.Parent?.Parent;
                    return value is not null;
                }
                else
                {
                    value = null;
                    return false;
                }
            }
            public static bool TryFindParentTree(IWorkflowLinkGroupViewModel linkGroup, out IWorkflowTreeViewModel? value)
            {
                value = linkGroup.Parent;
                return value is not null;
            }
            public static bool TryFindParentTree(IWorkflowSlotViewModel slot, out IWorkflowTreeViewModel? value)
            {
                value = slot.Parent?.Parent;
                return value is not null;
            }
            public static bool TryFindParentTree(IWorkflowNodeViewModel node, out IWorkflowTreeViewModel? value)
            {
                value = node.Parent;
                return value is not null;
            }

            #region Link Helper [ 官方固件 ]
            public class Link : IWorkflowLinkViewModelHelper
            {
                private IWorkflowLinkViewModel? viewModel = null;

                public virtual void Initialize(IWorkflowLinkViewModel link)
                {
                    viewModel = link;
                }
                public virtual Task CloseAsync() => Task.CompletedTask;
                public virtual void Dispose() { }

                public virtual void Delete()
                {
                    if (viewModel == null || !TryFindParentTree(viewModel, out var tree)) return;

                    var oldParent = viewModel.Parent;
                    var oldSender = viewModel.Sender;
                    var oldReceiver = viewModel.Receiver;
                    var oldVisible = viewModel.IsVisible;

                    // 从LinkGroup中移除
                    if (oldParent != null)
                    {
                        tree.GetHelper().Submit(new WorkflowActionPair(
                            () =>
                            {
                                if (oldParent != null) oldParent.Links.Remove(viewModel);
                                if (oldSender != null) oldSender.Targets.Remove(oldReceiver?.Parent);
                                if (oldReceiver != null) oldReceiver.Sources.Remove(oldSender?.Parent);
                                viewModel.Parent = null;
                                viewModel.Sender = null;
                                viewModel.Receiver = null;
                                viewModel.IsVisible = false;
                            },
                            () =>
                            {
                                if (oldParent != null) oldParent.Links.Add(viewModel);
                                if (oldSender != null) oldSender.Targets.Add(oldReceiver?.Parent);
                                if (oldReceiver != null) oldReceiver.Sources.Add(oldSender?.Parent);
                                viewModel.Parent = oldParent;
                                viewModel.Sender = oldSender;
                                viewModel.Receiver = oldReceiver;
                                viewModel.IsVisible = oldVisible;
                            }));
                    }
                }
            }
            #endregion

            #region LinkGroup Helper [ 官方固件 ]
            public class LinkGroup : IWorkflowLinkGroupViewModelHelper
            {
                private IWorkflowLinkGroupViewModel? viewModel = null;

                public virtual void Initialize(IWorkflowLinkGroupViewModel linkGroup)
                {
                    viewModel = linkGroup;
                }
                public virtual Task CloseAsync() => Task.CompletedTask;
                public virtual void Dispose() { }

                public virtual void Delete()
                {
                    if (viewModel == null || !TryFindParentTree(viewModel, out var tree)) return;

                    var oldParent = viewModel.Parent;
                    var oldLinks = new List<IWorkflowLinkViewModel>(viewModel.Links);

                    tree.GetHelper().Submit(new WorkflowActionPair(
                        () =>
                        {
                            if (oldParent != null) oldParent.LinkGroups.Remove(viewModel);

                            // 从LinkGroupMap中移除相关条目
                            foreach (var link in oldLinks)
                            {
                                if (link.Sender != null && link.Receiver != null)
                                {
                                    if (tree.LinkGroupMap.TryGetValue(link.Sender, out var receiverMap))
                                    {
                                        receiverMap.TryRemove(link.Receiver, out _);
                                        if (receiverMap.IsEmpty) tree.LinkGroupMap.TryRemove(link.Sender, out _);
                                    }
                                }
                            }

                            viewModel.Parent = null;
                            viewModel.Links.Clear();
                        },
                        () =>
                        {
                            if (oldParent != null) oldParent.LinkGroups.Add(viewModel);

                            // 恢复LinkGroupMap
                            foreach (var link in oldLinks)
                            {
                                if (link.Sender != null && link.Receiver != null)
                                {
                                    var receiverMap = tree.LinkGroupMap.GetOrAdd(link.Sender,
                                        new ConcurrentDictionary<IWorkflowSlotViewModel, IWorkflowLinkGroupViewModel>());
                                    receiverMap[link.Receiver] = viewModel;
                                }
                            }

                            viewModel.Parent = oldParent;
                            viewModel.Links = new ObservableCollection<IWorkflowLinkViewModel>(oldLinks);
                        }));
                }
            }
            #endregion

            #region Slot Helper [ 官方固件 ]
            public class Slot : IWorkflowSlotViewModelHelper
            {
                private IWorkflowSlotViewModel? viewModel = null;

                public virtual void Initialize(IWorkflowSlotViewModel slot) => viewModel = slot;
                public virtual Task CloseAsync() => Task.CompletedTask;
                public virtual void Dispose() { }

                public virtual void ApplyConnection()
                {
                    if (viewModel is null || !TryFindParentTree(viewModel, out var tree)) return;
                    tree.GetHelper().ApplyConnection(viewModel);
                }
                public virtual void ReceiveConnection()
                {
                    if (viewModel is null || !TryFindParentTree(viewModel, out var tree)) return;
                    tree.GetHelper().ReceiveConnection(viewModel);
                }

                public virtual void SetSize(Size size)
                {
                    if (viewModel is null || viewModel.Parent is null) return;
                    viewModel.Size = new Size(size.Width, size.Height);
                    UpdateAnchor();
                }
                public virtual void SetOffset(Offset offset)
                {
                    if (viewModel is null || viewModel.Parent is null) return;
                    viewModel.Offset = new Offset(offset.Left, offset.Top);
                    UpdateAnchor();
                }
                public virtual void SaveOffset()
                {
                    if (viewModel is null || !TryFindParentTree(viewModel, out var tree)) return;

                    var oldOffset = new Offset(viewModel.Offset.Left, viewModel.Offset.Top);
                    var newOffset = new Offset(viewModel.Offset.Left, viewModel.Offset.Top);

                    tree.GetHelper().Submit(new WorkflowActionPair(
                        () => SetOffset(newOffset),
                        () => SetOffset(oldOffset)
                    ));
                }
                public virtual void SaveSize()
                {
                    if (viewModel is null || !TryFindParentTree(viewModel, out var tree)) return;

                    var oldSize = new Size(viewModel.Size.Width, viewModel.Size.Height);
                    var newSize = new Size(viewModel.Size.Width, viewModel.Size.Height);

                    tree.GetHelper().Submit(new WorkflowActionPair(
                        () => SetSize(newSize),
                        () => SetSize(oldSize)
                    ));
                }
                private void UpdateAnchor()
                {
                    if (viewModel?.Parent == null) return;
                    viewModel.Anchor = new Anchor(
                        viewModel.Parent.Anchor.Left + viewModel.Offset.Left + viewModel.Size.Width / 2,
                        viewModel.Parent.Anchor.Top + viewModel.Offset.Top + viewModel.Size.Height / 2,
                        viewModel.Parent.Anchor.Layer + 1
                    );
                }

                public virtual void Delete()
                {
                    if (viewModel is null || viewModel.Parent is null || !TryFindParentTree(viewModel, out var tree)) return;

                    var oldParent = viewModel.Parent;
                    var oldTargets = new List<IWorkflowNodeViewModel>(viewModel.Targets);
                    var oldSources = new List<IWorkflowNodeViewModel>(viewModel.Sources);
                    var oldOffset = new Offset(viewModel.Offset.Left, viewModel.Offset.Top);
                    var oldSize = new Size(viewModel.Size.Width, viewModel.Size.Height);
                    var oldAnchor = new Anchor(viewModel.Anchor.Left, viewModel.Anchor.Top, viewModel.Anchor.Layer);

                    // 收集所有相关的连接
                    var affectedLinks = new List<IWorkflowLinkViewModel>();
                    foreach (var linkGroup in tree.LinkGroups)
                    {
                        foreach (var link in linkGroup.Links)
                        {
                            if (link.Sender == viewModel || link.Receiver == viewModel)
                            {
                                affectedLinks.Add(link);
                            }
                        }
                    }

                    tree.GetHelper().Submit(new WorkflowActionPair(
                        () =>
                        {
                            // 移除所有相关连接
                            foreach (var link in affectedLinks)
                            {
                                link.GetHelper().Delete();
                            }

                            // 从父节点移除
                            oldParent.Slots.Remove(viewModel);
                            viewModel.Parent = null;
                        },
                        () =>
                        {
                            // 恢复父节点关系
                            viewModel.Parent = oldParent;
                            oldParent.Slots.Add(viewModel);

                            // 恢复属性
                            viewModel.Offset = oldOffset;
                            viewModel.Size = oldSize;
                            viewModel.Anchor = oldAnchor;

                            // 恢复连接（需要更复杂的实现，这里简化）
                            foreach (var link in affectedLinks)
                            {
                                if (link.Parent != null) link.Parent.Links.Add(link);
                            }
                        }
                    ));
                }
            }
            #endregion

            #region Node Helper [ 官方固件 ]
            public class Node : IWorkflowNodeViewModelHelper
            {
                private IWorkflowNodeViewModel? viewModel = null;

                public virtual void Initialize(IWorkflowNodeViewModel node)
                {
                    viewModel = node;
                }
                public virtual Task BroadcastAsync(object? parameter) => Task.CompletedTask;
                public virtual Task WorkAsync(object? parameter) => Task.CompletedTask;
                public virtual void Dispose() { }

                public virtual void SaveAnchor()
                {
                    if (viewModel is null || !TryFindParentTree(viewModel, out var tree)) return;
                    var oldAnchor = viewModel.Anchor;
                    var newAnchor = new Anchor(viewModel.Anchor.Left, viewModel.Anchor.Top, viewModel.Anchor.Layer);
                    tree.GetHelper().Submit(new WorkflowActionPair(
                        () =>
                        {
                            SetAnchor(newAnchor);
                        },
                        () =>
                        {
                            SetAnchor(oldAnchor);
                        }));
                }
                public virtual void SaveSize()
                {
                    if (viewModel is null || !TryFindParentTree(viewModel, out var tree)) return;
                    var oldSize = viewModel.Size;
                    var newSize = new Size(viewModel.Size.Width, viewModel.Size.Height);
                    tree.GetHelper().Submit(new WorkflowActionPair(
                        () =>
                        {
                            SetSize(newSize);
                        },
                        () =>
                        {
                            SetSize(oldSize);
                        }));
                }
                public virtual void SetAnchor(Anchor newValue)
                {
                    if (viewModel is null) return;
                    viewModel.Anchor.Left = newValue.Left;
                    viewModel.Anchor.Top = newValue.Top;
                    foreach (var slot in viewModel.Slots)
                    {
                        slot.Anchor.Left = viewModel.Anchor.Left + slot.Offset.Left + slot.Size.Width / 2;
                        slot.Anchor.Top = viewModel.Anchor.Top + slot.Offset.Top + slot.Size.Height / 2;
                    }
                }
                public virtual void SetSize(Size newValue)
                {
                    if (viewModel is null) return;
                    viewModel.Size.Width = newValue.Width;
                    viewModel.Size.Height = newValue.Height;
                }

                public virtual async Task CloseAsync()
                {
                    if (viewModel == null) return;

                    // 锁定所有命令
                    var commands = new[]
                    {
                        viewModel.SaveAnchorCommand, viewModel.SaveSizeCommand, viewModel.SetAnchorCommand,
                        viewModel.SetSizeCommand, viewModel.CreateSlotCommand, viewModel.DeleteCommand,
                        viewModel.WorkCommand, viewModel.BroadcastCommand
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
                public virtual void CreateSlot(IWorkflowSlotViewModel slot)
                {
                    if (viewModel is null || !TryFindParentTree(viewModel, out var tree)) return;

                    var oldParent = slot.Parent;
                    var oldAnchor = new Anchor(slot.Anchor.Left, slot.Anchor.Top, slot.Anchor.Layer);
                    var oldOffset = new Offset(slot.Offset.Left, slot.Offset.Top);
                    var oldSize = new Size(slot.Size.Width, slot.Size.Height);

                    var newAnchor = new Anchor(
                        viewModel.Anchor.Left + slot.Offset.Left + slot.Size.Width / 2,
                        viewModel.Anchor.Top + slot.Offset.Top + slot.Size.Height / 2,
                        viewModel.Anchor.Layer + 1
                    );

                    tree.GetHelper().Submit(new WorkflowActionPair(
                        () =>
                        {
                            viewModel.Slots.Add(slot);
                            slot.Parent = viewModel;
                            slot.Anchor = newAnchor;
                        },
                        () =>
                        {
                            viewModel.Slots.Remove(slot);
                            slot.Parent = oldParent;
                            slot.Anchor = oldAnchor;
                            slot.Offset = oldOffset;
                            slot.Size = oldSize;
                        }));
                }
                public virtual void Delete()
                {
                    if (viewModel is null || !TryFindParentTree(viewModel, out var tree)) return;

                    var oldParent = viewModel.Parent;
                    var oldNodes = new List<IWorkflowNodeViewModel>(tree.Nodes);

                    // 收集所有相关的连接和Slot
                    var affectedSlots = new List<IWorkflowSlotViewModel>(viewModel.Slots);
                    var affectedLinks = new List<IWorkflowLinkViewModel>();

                    foreach (var linkGroup in tree.LinkGroups)
                    {
                        foreach (var link in linkGroup.Links)
                        {
                            if (link.Sender?.Parent == viewModel || link.Receiver?.Parent == viewModel)
                            {
                                affectedLinks.Add(link);
                            }
                        }
                    }

                    tree.GetHelper().Submit(new WorkflowActionPair(
                        () =>
                        {
                            // 先删除所有相关连接
                            foreach (var link in affectedLinks)
                            {
                                link.GetHelper().Delete();
                            }

                            // 从树中移除节点
                            tree.Nodes.Remove(viewModel);
                            viewModel.Parent = null;
                        },
                        () =>
                        {
                            // 恢复节点到树中
                            viewModel.Parent = oldParent;
                            if (!tree.Nodes.Contains(viewModel))
                                tree.Nodes.Add(viewModel);

                            // 恢复连接（简化实现，实际需要更复杂的恢复逻辑）
                            foreach (var link in affectedLinks)
                            {
                                if (link.Parent != null && !link.Parent.Links.Contains(link))
                                    link.Parent.Links.Add(link);
                            }
                        }));
                }
            }
            #endregion

            #region Tree Helper [ 官方固件 ]
            public class Tree : IWorkflowTreeViewModelHelper
            {
                private IWorkflowTreeViewModel? viewModel = null;
                private IWorkflowSlotViewModel? connectionSender = null;
                private IWorkflowSlotViewModel? connectionReceiver = null;

                public virtual void Initialize(IWorkflowTreeViewModel tree) => viewModel = tree;
                public virtual async Task CloseAsync()
                {
                    if (viewModel is null) return;

                    // 收集所有需要操作的命令
                    var commandsToLock = new List<IVeloxCommand>();

                    foreach (var linkGroup in viewModel.LinkGroups)
                    {
                        commandsToLock.Add(linkGroup.DeleteCommand);
                        foreach (var link in linkGroup.Links)
                        {
                            commandsToLock.Add(link.DeleteCommand);
                        }
                    }

                    foreach (var node in viewModel.Nodes)
                    {
                        var nodeCommands = new[]
                        {
                            node.SaveAnchorCommand, node.SaveSizeCommand, node.SetAnchorCommand,
                            node.SetSizeCommand, node.CreateSlotCommand, node.DeleteCommand,
                            node.WorkCommand, node.BroadcastCommand
                        };
                        commandsToLock.AddRange(nodeCommands);

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
                public virtual void ResetVirtualLink()
                {
                    if (viewModel is null) return;
                    viewModel.VirtualLink.Sender = null;
                    viewModel.VirtualLink.Receiver = null;
                    viewModel.VirtualLink.IsVisible = false;
                    connectionSender = null;
                    connectionReceiver = null;
                }
                public virtual void CreateNode(IWorkflowNodeViewModel node)
                {
                    if (viewModel is null) return;

                    var oldParent = node.Parent;
                    var oldNodes = new List<IWorkflowNodeViewModel>(viewModel.Nodes);

                    Submit(new WorkflowActionPair(
                        () =>
                        {
                            node.Parent = viewModel;
                            if (!viewModel.Nodes.Contains(node))
                                viewModel.Nodes.Add(node);
                        },
                        () =>
                        {
                            node.Parent = oldParent;
                            viewModel.Nodes = new ObservableCollection<IWorkflowNodeViewModel>(oldNodes);
                        }));
                }
                public virtual void MovePointer(Anchor anchor)
                {
                    if (viewModel is null) return;

                    if (connectionSender != null)
                    {
                        viewModel.VirtualLink.Sender = connectionSender;
                        viewModel.VirtualLink.Receiver = new SlotViewModelBase() { Anchor = anchor };
                        viewModel.VirtualLink.IsVisible = true;
                    }
                    else
                    {
                        viewModel.VirtualLink.Receiver.Anchor = anchor;
                    }
                }

                #region Connection Manager [ 核心连接逻辑 ]
                public virtual void ApplyConnection(IWorkflowSlotViewModel slot)
                {
                    if (viewModel is null) return;

                    if (connectionSender == null)
                    {
                        // 开始新连接
                        connectionSender = slot;
                        viewModel.VirtualLink.Sender = slot;
                        viewModel.VirtualLink.IsVisible = true;
                        slot.State = SlotState.PreviewSender;
                    }
                    else if (connectionSender == slot)
                    {
                        // 取消连接
                        ResetVirtualLink();
                        slot.State = SlotState.StandBy;
                    }
                }
                public virtual void ReceiveConnection(IWorkflowSlotViewModel slot)
                {
                    if (viewModel is null || connectionSender == null) return;

                    if (CanConnect(connectionSender, slot))
                    {
                        connectionReceiver = slot;
                        slot.State = SlotState.PreviewProcessor;

                        // 创建实际的连接
                        CreateConnection(connectionSender, slot);
                        ResetVirtualLink();
                    }
                }
                private bool CanConnect(IWorkflowSlotViewModel sender, IWorkflowSlotViewModel receiver)
                {
                    if (sender == receiver) return false;
                    if (sender.Parent == receiver.Parent) return false; // 同一节点内的Slot不能连接

                    // 检查Channel兼容性
                    if (!sender.Channel.HasFlag(SlotChannel.OneSource) &&
                        !sender.Channel.HasFlag(SlotChannel.MultipleSources)) return false;

                    if (!receiver.Channel.HasFlag(SlotChannel.OneTarget) &&
                        !receiver.Channel.HasFlag(SlotChannel.MultipleTargets)) return false;

                    // 检查是否已存在连接
                    if (viewModel?.LinkGroupMap.TryGetValue(sender, out var receiverMap) == true)
                    {
                        if (receiverMap.ContainsKey(receiver)) return false;
                    }

                    return true;
                }
                private void CreateConnection(IWorkflowSlotViewModel sender, IWorkflowSlotViewModel receiver)
                {
                    if (viewModel == null) return;

                    // 创建LinkGroup（如果不存在）
                    var linkGroup = new LinkGroupViewModelBase();
                    var link = new LinkViewModelBase
                    {
                        Sender = sender,
                        Receiver = receiver,
                        IsVisible = true
                    };

                    linkGroup.Links.Add(link);

                    // 更新LinkGroupMap
                    var receiverMap = viewModel.LinkGroupMap.GetOrAdd(sender,
                        new ConcurrentDictionary<IWorkflowSlotViewModel, IWorkflowLinkGroupViewModel>());
                    receiverMap[receiver] = linkGroup;

                    // 更新状态
                    sender.State = SlotState.Sender;
                    receiver.State = SlotState.Processor;

                    // 添加到树中
                    viewModel.LinkGroups.Add(linkGroup);

                    // 记录操作以便撤销
                    Submit(new WorkflowActionPair(
                        () =>
                        {
                            // 重做：创建连接
                            viewModel.LinkGroups.Add(linkGroup);
                            receiverMap[receiver] = linkGroup;
                            sender.State = SlotState.Sender;
                            receiver.State = SlotState.Processor;
                        },
                        () =>
                        {
                            // 撤销：删除连接
                            viewModel.LinkGroups.Remove(linkGroup);
                            receiverMap.TryRemove(receiver, out _);
                            sender.State = SlotState.StandBy;
                            receiver.State = SlotState.StandBy;
                        }
                    ));
                }
                #endregion

                #region Redo & Undo [ 撤销与重做机制 ]
                private readonly ConcurrentStack<IWorkflowActionPair> redoStack = new();
                private readonly ConcurrentStack<IWorkflowActionPair> undoStack = new();
                private readonly object stackLock = new();

                public virtual void Redo()
                {
                    lock (stackLock)
                    {
                        if (redoStack.TryPop(out var pair))
                        {
                            try
                            {
                                pair.Redo.Invoke();
                                undoStack.Push(pair);
                            }
                            catch (Exception ex)
                            {
                                // 记录错误，但不中断执行
                                System.Diagnostics.Debug.WriteLine($"Redo failed: {ex.Message}");
                            }
                        }
                    }
                }
                public virtual void Submit(IWorkflowActionPair actionPair)
                {
                    lock (stackLock)
                    {
                        try
                        {
                            actionPair.Redo.Invoke();
                            undoStack.Push(actionPair);
                            redoStack.Clear(); // 新操作清空重做栈
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Submit failed: {ex.Message}");
                        }
                    }
                }
                public virtual void Undo()
                {
                    lock (stackLock)
                    {
                        if (undoStack.TryPop(out var pair))
                        {
                            try
                            {
                                pair.Undo.Invoke();
                                redoStack.Push(pair);
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"Undo failed: {ex.Message}");
                            }
                        }
                    }
                }
                public virtual void ClearHistory()
                {
                    lock (stackLock)
                    {
                        redoStack.Clear();
                        undoStack.Clear();
                    }
                }
                #endregion
            }
            #endregion
        }
    }
}