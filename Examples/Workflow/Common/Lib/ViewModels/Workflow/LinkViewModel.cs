using VeloxDev.AI;
using VeloxDev.WorkflowSystem;

namespace Demo.ViewModels;

[AgentContext(AgentLanguages.Chinese, "派生的Link组件之一")]
[WorkflowBuilder.Link<LinkHelper>]
public partial class LinkViewModel
{
    public LinkViewModel() => InitializeWorkflow();

    // …… 自由扩展您的连接线视图模型
}