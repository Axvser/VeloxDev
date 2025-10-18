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

                public virtual Task CloseAsync() => Task.CompletedTask;

                public virtual void Delete()
                {
                    if (viewModel == null || !TryFindParentTree(viewModel, out var tree)) return;

                }

                public virtual void Dispose() { }

                public virtual void Initialize(IWorkflowLinkViewModel link)
                {
                    viewModel = link;
                }
            }
            #endregion

            #region LinkGroup Helper [ 官方固件 ]
            public class LinkGroup : IWorkflowLinkGroupViewModelHelper
            {
                private IWorkflowLinkGroupViewModel? viewModel = null;

                public virtual Task CloseAsync() => Task.CompletedTask;

                public virtual void Delete()
                {
                    if (viewModel == null || !TryFindParentTree(viewModel, out var tree)) return;

                }

                public virtual void Dispose() { }

                public virtual void Initialize(IWorkflowLinkGroupViewModel linkGroup)
                {
                    viewModel = linkGroup;
                }
            }
            #endregion

            #region Slot Helper [ 官方固件 ]
            public class Slot : IWorkflowSlotViewModelHelper
            {
                private IWorkflowSlotViewModel? viewModel = null;

                public virtual void ApplyConnection()
                {
                    throw new NotImplementedException();
                }

                public virtual Task CloseAsync()
                {
                    throw new NotImplementedException();
                }

                public virtual void Delete()
                {
                    throw new NotImplementedException();
                }

                public virtual void Dispose()
                {
                    GC.SuppressFinalize(this);
                }

                public virtual void Initialize(IWorkflowSlotViewModel slot)
                {
                    viewModel = slot;
                }

                public virtual void OnAnchorChanged(Anchor oldValue, Anchor newValue)
                {
                    throw new NotImplementedException();
                }

                public virtual void OnChannelChanged(SlotChannel oldValue, SlotChannel newValue)
                {
                    throw new NotImplementedException();
                }

                public virtual void OnOffsetChanged(Anchor oldValue, Anchor newValue)
                {
                    throw new NotImplementedException();
                }

                public virtual void OnParentChanged(IWorkflowNodeViewModel? oldValue, IWorkflowNodeViewModel? newValue)
                {
                    throw new NotImplementedException();
                }

                public virtual void OnSizeChanged(Size oldValue, Size newValue)
                {
                    throw new NotImplementedException();
                }

                public virtual void OnStateChanged(SlotState oldValue, SlotState newValue)
                {
                    throw new NotImplementedException();
                }

                public virtual void Press(Anchor anchor)
                {
                    throw new NotImplementedException();
                }

                public virtual void ReceiveConnection()
                {
                    throw new NotImplementedException();
                }

                public virtual void Release(Anchor anchor)
                {
                    throw new NotImplementedException();
                }

                public virtual void Scale(Size size)
                {
                    throw new NotImplementedException();
                }

                public virtual void Translate(Offset offset)
                {
                    throw new NotImplementedException();
                }
            }
            #endregion

            #region Node Helper [ 官方固件 ]
            public class Node : IWorkflowNodeViewModelHelper
            {
                private IWorkflowNodeViewModel? viewModel = null;

                public virtual Task BroadcastAsync(object? parameter)
                {
                    throw new NotImplementedException();
                }

                public virtual Task CloseAsync()
                {
                    throw new NotImplementedException();
                }

                public virtual void CreateSlot(IWorkflowSlotViewModel viewModel)
                {
                    throw new NotImplementedException();
                }

                public virtual void Delete()
                {
                    throw new NotImplementedException();
                }

                public virtual void Dispose() { }

                public virtual void Initialize(IWorkflowNodeViewModel node)
                {
                    viewModel = node;
                }

                public virtual void Move(Anchor newValue)
                {
                    throw new NotImplementedException();
                }

                public virtual void OnAnchorChanged(Anchor oldValue, Anchor newValue)
                {
                    throw new NotImplementedException();
                }

                public virtual void OnSizeChanged(Size oldValue, Size newValue)
                {
                    throw new NotImplementedException();
                }

                public virtual void Press(Anchor newValue)
                {
                    throw new NotImplementedException();
                }

                public virtual void Release(Anchor newValue)
                {
                    throw new NotImplementedException();
                }

                public virtual void Scale(Size newValue)
                {
                    throw new NotImplementedException();
                }

                public virtual Task WorkAsync(object? parameter)
                {
                    throw new NotImplementedException();
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
                        node.PressCommand.Lock();
                        node.MoveCommand.Lock();
                        node.ScaleCommand.Lock();
                        node.ReleaseCommand.Lock();
                        node.CreateSlotCommand.Lock();
                        node.DeleteCommand.Lock();
                        node.WorkCommand.Lock();
                        node.BroadcastCommand.Lock();
                        foreach (var slot in node.Slots)
                        {
                            slot.PressCommand.Lock();
                            slot.TranslateCommand.Lock();
                            slot.ScaleCommand.Lock();
                            slot.ReleaseCommand.Lock();
                            slot.ApplyConnectionCommand.Lock();
                            slot.ReceiveConnectionCommand.Lock();
                            slot.DeleteCommand.Lock();
                        }
                    }
                    foreach (var linkGroup in viewModel.LinkGroups)
                    {
                        await linkGroup.DeleteCommand.InterruptAsync();
                        foreach (var link in linkGroup.Links)
                        {
                            await link.DeleteCommand.InterruptAsync();
                        }
                    }
                    foreach (var node in viewModel.Nodes)
                    {
                        await node.PressCommand.InterruptAsync();
                        await node.MoveCommand.InterruptAsync();
                        await node.ScaleCommand.InterruptAsync();
                        await node.ReleaseCommand.InterruptAsync();
                        await node.CreateSlotCommand.InterruptAsync();
                        await node.DeleteCommand.InterruptAsync();
                        await node.WorkCommand.InterruptAsync();
                        await node.BroadcastCommand.InterruptAsync();
                        foreach (var slot in node.Slots)
                        {
                            await slot.PressCommand.InterruptAsync();
                            await slot.TranslateCommand.InterruptAsync();
                            await slot.ScaleCommand.InterruptAsync();
                            await slot.ReleaseCommand.InterruptAsync();
                            await slot.ApplyConnectionCommand.InterruptAsync();
                            await slot.ReceiveConnectionCommand.InterruptAsync();
                            await slot.DeleteCommand.InterruptAsync();
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
                        node.PressCommand.UnLock();
                        node.MoveCommand.UnLock();
                        node.ScaleCommand.UnLock();
                        node.ReleaseCommand.UnLock();
                        node.CreateSlotCommand.UnLock();
                        node.DeleteCommand.UnLock();
                        node.WorkCommand.UnLock();
                        node.BroadcastCommand.UnLock();
                        foreach (var slot in node.Slots)
                        {
                            slot.PressCommand.UnLock();
                            slot.TranslateCommand.UnLock();
                            slot.ScaleCommand.UnLock();
                            slot.ReleaseCommand.UnLock();
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

                }
                public virtual void ReceiveConnection(IWorkflowSlotViewModel slot)
                {
                    if (viewModel is null) return;

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