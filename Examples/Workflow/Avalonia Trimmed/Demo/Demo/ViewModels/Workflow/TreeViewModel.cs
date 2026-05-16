using Demo.ViewModels.Workflow.Helper;
using VeloxDev.AOT;
using VeloxDev.WorkflowSystem;

namespace Demo.ViewModels.Workflow
{
    [AOTReflection]
    [WorkflowBuilder.Tree<VirtualizeHelper>]
    internal partial class TreeViewModel
    {
        public TreeViewModel() => InitializeWorkflow();

        // ↓ 扩展视图模型
    }
}
