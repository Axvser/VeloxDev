using Avalonia_StyleGraph.ViewModels.Workflow.Helper;
using VeloxDev.Core.WorkflowSystem;

namespace Avalonia_StyleGraph.ViewModels.Workflow;

[WorkflowBuilder.ViewModel.Node
    <ProcessorHelper>
    (workSemaphore: 1)]
public partial class HoverProcessorViewModel
{
    public HoverProcessorViewModel() => InitializeWorkflow();
}
