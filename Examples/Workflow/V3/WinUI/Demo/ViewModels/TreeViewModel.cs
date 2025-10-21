using VeloxDev.Core.AOT;
using VeloxDev.Core.WorkflowSystem;

namespace Demo.ViewModels;

[AOTReflection(Constructors: true, Methods: true, Properties: true, Fields: true)]
[WorkflowBuilder.ViewModel.Tree<WorkflowHelper.ViewModel.Tree>]
public partial class TreeViewModel
{
    public TreeViewModel() => InitializeWorkflow();

    // …… 自由扩展您的工作流树视图模型
}