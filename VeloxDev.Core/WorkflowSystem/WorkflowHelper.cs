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
            #region Link Helper [ 官方固件 ]
            public class Link : IWorkflowLinkViewModelHelper
            {
                private IWorkflowLinkViewModel? _self;

                public void Initialize(IWorkflowLinkViewModel link) => _self = link;
                public Task CloseAsync() => Task.CompletedTask;
                public void Dispose() { }

                public void Delete()
                {

                }
            }
            #endregion

            #region Slot Helper [ 官方固件 ]
            public class Slot : IWorkflowSlotViewModelHelper
            {
                private IWorkflowSlotViewModel? _self;

                #region Simple Components
                public void Initialize(IWorkflowSlotViewModel slot) => _self = slot;
                public Task CloseAsync() => Task.CompletedTask;
                public void Dispose() { }
                public void ApplyConnection()
                {
                    if (_self is null) return;

                    // 通过Parent链向上查找Tree
                    IWorkflowTreeViewModel? tree = null;
                    var current = _self.Parent?.Parent as IWorkflowTreeViewModel;
                    if (current != null)
                    {
                        tree = current;
                    }

                    tree?.GetHelper().ApplyConnection(_self);
                }
                public void ReceiveConnection()
                {
                    if (_self is null) return;

                    // 通过Parent链向上查找Tree
                    IWorkflowTreeViewModel? tree = null;
                    var current = _self.Parent?.Parent as IWorkflowTreeViewModel;
                    if (current != null)
                    {
                        tree = current;
                    }

                    tree?.GetHelper().ReceiveConnection(_self);
                }
                public void SetSize(Size size)
                {
                    if (_self is null) return;

                    // 直接替换引用，确保实时同步
                    _self.Size = new Size(size.Width, size.Height);

                    // 更新Anchor
                    if (_self.Parent != null)
                    {
                        _self.Anchor = new Anchor(
                            _self.Parent.Anchor.Left + _self.Offset.Left + _self.Size.Width / 2,
                            _self.Parent.Anchor.Top + _self.Offset.Top + _self.Size.Height / 2,
                            _self.Parent.Anchor.Layer + 1
                        );
                    }
                }
                public void SetOffset(Offset offset)
                {
                    if (_self is null) return;

                    // 直接替换引用，确保实时同步
                    _self.Offset = new Offset(offset.Left, offset.Top);

                    // 更新Anchor
                    if (_self.Parent != null)
                    {
                        _self.Anchor = new Anchor(
                            _self.Parent.Anchor.Left + _self.Offset.Left + _self.Size.Width / 2,
                            _self.Parent.Anchor.Top + _self.Offset.Top + _self.Size.Height / 2,
                            _self.Parent.Anchor.Layer + 1
                        );
                    }
                }
                public void SaveOffset()
                {
                    if (_self is null) return;

                    // 通过Parent链向上查找Tree
                    IWorkflowTreeViewModel? tree = null;
                    var current = _self.Parent?.Parent as IWorkflowTreeViewModel;
                    if (current != null)
                    {
                        tree = current;
                    }

                    if (tree == null) return;

                    var oldOffset = new Offset(_self.Offset.Left, _self.Offset.Top);
                    var newOffset = new Offset(_self.Offset.Left, _self.Offset.Top);

                    tree.GetHelper().Submit(new WorkflowActionPair(
                        () => SetOffset(newOffset),
                        () => SetOffset(oldOffset)
                    ));
                }
                public void SaveSize()
                {
                    if (_self is null) return;

                    // 通过Parent链向上查找Tree
                    IWorkflowTreeViewModel? tree = null;
                    var current = _self.Parent?.Parent as IWorkflowTreeViewModel;
                    if (current != null)
                    {
                        tree = current;
                    }

                    if (tree == null) return;

                    var oldSize = new Size(_self.Size.Width, _self.Size.Height);
                    var newSize = new Size(_self.Size.Width, _self.Size.Height);

                    tree.GetHelper().Submit(new WorkflowActionPair(
                        () => SetSize(newSize),
                        () => SetSize(oldSize)
                    ));
                }
                #endregion

                public void Delete()
                {

                }
            }
            #endregion

            #region Node Helper [ 官方固件 ]
            public class Node : IWorkflowNodeViewModelHelper
            {
                private IWorkflowNodeViewModel? _self;

                #region Simple Components
                public void Initialize(IWorkflowNodeViewModel node) => _self = node;
                public async Task CloseAsync()
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
                public Task BroadcastAsync(object? parameter) => Task.CompletedTask;
                public Task WorkAsync(object? parameter) => Task.CompletedTask;
                public void Dispose() { }
                public void SaveAnchor()
                {
                    if (_self is null) return;

                }
                public void SaveSize()
                {
                    if (_self is null) return;

                }
                public void SetAnchor(Anchor newValue)
                {
                    if (_self is null) return;

                }
                public void SetSize(Size newValue)
                {
                    if (_self is null) return;

                }
                #endregion

                #region Complex Components
                public void CreateSlot(IWorkflowSlotViewModel slot)
                {

                }

                public void Delete()
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
                public async Task CloseAsync()
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
                public void Dispose() { }
                public void CreateNode(IWorkflowNodeViewModel node)
                {

                }
                public void MovePointer(Anchor anchor)
                {

                }
                #endregion
                
                #region Connection Manager
                private IWorkflowSlotViewModel? _sender = null;
                private IWorkflowSlotViewModel? _receiver = null;
                private bool _isbuilding = false;
                public void ApplyConnection(IWorkflowSlotViewModel slot)
                {

                }
                public void ReceiveConnection(IWorkflowSlotViewModel slot)
                {

                }
                public void ResetVirtualLink()
                {

                }
                #endregion

                #region Redo & Undo
                private readonly ConcurrentStack<IWorkflowActionPair> _redoStack = new();
                private readonly ConcurrentStack<IWorkflowActionPair> _undoStack = new();
                private readonly object _stackLock = new();
                public void Redo()
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
                public void Submit(IWorkflowActionPair actionPair)
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
                public void Undo()
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
                public void ClearHistory()
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