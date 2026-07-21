# Tree

通过 `[WorkflowBuilder.Tree<THelper>]` 扩展 `TreeDefaultViewModel`。

```csharp
using VeloxDev.WorkflowSystem;

public class MyTreeHelper : TreeHelper<MyTreeViewModel>
{
    public override void CreateNode(IWorkflowNodeViewModel node)
    {
        base.CreateNode(node);
        // 自定义创建逻辑
    }
}

[WorkflowBuilder.Tree<MyTreeHelper>]
public partial class MyTreeViewModel
{
    public MyTreeViewModel() => InitializeWorkflow();

    [VeloxProperty] private string workspaceName = "默认工作区";
}
```
