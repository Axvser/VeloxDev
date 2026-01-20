using Avalonia_StyleGraph.ViewModels.Workflow.Helper.StyleItems;
using System;
using System.Threading;
using System.Threading.Tasks;
using VeloxDev.Core.Interfaces.WorkflowSystem;
using VeloxDev.Core.WorkflowSystem;

namespace Avalonia_StyleGraph.ViewModels.Workflow.Helper
{
    public class TriggerHelper : WorkflowHelper.ViewModel.Node
    {
        private HoverTriggerViewModel? _viewModel;

        public override void Install(IWorkflowNodeViewModel node)
        {
            base.Install(node);
            _viewModel = node as HoverTriggerViewModel;
        }

        public override async Task WorkAsync(object? parameter, CancellationToken ct)
        {
            try
            {
                if (parameter is not BorderStyle style || _viewModel is null)
                    return;

                style.PointerHoverd = _viewModel.PointerHoverd;
                await BroadcastAsync(parameter, ct);
                return;
            }
            catch (OperationCanceledException) { return; }
            catch { throw; }
        }
    }
}
