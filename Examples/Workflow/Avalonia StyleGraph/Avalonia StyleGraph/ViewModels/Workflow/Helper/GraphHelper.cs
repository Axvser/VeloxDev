using System;
using System.Collections.Generic;
using VeloxDev.Core.Interfaces.WorkflowSystem;
using VeloxDev.Core.WorkflowSystem;

namespace Avalonia_StyleGraph.ViewModels.Workflow.Helper
{
    public class GraphHelper : WorkflowHelper.ViewModel.Tree
    {
        private static readonly HashSet<int> map =
            [
                HashCode.Combine(typeof(HoverTriggerViewModel),typeof(HoverProcessorViewModel)),
                HashCode.Combine(typeof(HoverStyleViewModel),typeof(HoverTriggerViewModel)),
            ];

        public override bool ValidateConnection(IWorkflowSlotViewModel sender, IWorkflowSlotViewModel receiver)
        {
            if(sender.Parent is null || receiver.Parent is null) return false;
            return map.Contains(HashCode.Combine(sender.Parent.GetType(), receiver.Parent.GetType()));
        }
    }
}
