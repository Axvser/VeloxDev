using VeloxDev.AI;
using VeloxDev.MVVM;
using VeloxDev.WorkflowSystem;

namespace Demo.ViewModels;

[AgentContext(AgentLanguages.Chinese, "派生的Link组件之一")]
[WorkflowBuilder.Link<LinkHelper>]
public partial class LinkViewModel
{
    public LinkViewModel() => InitializeWorkflow();

    [AgentContext(AgentLanguages.Chinese, "True表示使用折线连接两个节点")]
    [VeloxProperty] private bool usePolyline = true;
}