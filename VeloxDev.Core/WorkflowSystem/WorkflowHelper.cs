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
            public static bool TryFindTree(object viewModel, out IWorkflowTreeViewModel? value)
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
                            return value is null;
                        }
                    default:
                        value = null;
                        return false;
                }
            }
            public static bool TryFindTree(IWorkflowLinkViewModel link, out IWorkflowTreeViewModel? value)
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
            public static bool TryFindTree(IWorkflowLinkGroupViewModel linkGroup, out IWorkflowTreeViewModel? value)
            {
                value = linkGroup.Parent;
                return value is null;
            }
            public static bool TryFindTree(IWorkflowSlotViewModel slot, out IWorkflowTreeViewModel? value)
            {
                value = slot.Parent.Parent;
                return value is null;
            }
            public static bool TryFindTree(IWorkflowNodeViewModel node, out IWorkflowTreeViewModel? value)
            {
                value = node.Parent;
                return value is null;
            }
            public static bool TryFindTree(IWorkflowTreeViewModel tree, out IWorkflowTreeViewModel? value)
            {
                value = tree;
                return true;
            }

            #region Link Helper
            public class Link : IWorkflowLinkViewModelHelper
            {
                private IWorkflowLinkViewModel? viewModel = null;

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

                public virtual void Initialize(IWorkflowLinkViewModel link)
                {
                    viewModel = link;
                }
            }
            #endregion

            #region LinkGroup Helper
            public class LinkGroup : IWorkflowLinkGroupViewModelHelper
            {
                private IWorkflowLinkGroupViewModel? viewModel = null;

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

                public virtual void Initialize(IWorkflowLinkGroupViewModel linkGroup)
                {
                    viewModel = linkGroup;
                }
            }
            #endregion

            #region Slot Helper
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

            #region Node Helper
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

                public virtual void Dispose()
                {
                    GC.SuppressFinalize(this);
                }

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

            #region Tree Helper
            public class Tree : IWorkflowTreeViewModelHelper
            {
                private IWorkflowTreeViewModel? viewModel = null;

                public virtual void Initialize(IWorkflowTreeViewModel tree)
                {
                    OnInitializing();
                    viewModel = tree;
                    OnInitialized();
                }
                public virtual async Task CloseAsync()
                {
                    if (viewModel is null) return;
                    await OnClosing();
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
                    await OnClosed();
                }
                public virtual void Dispose()
                {
                    GC.SuppressFinalize(this);
                }
                protected virtual void OnInitializing() { }
                protected virtual void OnInitialized() { }
                protected virtual Task OnClosing() => Task.CompletedTask;
                protected virtual Task OnClosed() => Task.CompletedTask;

                public virtual void CreateNode(IWorkflowNodeViewModel node)
                {
                    if (viewModel is null) return;

                }
                public virtual void MovePointer(Anchor anchor)
                {
                    if (viewModel is null) return;
                    OnPointerMoving(anchor);
                    viewModel.VirtualLink.Receiver.Anchor = anchor;
                    OnPointerMoved(anchor);
                }
                protected virtual void OnNodeCreating(IWorkflowNodeViewModel node) { }
                protected virtual void OnNodeCreated(IWorkflowNodeViewModel node) { }
                protected virtual void OnPointerMoving(Anchor anchor) { }
                protected virtual void OnPointerMoved(Anchor anchor) { }

                #region Connection Manager
                private IWorkflowSlotViewModel currentConnectionSender;
                public virtual void ApplyConnection(IWorkflowSlotViewModel slot)
                {
                    if (viewModel is null || slot is null) return;

                    OnConnectionAppling(slot);

                    // 设置当前连接发送端
                    currentConnectionSender = slot;
                    slot.State = SlotState.PreviewSender;

                    // 初始化虚拟连接线
                    if (viewModel.VirtualLink != null)
                    {
                        viewModel.VirtualLink.Sender = slot;
                        viewModel.VirtualLink.Receiver = null;
                    }

                    OnConnectionApplied(slot);
                }
                public virtual void ReceiveConnection(IWorkflowSlotViewModel slot)
                {
                    if (viewModel is null || currentConnectionSender is null || slot is null) return;

                    OnConnectionReceiving(slot);

                    // 验证连接有效性
                    if (ValidateConnection(currentConnectionSender, slot))
                    {
                        // 创建新连接（支持撤销重做）
                        CreateConnection(currentConnectionSender, slot);
                    }
                    else
                    {
                        // 连接无效，取消操作
                        CancelConnection();
                    }

                    OnConnectionReceived(slot);
                }

                protected virtual bool ValidateConnection(IWorkflowSlotViewModel sender, IWorkflowSlotViewModel receiver)
                {
                    if (sender == null || receiver == null) return false;

                    // 不能连接到同一节点
                    if (sender.Parent == receiver.Parent) return false;

                    // 通道兼容性检查
                    bool senderCanOutput = (sender.Channel & (SlotChannel.OneTarget | SlotChannel.MultipleTargets)) != 0;
                    bool receiverCanInput = (receiver.Channel & (SlotChannel.OneSource | SlotChannel.MultipleSources)) != 0;
                    if (!senderCanOutput || !receiverCanInput) return false;

                    // 连接限制检查
                    if ((sender.Channel & SlotChannel.OneTarget) != 0 && sender.Targets.Count >= 1) return false;
                    if ((receiver.Channel & SlotChannel.OneSource) != 0 && receiver.Sources.Count >= 1) return false;

                    // 重复连接检查
                    if (IsDuplicateConnection(sender, receiver)) return false;

                    return true;
                }
                protected virtual bool IsDuplicateConnection(IWorkflowSlotViewModel sender, IWorkflowSlotViewModel receiver)
                {
                    if (viewModel?.LinkGroups == null) return false;

                    foreach (var linkGroup in viewModel.LinkGroups)
                    {
                        foreach (var link in linkGroup.Links)
                        {
                            if (link.Sender == sender && link.Receiver == receiver)
                                return true;
                        }
                    }
                    return false;
                }
                protected virtual void CreateConnection(IWorkflowSlotViewModel sender, IWorkflowSlotViewModel receiver)
                {
                    var newLink = CreateNewLink();
                    if (newLink == null) return;

                    newLink.Sender = sender;
                    newLink.Receiver = receiver;

                    var actionPair = new WorkflowActionPair(
                        redo: () => ExecuteCreateConnection(sender, receiver, newLink),
                        undo: () => UndoCreateConnection(sender, receiver, newLink)
                    );

                    Submit(actionPair);
                    ClearConnectionState();
                }
                protected virtual void ExecuteCreateConnection(IWorkflowSlotViewModel sender, IWorkflowSlotViewModel receiver, IWorkflowLinkViewModel link)
                {
                    // 添加到LinkGroup
                    var linkGroup = GetOrCreateLinkGroup();
                    linkGroup.Links.Add(link);

                    // 建立Slot关系
                    if (!sender.Targets.Contains(receiver.Parent))
                        sender.Targets.Add(receiver.Parent);

                    if (!receiver.Sources.Contains(sender.Parent))
                        receiver.Sources.Add(sender.Parent);

                    // 更新状态
                    sender.State = SlotState.Sender;
                    receiver.State = SlotState.Processor;
                }
                protected virtual void UndoCreateConnection(IWorkflowSlotViewModel sender, IWorkflowSlotViewModel receiver, IWorkflowLinkViewModel link)
                {
                    // 从LinkGroup移除
                    foreach (var linkGroup in viewModel.LinkGroups)
                    {
                        if (linkGroup.Links.Contains(link))
                        {
                            linkGroup.Links.Remove(link);
                            break;
                        }
                    }

                    // 断开Slot关系
                    if (sender.Targets.Contains(receiver.Parent))
                        sender.Targets.Remove(receiver.Parent);

                    if (receiver.Sources.Contains(sender.Parent))
                        receiver.Sources.Remove(sender.Parent);

                    // 恢复状态
                    sender.State = SlotState.StandBy;
                    receiver.State = SlotState.StandBy;
                }
                protected virtual IWorkflowLinkGroupViewModel GetOrCreateLinkGroup()
                {
                    var linkGroup = viewModel.LinkGroups.FirstOrDefault();
                    if (linkGroup == null)
                    {
                        linkGroup = new LinkGroupViewModelBase();
                        viewModel.LinkGroups.Add(linkGroup);
                    }
                    return linkGroup;
                }
                protected virtual IWorkflowLinkViewModel CreateNewLink()
                {
                    return new LinkViewModelBase();
                }
                protected virtual void ClearConnectionState()
                {
                    currentConnectionSender = null;

                    if (viewModel?.VirtualLink != null)
                    {
                        viewModel.VirtualLink.Sender = null;
                        viewModel.VirtualLink.Receiver = null;
                    }
                }
                protected virtual void CancelConnection()
                {
                    if (currentConnectionSender != null)
                    {
                        currentConnectionSender.State = SlotState.StandBy;
                    }
                    ClearConnectionState();
                }

                protected virtual void OnConnectionAppling(IWorkflowSlotViewModel slot) { }
                protected virtual void OnConnectionApplied(IWorkflowSlotViewModel slot) { }
                protected virtual void OnConnectionReceiving(IWorkflowSlotViewModel slot) { }
                protected virtual void OnConnectionReceived(IWorkflowSlotViewModel slot) { }
                #endregion

                #region Redo & Undo
                private readonly ConcurrentStack<IWorkflowActionPair> redoStack = [];
                private readonly ConcurrentStack<IWorkflowActionPair> undoStack = [];
                public virtual void Redo()
                {
                    if (redoStack.TryPop(out var pair))
                    {
                        OnRedoing(pair);
                        pair.Redo.Invoke();
                        undoStack.Push(pair);
                        OnRedoed(pair);
                    }
                }
                public virtual void Submit(IWorkflowActionPair actionPair)
                {
                    OnSubmiting(actionPair);
                    actionPair.Redo.Invoke();
                    undoStack.Push(actionPair);
                    OnSubmited(actionPair);
                }
                public virtual void Undo()
                {
                    if (undoStack.TryPop(out var pair))
                    {
                        OnUndoing(pair);
                        pair.Undo.Invoke();
                        redoStack.Push(pair);
                        OnUndoed(pair);
                    }
                }
                protected virtual void OnRedoing(IWorkflowActionPair actionPair) { }
                protected virtual void OnRedoed(IWorkflowActionPair actionPair) { }
                protected virtual void OnSubmiting(IWorkflowActionPair actionPair) { }
                protected virtual void OnSubmited(IWorkflowActionPair actionPair) { }
                protected virtual void OnUndoing(IWorkflowActionPair actionPair) { }
                protected virtual void OnUndoed(IWorkflowActionPair actionPair) { }
                #endregion
            }
            #endregion
        }
    }
}