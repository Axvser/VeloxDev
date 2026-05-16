using VeloxDev.AOT;
using VeloxDev.WorkflowSystem;

namespace Demo.ViewModels.Workflow
{
    [AOTReflection]
    [WorkflowBuilder.Link<LinkHelper>]
    internal partial class LinkViewModel
    {
        public LinkViewModel() => InitializeWorkflow();

        // ↓ 扩展视图模型
    }
}
