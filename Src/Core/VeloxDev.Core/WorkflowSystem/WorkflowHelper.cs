using System.Collections.Specialized;
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
                public virtual void Uninstall(IWorkflowLinkViewModel link)
                {
                    component = null;
                    commands = [];
                }
                public virtual void Closing() => commands.StandardClosing();
                public virtual async Task CloseAsync() => await commands.StandardCloseAsync();
                public virtual void Closed() => commands.StandardClosed();

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
                    slot.Targets.CollectionChanged += OnTargetsChanged;
                    slot.Sources.CollectionChanged += OnSourcesChanged;
                }
                public virtual void Uninstall(IWorkflowSlotViewModel slot)
                {
                    component = null;
                    commands = [];
                    slot.Targets.CollectionChanged -= OnTargetsChanged;
                    slot.Sources.CollectionChanged -= OnSourcesChanged;
                }

                public virtual void Closing() => commands.StandardClosing();
                public virtual async Task CloseAsync() => await commands.StandardCloseAsync();
                public virtual void Closed() => commands.StandardClosed();

                public virtual void SetSize(Size size) => component?.StandardSetSize(size);
                public virtual void SetOffset(Offset offset) => component?.StandardSetOffset(offset);
                public virtual void SetChannel(SlotChannel channel) => component?.StandardSetChannel(channel);
                public virtual void SetLayer(int layer) => component?.StandardSetLayer(layer);

                public virtual void UpdateLayout() => component?.StandardUpdateLayout();
                public virtual void UpdateState() => component?.StandardUpdateState();

                public virtual void SendConnection() => component?.StandardApplyConnection();
                public virtual void ReceiveConnection() => component?.StandardReceiveConnection();

                public virtual void Delete() => component?.StandardDelete();

                #region Data CallBack
                private void OnTargetsChanged(object? sender, NotifyCollectionChangedEventArgs e)
                {
                    switch (e.Action)
                    {
                        case NotifyCollectionChangedAction.Add:
                            if (e.NewItems is null) return;
                            foreach (var item in e.NewItems)
                            {
                                if (item is IWorkflowSlotViewModel slot)
                                {
                                    OnTargetAdded(slot);
                                }
                            }
                            break;
                        case NotifyCollectionChangedAction.Remove:
                            if (e.OldItems is null) return;
                            foreach (var item in e.OldItems)
                            {
                                if (item is IWorkflowSlotViewModel slot)
                                {
                                    OnTargetRemoved(slot);
                                }
                            }
                            break;
                    }
                }
                protected virtual void OnTargetAdded(IWorkflowSlotViewModel slot) { }
                protected virtual void OnTargetRemoved(IWorkflowSlotViewModel slot) { }

                private void OnSourcesChanged(object? sender, NotifyCollectionChangedEventArgs e)
                {
                    switch (e.Action)
                    {
                        case NotifyCollectionChangedAction.Add:
                            if (e.NewItems is null) return;
                            foreach (var item in e.NewItems)
                            {
                                if (item is IWorkflowSlotViewModel slot)
                                {
                                    OnSourceAdded(slot);
                                }
                            }
                            break;
                        case NotifyCollectionChangedAction.Remove:
                            if (e.OldItems is null) return;
                            foreach (var item in e.OldItems)
                            {
                                if (item is IWorkflowSlotViewModel slot)
                                {
                                    OnSourceRemoved(slot);
                                }
                            }
                            break;
                    }
                }
                protected virtual void OnSourceAdded(IWorkflowSlotViewModel slot) { }
                protected virtual void OnSourceRemoved(IWorkflowSlotViewModel slot) { }
                #endregion
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
                    node.Slots.CollectionChanged += OnSlotsChanged;
                }
                public virtual void Uninstall(IWorkflowNodeViewModel node)
                {
                    component = null;
                    commands = [];
                    node.Slots.CollectionChanged -= OnSlotsChanged;
                }
                public virtual void Closing() => commands.StandardClosing();
                public virtual async Task CloseAsync() => await commands.StandardCloseAsync();
                public virtual void Closed() => commands.StandardClosed();
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

                private void OnSlotsChanged(object? sender, NotifyCollectionChangedEventArgs e)
                {
                    switch (e.Action)
                    {
                        case NotifyCollectionChangedAction.Add:
                            if (e.NewItems is null) return;
                            foreach (var item in e.NewItems)
                            {
                                if (item is IWorkflowSlotViewModel slot)
                                {
                                    OnSlotAdded(slot);
                                }
                            }
                            break;
                        case NotifyCollectionChangedAction.Remove:
                            if (e.OldItems is null) return;
                            foreach (var item in e.OldItems)
                            {
                                if (item is IWorkflowSlotViewModel slot)
                                {
                                    OnSlotRemoved(slot);
                                }
                            }
                            break;
                    }
                }
                protected virtual void OnSlotAdded(IWorkflowSlotViewModel slot) { }
                protected virtual void OnSlotRemoved(IWorkflowSlotViewModel slot) { }
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
                    tree.Nodes.CollectionChanged += OnNodesChanged;
                    tree.Links.CollectionChanged += OnLinksChanged;
                }
                public virtual void Uninstall(IWorkflowTreeViewModel tree)
                {
                    component = null;
                    commands = [];
                    tree.Nodes.CollectionChanged -= OnNodesChanged;
                    tree.Links.CollectionChanged -= OnLinksChanged;
                }

                public virtual void Closing() => commands.StandardClosing();
                public virtual async Task CloseAsync()
                {
                    if (component is not null) await component.StandardCloseAsync();
                }
                public virtual void Closed() => commands.StandardClosed();

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

                #region Data CallBack
                private void OnNodesChanged(object? sender, NotifyCollectionChangedEventArgs e)
                {
                    switch (e.Action)
                    {
                        case NotifyCollectionChangedAction.Add:
                            if (e.NewItems is null) return;
                            foreach (var item in e.NewItems)
                            {
                                if (item is IWorkflowNodeViewModel node)
                                {
                                    OnNodeAdded(node);
                                }
                            }
                            break;
                        case NotifyCollectionChangedAction.Remove:
                            if (e.OldItems is null) return;
                            foreach (var item in e.OldItems)
                            {
                                if (item is IWorkflowNodeViewModel node)
                                {
                                    OnNodeRemoved(node);
                                }
                            }
                            break;
                    }
                }
                protected virtual void OnNodeAdded(IWorkflowNodeViewModel node) { }
                protected virtual void OnNodeRemoved(IWorkflowNodeViewModel node) { }

                private void OnLinksChanged(object? sender, NotifyCollectionChangedEventArgs e)
                {
                    switch (e.Action)
                    {
                        case NotifyCollectionChangedAction.Add:
                            if (e.NewItems is null) return;
                            foreach (var item in e.NewItems)
                            {
                                if (item is IWorkflowLinkViewModel link)
                                {
                                    OnLinkAdded(link);
                                }
                            }
                            break;
                        case NotifyCollectionChangedAction.Remove:
                            if (e.OldItems is null) return;
                            foreach (var item in e.OldItems)
                            {
                                if (item is IWorkflowLinkViewModel link)
                                {
                                    OnLinkRemoved(link);
                                }
                            }
                            break;
                    }
                }
                protected virtual void OnLinkAdded(IWorkflowLinkViewModel link) { }
                protected virtual void OnLinkRemoved(IWorkflowLinkViewModel link) { }
                #endregion

                #region Connection Manager
                public virtual bool ValidateConnection(
                    IWorkflowSlotViewModel sender,
                    IWorkflowSlotViewModel receiver)
                    => true;

                public virtual void SendConnection(IWorkflowSlotViewModel slot)
                    => component?.StandardApplyConnection(slot);

                public virtual void ReceiveConnection(IWorkflowSlotViewModel slot)
                    => component?.StandardReceiveConnection(slot);

                public virtual void ResetVirtualLink()
                    => component?.StandardResetVirtualLink();
                #endregion

                #region Redo & Undo
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