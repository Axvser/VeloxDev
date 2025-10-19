using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Reflection;
using VeloxDev.Core.Interfaces.WorkflowSystem;
using VeloxDev.Core.MVVM;

namespace VeloxDev.Core.WorkflowSystem.Templates
{
    public partial class TreeViewModelBase : IWorkflowTreeViewModel
    {
        private IWorkflowTreeViewModelHelper Helper = new WorkflowHelper.ViewModel.Tree();

        public TreeViewModelBase() { InitializeWorkflow(); }

        [VeloxProperty] private IWorkflowLinkViewModel virtualLink = new LinkViewModelBase() { Sender = new SlotViewModelBase(), Receiver = new SlotViewModelBase() };
        [VeloxProperty] private ObservableCollection<IWorkflowNodeViewModel> nodes = [];
        [VeloxProperty] private ObservableCollection<IWorkflowLinkViewModel> links = [];
        [VeloxProperty] private Dictionary<IWorkflowSlotViewModel, Dictionary<IWorkflowSlotViewModel, IWorkflowLinkViewModel>> linksMap = [];

        [VeloxCommand]
        protected virtual Task CreateNode(object? parameter, CancellationToken ct)
        {
            if (parameter is not IWorkflowNodeViewModel node) return Task.CompletedTask;
            Helper.CreateNode(node);
            return Task.CompletedTask;
        }
        [VeloxCommand]
        protected virtual Task SetPointer(object? parameter, CancellationToken ct)
        {
            if (parameter is not Anchor anchor) return Task.CompletedTask;
            Helper.SetPointer(anchor);
            return Task.CompletedTask;
        }
        [VeloxCommand]
        protected virtual Task ResetVirtualLink(object? parameter, CancellationToken ct)
        {
            Helper.ResetVirtualLink();
            return Task.CompletedTask;
        }
        [VeloxCommand]
        protected virtual Task ApplyConnection(object? parameter, CancellationToken ct)
        {
            if (parameter is not IWorkflowSlotViewModel slot) return Task.CompletedTask;
            Helper.ApplyConnection(slot);
            return Task.CompletedTask;
        }
        [VeloxCommand]
        protected virtual Task ReceiveConnection(object? parameter, CancellationToken ct)
        {
            if (parameter is not IWorkflowSlotViewModel slot) return Task.CompletedTask;
            Helper.ReceiveConnection(slot);
            return Task.CompletedTask;
        }
        [VeloxCommand]
        protected virtual Task Submit(object? parameter, CancellationToken ct)
        {
            if (parameter is not IWorkflowActionPair actionPair) return Task.CompletedTask;
            Helper.Submit(actionPair);
            return Task.CompletedTask;
        }
        [VeloxCommand]
        protected virtual Task Redo(object? parameter, CancellationToken ct)
        {
            Helper.Redo();
            return Task.CompletedTask;
        }
        [VeloxCommand]
        protected virtual Task Undo(object? parameter, CancellationToken ct)
        {
            Helper.Undo();
            return Task.CompletedTask;
        }
        [VeloxCommand]
        protected virtual async Task Close(object? parameter, CancellationToken ct)
        {
            await Helper.CloseAsync();
        }

        public virtual IWorkflowTreeViewModelHelper GetHelper() => Helper;
        public virtual void InitializeWorkflow() => Helper.Initialize(this);
        public virtual void SetHelper(IWorkflowTreeViewModelHelper helper)
        {
            helper.Initialize(this);
            Helper = helper;
        }
    }
}
