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

                }

                public virtual void Delete()
                {

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
                        Receiver = new SlotViewModelBase()
                    };
                public virtual void CreateNode(IWorkflowNodeViewModel node)
                {

                }
                public virtual void MovePointer(Anchor anchor)
                {

                }
                #endregion

                #region Connection Manager
                private IWorkflowSlotViewModel? _sender = null;
                private IWorkflowSlotViewModel? _receiver = null;
                private bool _isbuilding = false;
                public virtual void ApplyConnection(IWorkflowSlotViewModel slot)
                {

                }
                public virtual void ReceiveConnection(IWorkflowSlotViewModel slot)
                {

                }
                public virtual void ResetVirtualLink()
                {

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