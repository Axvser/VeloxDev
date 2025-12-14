using VeloxDev.Core.Interfaces.MVVM;
using VeloxDev.Core.Interfaces.WorkflowSystem;
using VeloxDev.Core.WorkflowSystem.StandardEx;
using VeloxDev.Core.WorkflowSystem.Templates;

namespace VeloxDev.Core.WorkflowSystem
{
    public static class WorkflowHelper
    {
        public static class ViewModel
        {
            /// <summary>
            /// [ Component Helper ] Provide standard supports for Link Component
            /// </summary>
            public class Link : IWorkflowLinkViewModelHelper
            {
                private IWorkflowLinkViewModel? component;
                private IReadOnlyCollection<IVeloxCommand> commands = [];

                public virtual void Install(IWorkflowLinkViewModel link)
                {
                    component = link;
                    commands = link.GetStandardCommands();
                }
                public virtual void Uninstall(IWorkflowLinkViewModel link) { }
                public virtual void Closing() => commands.StandardClosing();
                public virtual async Task CloseAsync() => await commands.StandardCloseAsync();
                public virtual void Closed() => commands.StandardClosed();
                public virtual void Dispose() { GC.SuppressFinalize(this); }

                public virtual void Delete() => component?.StandardDelete();
            }

            /// <summary>
            /// [ Component Helper ] Provide standard supports for Slot Component
            /// </summary>
            public class Slot : IWorkflowSlotViewModelHelper
            {
                private IWorkflowSlotViewModel? component;
                private IReadOnlyCollection<IVeloxCommand> commands = [];

                public virtual void Install(IWorkflowSlotViewModel slot)
                {
                    component = slot;
                    commands = slot.GetStandardCommands();
                }
                public void Uninstall(IWorkflowSlotViewModel slot)
                {

                }
                public virtual void Closing() => commands.StandardClosing();
                public virtual async Task CloseAsync() => await commands.StandardCloseAsync();
                public virtual void Closed() => commands.StandardClosed();
                public virtual void Dispose() { GC.SuppressFinalize(this); }

                public virtual void SetSize(Size size) => component?.StandardSetSize(size);
                public virtual void SetOffset(Offset offset) => component?.StandardSetOffset(offset);
                public virtual void SetChannel(SlotChannel channel) => component?.StandardSetChannel(channel);
                public virtual void SetLayer(int layer) => component?.StandardSetLayer(layer);

                public virtual void UpdateAnchor() => component?.StandardUpdateAnchor();
                public virtual void UpdateState() => component?.StandardUpdateState();

                public virtual void ApplyConnection() => component?.StandardApplyConnection();
                public virtual void ReceiveConnection() => component?.StandardReceiveConnection();

                public virtual void Delete() => component?.StandardDelete();
            }

            /// <summary>
            /// [ Component Helper ] Provide standard supports for Node Component
            /// </summary>
            public class Node : IWorkflowNodeViewModelHelper
            {
                private IWorkflowNodeViewModel? component;
                private IReadOnlyCollection<IVeloxCommand> commands = [];

                public virtual void Install(IWorkflowNodeViewModel node)
                {
                    component = node;
                    commands = node.GetStandardCommands();
                }
                public void Uninstall(IWorkflowNodeViewModel node)
                {

                }
                public virtual void Closing() => commands.StandardClosing();
                public virtual async Task CloseAsync() => await commands.StandardCloseAsync();
                public virtual void Closed() => commands.StandardClosed();
                public virtual void Dispose() { GC.SuppressFinalize(this); }
                public virtual void CreateSlot(IWorkflowSlotViewModel slot) => component?.StandardCreateSlot(slot);

                public virtual async Task BroadcastAsync(
                    object? parameter,
                    CancellationToken ct)
                {
                    if (component is not null) await component.StandardBroadcastAsync(parameter, ct);
                }
                public virtual Task WorkAsync(
                    object? parameter,
                    CancellationToken ct)
                    => Task.CompletedTask;
                public virtual Task<bool> ValidateBroadcastAsync(
                    IWorkflowSlotViewModel sender,
                    IWorkflowSlotViewModel receiver,
                    object? parameter,
                    CancellationToken ct)
                    => Task.FromResult(true);

                public virtual void SetAnchor(Anchor anchor) => component?.StandardSetAnchor(anchor);
                public virtual void SetLayer(int layer) => component?.StandardSetLayer(layer);
                public virtual void SetSize(Size size) => component?.StandardSetSize(size);
                public virtual void Move(Offset offset) => component?.StandardMove(offset);

                public virtual void Delete() => component?.StandardDelete();
            }

            /// <summary>
            /// [ Component Helper ] Provide standard supports for Tree Component
            /// </summary>
            public class Tree : IWorkflowTreeViewModelHelper
            {
                private IWorkflowTreeViewModel? component = null;
                private IReadOnlyCollection<IVeloxCommand> commands = [];

                public virtual void Install(IWorkflowTreeViewModel tree)
                {
                    component = tree;
                    commands = tree.GetStandardCommands();
                }

                public void Uninstall(IWorkflowTreeViewModel tree) { }

                public virtual void Closing() => commands.StandardClosing();
                public virtual async Task CloseAsync() => await commands.StandardCloseAsync();
                public virtual void Closed() => commands.StandardClosed();
                public virtual void Dispose() { GC.SuppressFinalize(this); }

                public virtual IWorkflowLinkViewModel CreateLink(
                    IWorkflowSlotViewModel sender,
                    IWorkflowSlotViewModel receiver)
                    => new LinkViewModelBase()
                    {
                        Sender = new SlotViewModelBase() { Anchor = sender.Anchor },
                        Receiver = new SlotViewModelBase() { Anchor = receiver.Anchor },
                    };

                public virtual void CreateNode(IWorkflowNodeViewModel node)
                    => component?.StandardCreateNode(node);

                public virtual void SetPointer(Anchor anchor)
                    => component?.StandardSetPointer(anchor);

                #region Connection Manager - 替换为调用Ex方法
                public virtual bool ValidateConnection(
                    IWorkflowSlotViewModel sender,
                    IWorkflowSlotViewModel receiver)
                    => true;

                public virtual void ApplyConnection(IWorkflowSlotViewModel slot)
                    => component?.StandardApplyConnection(slot);

                public virtual void ReceiveConnection(IWorkflowSlotViewModel slot)
                    => component?.StandardReceiveConnection(slot);

                public virtual void ResetVirtualLink()
                    => component?.StandardResetVirtualLink();
                #endregion

                #region Redo & Undo - 替换为调用Ex方法
                public virtual void Redo()
                    => component?.StandardRedo();

                public virtual void Submit(IWorkflowActionPair actionPair)
                    => component?.StandardSubmit(actionPair);

                public virtual void Undo()
                    => component?.StandardUndo();

                public virtual void ClearHistory()
                    => component?.StandardClearHistory();
                #endregion
            }
        }
    }
}