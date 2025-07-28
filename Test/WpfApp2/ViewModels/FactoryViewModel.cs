using VeloxDev.Core.WorkflowSystem;

namespace WpfApp2.ViewModels;

[Workflow.Context.Tree(typeof(SlotContext), typeof(LinkContext))]
public partial class FactoryViewModel
{
    public FactoryViewModel() { InitializeWorkflow(); }
}