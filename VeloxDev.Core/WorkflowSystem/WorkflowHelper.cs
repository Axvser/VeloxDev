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
            public static bool TryFindParentTree(object viewModel, out IWorkflowTreeViewModel? value)
            {
                switch (viewModel)
                {
                    case IWorkflowLinkViewModel link:
                        if (link.Sender is not null)
                        {
                            value = link.Sender.Parent.Parent;
                            return value is null;
                        }
                        else if (link.Receiver is not null)
                        {
                            value = link.Receiver.Parent.Parent;
                            return value is null;
                        }
                        else
                        {
                            value = null;
                            return false;
                        }
                    case IWorkflowLinkGroupViewModel linkGroup:
                        {
                            value = linkGroup.Parent;
                            return value is null;
                        }
                    case IWorkflowSlotViewModel slot:
                        {
                            value = slot.Parent.Parent;
                            return value is null;
                        }
                    case IWorkflowNodeViewModel node:
                        {
                            value = node.Parent;
                            return value is null;
                        }
                    case IWorkflowTreeViewModel tree:
                        {
                            value = tree;
                            return true;
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
                    value = link.Sender.Parent.Parent;
                    return value is null;
                }
                else if (link.Receiver is not null)
                {
                    value = link.Receiver.Parent.Parent;
                    return value is null;
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
                return value is null;
            }
            public static bool TryFindParentTree(IWorkflowSlotViewModel slot, out IWorkflowTreeViewModel? value)
            {
                value = slot.Parent.Parent;
                return value is null;
            }
            public static bool TryFindParentTree(IWorkflowNodeViewModel node, out IWorkflowTreeViewModel? value)
            {
                value = node.Parent;
                return value is null;
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

                }
            }
            #endregion

            #region Slot Helper [ 官方固件 ]
            public class Slot : IWorkflowSlotViewModelHelper
            {
                private IWorkflowSlotViewModel? viewModel = null;

                public virtual void Initialize(IWorkflowSlotViewModel slot)
                {
                    viewModel = slot;
                }
                public virtual Task CloseAsync() => Task.CompletedTask;
                public virtual void Dispose() { }

                public virtual void ApplyConnection()
                {
                    if (viewModel is null || !TryFindParentTree(this, out var tree)) return;
                    tree.GetHelper().ApplyConnection(viewModel);
                }
                public virtual void ReceiveConnection()
                {
                    if (viewModel is null || !TryFindParentTree(this, out var tree)) return;
                    tree.GetHelper().ReceiveConnection(viewModel);
                }
                public virtual void Size(Size size)
                {
                    if (viewModel is null || viewModel.Parent is null) return;
                    viewModel.Size.Width = size.Width;
                    viewModel.Size.Height = size.Height;
                    viewModel.Anchor.Left = viewModel.Parent.Anchor.Left + viewModel.Offset.Left + viewModel.Size.Width / 2;
                    viewModel.Anchor.Top = viewModel.Parent.Anchor.Top + viewModel.Offset.Top + viewModel.Size.Height / 2;
                }
                public virtual void Offset(Offset offset)
                {
                    if (viewModel is null || viewModel.Parent is null) return;
                    viewModel.Offset.Left = offset.Left;
                    viewModel.Offset.Top = offset.Top;
                    viewModel.Anchor.Left = viewModel.Parent.Anchor.Left + viewModel.Offset.Left + viewModel.Size.Width / 2;
                    viewModel.Anchor.Top = viewModel.Parent.Anchor.Top + viewModel.Offset.Top + viewModel.Size.Height / 2;
                }

                public virtual void SaveOffset()
                {
                    throw new NotImplementedException();
                }
                public virtual void SaveSize()
                {
                    throw new NotImplementedException();
                }
                public virtual void Delete()
                {
                    throw new NotImplementedException();
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

                public void SaveAnchor()
                {
                    if (viewModel is null || !TryFindParentTree(viewModel, out var tree)) return;
                    var oldAnchor = viewModel.Anchor;
                    var newAnchor = new Anchor(viewModel.Anchor.Left, viewModel.Anchor.Top, viewModel.Anchor.Layer);
                    tree.GetHelper().Submit(new WorkflowActionPair(
                        () =>
                        {
                            Move(newAnchor);
                        },
                        () =>
                        {
                            Move(oldAnchor);
                        }));
                }
                public void SaveSize()
                {
                    if (viewModel is null || !TryFindParentTree(viewModel, out var tree)) return;
                    var oldSize = viewModel.Size;
                    var newSize = new Size(viewModel.Size.Width, viewModel.Size.Height);
                    tree.GetHelper().Submit(new WorkflowActionPair(
                        () =>
                        {
                            Scale(newSize);
                        },
                        () =>
                        {
                            Scale(oldSize);
                        }));
                }
                public virtual void Move(Anchor newValue)
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
                public virtual void Scale(Size newValue)
                {
                    if (viewModel is null) return;
                    viewModel.Size.Width = newValue.Width;
                    viewModel.Size.Height = newValue.Height;
                }

                public virtual Task CloseAsync()
                {
                    // 1. 识别可达性，从Node的每个Link的Source开始，溯源所有可能引发此节点继续执行任务的Node
                    // 2. 模仿TreeHelper中的Close逻辑，先锁定除Close的所有Command，然后中断，最后解锁
                }
                public virtual void CreateSlot(IWorkflowSlotViewModel slot)
                {
                    if (viewModel is null || !TryFindParentTree(this, out var tree)) return;
                    var oldParent = slot.Parent;
                    var oldAnchor = slot.Anchor;
                    var oldOffset = slot.Offset;
                    var oldSize = slot.Size;
                    var newAnchor = viewModel.Anchor + new Anchor(slot.Offset.Left, slot.Offset.Top) + new Anchor(slot.Size.Width / 2, slot.Size.Height / 2);
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
                    if (viewModel is null || !TryFindParentTree(this, out var tree)) return;
                    var oldParent = viewModel.Parent;

                    Dictionary<IWorkflowSlotViewModel, ConcurrentDictionary<IWorkflowSlotViewModel, IWorkflowLinkGroupViewModel>> oldMap = [];
                    HashSet<IWorkflowLinkGroupViewModel> oldLinkGroups = [];
                    // 自行补充更多细节
                    foreach (var slot in viewModel.Slots)
                    {
                        if (tree.LinkGroupMap.TryGetValue(slot, out var linkGroupMap))
                        {
                            // 应该是两条Map，它双向存储关系图，然后哈希表内存放需要移除的
                            // 注意slot自身的Targets和Sources也要同步、Slot的State也要同步
                            // 撤销时，oldMap和oldLinkGroups也要用于恢复tree中的上下文
                        }
                    }

                    tree.GetHelper().Submit(new WorkflowActionPair(
                        () =>
                        {
                            tree.Nodes.Remove(viewModel);
                            viewModel.Parent = null;
                        },
                        () =>
                        {
                            tree.Nodes.Add(viewModel);
                            viewModel.Parent = oldParent;
                        }));
                }
            }
            #endregion

            #region Tree Helper [ 官方固件 ]
            public class Tree : IWorkflowTreeViewModelHelper
            {
                private IWorkflowTreeViewModel? viewModel = null;

                #region Base
                public virtual void Initialize(IWorkflowTreeViewModel tree)
                {
                    viewModel = tree;
                }
                public virtual async Task CloseAsync()
                {
                    if (viewModel is null) return;
                    foreach (var linkGroup in viewModel.LinkGroups)
                    {
                        linkGroup.DeleteCommand.Lock();
                        foreach (var link in linkGroup.Links)
                        {
                            link.DeleteCommand.Lock();
                        }
                    }
                    foreach (var node in viewModel.Nodes)
                    {
                        node.SaveAnchorCommand.Lock();
                        node.SaveSizeCommand.Lock();
                        node.MoveCommand.Lock();
                        node.ScaleCommand.Lock();
                        node.CreateSlotCommand.Lock();
                        node.DeleteCommand.Lock();
                        node.WorkCommand.Lock();
                        node.BroadcastCommand.Lock();
                        foreach (var slot in node.Slots)
                        {
                            slot.SaveOffsetCommand.Lock();
                            slot.SaveSizeCommand.Lock();
                            slot.OffsetCommand.Lock();
                            slot.SizeCommand.Lock();
                            slot.ApplyConnectionCommand.Lock();
                            slot.ReceiveConnectionCommand.Lock();
                            slot.DeleteCommand.Lock();
                        }
                    }
                    foreach (var linkGroup in viewModel.LinkGroups)
                    {
                        linkGroup.DeleteCommand.Interrupt();
                        foreach (var link in linkGroup.Links)
                        {
                            link.DeleteCommand.Interrupt();
                        }
                    }
                    foreach (var node in viewModel.Nodes)
                    {
                        node.SaveAnchorCommand.Interrupt();
                        node.SaveSizeCommand.Interrupt();
                        node.MoveCommand.Interrupt();
                        node.ScaleCommand.Interrupt();
                        node.CreateSlotCommand.Interrupt();
                        node.DeleteCommand.Interrupt();
                        node.WorkCommand.Interrupt();
                        node.BroadcastCommand.Interrupt();
                        foreach (var slot in node.Slots)
                        {
                            slot.SaveOffsetCommand.Interrupt();
                            slot.SaveSizeCommand.Interrupt();
                            slot.OffsetCommand.Interrupt();
                            slot.SizeCommand.Interrupt();
                            slot.ApplyConnectionCommand.Interrupt();
                            slot.ReceiveConnectionCommand.Interrupt();
                            slot.DeleteCommand.Interrupt();
                        }
                    }
                    foreach (var linkGroup in viewModel.LinkGroups)
                    {
                        linkGroup.DeleteCommand.UnLock();
                        foreach (var link in linkGroup.Links)
                        {
                            link.DeleteCommand.UnLock();
                        }
                    }
                    foreach (var node in viewModel.Nodes)
                    {
                        node.SaveAnchorCommand.UnLock();
                        node.SaveSizeCommand.UnLock();
                        node.MoveCommand.UnLock();
                        node.ScaleCommand.UnLock();
                        node.CreateSlotCommand.UnLock();
                        node.DeleteCommand.UnLock();
                        node.WorkCommand.UnLock();
                        node.BroadcastCommand.UnLock();
                        foreach (var slot in node.Slots)
                        {
                            slot.SaveOffsetCommand.UnLock();
                            slot.SaveSizeCommand.UnLock();
                            slot.OffsetCommand.UnLock();
                            slot.SizeCommand.UnLock();
                            slot.ApplyConnectionCommand.UnLock();
                            slot.ReceiveConnectionCommand.UnLock();
                            slot.DeleteCommand.UnLock();
                        }
                    }
                }
                public virtual void Dispose() { }
                public virtual void ResetVirtualLink()
                {
                    if (viewModel is null) return;
                    viewModel.VirtualLink.Sender.Anchor = new();
                    viewModel.VirtualLink.Receiver.Anchor = new();
                    viewModel.VirtualLink.IsVisible = false;
                }
                public virtual void CreateNode(IWorkflowNodeViewModel node)
                {
                    if (viewModel is null || !TryFindParentTree(viewModel, out var tree)) return;
                    node.GetHelper().Delete();
                    var oldParent = node.Parent;
                    Submit(new WorkflowActionPair(
                        () =>
                        {
                            node.Parent = viewModel;
                            tree.Nodes.Add(node);
                        },
                        () =>
                        {
                            node.Parent = oldParent;
                            tree.Nodes.Remove(node);
                        }));
                }
                public virtual void MovePointer(Anchor anchor)
                {
                    if (viewModel is null) return;
                    viewModel.VirtualLink.Receiver.Anchor = anchor;
                }
                #endregion

                #region Connection Manager
                private IWorkflowSlotViewModel? sender = null;
                private IWorkflowSlotViewModel? receiver = null;
                public virtual void ApplyConnection(IWorkflowSlotViewModel slot)
                {
                    if (viewModel is null) return;
                    // 补充实现
                }
                public virtual void ReceiveConnection(IWorkflowSlotViewModel slot)
                {
                    if (viewModel is null) return;
                    // 补充实现
                }
                #endregion

                #region Redo & Undo
                private readonly ConcurrentStack<IWorkflowActionPair> redoStack = [];
                private readonly ConcurrentStack<IWorkflowActionPair> undoStack = [];
                public virtual void Redo()
                {
                    if (redoStack.TryPop(out var pair))
                    {
                        pair.Redo.Invoke();
                        undoStack.Push(pair);
                    }
                }
                public virtual void Submit(IWorkflowActionPair actionPair)
                {
                    actionPair.Redo.Invoke();
                    undoStack.Push(actionPair);
                }
                public virtual void Undo()
                {
                    if (undoStack.TryPop(out var pair))
                    {
                        pair.Undo.Invoke();
                        redoStack.Push(pair);
                    }
                }
                public virtual void ClearHistory()
                {
                    redoStack.Clear();
                    undoStack.Clear();
                }
                #endregion
            }
            #endregion
        }
    }
}