# Business Tree

Extend `TreeViewModelBase` with `[WorkflowBuilder.Tree<THelper>]` for domain-specific behavior.

```csharp
using VeloxDev.WorkflowSystem;

public class MyTreeHelper : TreeHelper<MyTreeViewModel>
{
    public override void CreateNode(IWorkflowNodeViewModel node)
    {
        base.CreateNode(node);
        // Custom creation logic
    }
}

[WorkflowBuilder.Tree<MyTreeHelper>]
public partial class MyTreeViewModel
{
    public MyTreeViewModel() => InitializeWorkflow();

    [VeloxProperty] private string workspaceName = "Default";
}
```
