﻿using VeloxDev.Core.AOT;
using VeloxDev.Core.Interfaces.WorkflowSystem;
using VeloxDev.Core.MVVM;

namespace VeloxDev.Core.WorkflowSystem.Templates
{
    [AOTReflection(Constructors: true, Methods: true, Properties: true, Fields: true)]
    public partial class LinkViewModelBase : IWorkflowLinkViewModel
    {
        private IWorkflowLinkViewModelHelper Helper = new WorkflowHelper.ViewModel.Link();

        public LinkViewModelBase() { InitializeWorkflow(); }

        [VeloxProperty] private IWorkflowSlotViewModel sender = new SlotViewModelBase();
        [VeloxProperty] private IWorkflowSlotViewModel receiver = new SlotViewModelBase();
        [VeloxProperty] private bool isVisible = false;

        [VeloxCommand]
        protected virtual Task Delete(object? parameter, CancellationToken ct)
        {
            Helper.Delete();
            return Task.CompletedTask;
        }
        [VeloxCommand]
        protected virtual async Task Close(object? parameter, CancellationToken ct)
        {
            await Helper.CloseAsync();
        }

        public virtual IWorkflowLinkViewModelHelper GetHelper() => Helper;
        public virtual void InitializeWorkflow() => Helper.Initialize(this);
        public virtual void SetHelper(IWorkflowLinkViewModelHelper helper)
        {
            helper.Initialize(this);
            Helper = helper;
        }
    }
}
