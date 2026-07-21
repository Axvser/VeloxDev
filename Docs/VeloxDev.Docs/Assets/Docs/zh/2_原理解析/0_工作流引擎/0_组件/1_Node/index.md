# Node

`NodeDefaultViewModel` 是 `IWorkflowNodeViewModel` 的默认实现，封装了节点核心状态与命令。

```csharp
public sealed partial class NodeDefaultViewModel : IWorkflowNodeViewModel
{
    [VeloxProperty] private Anchor anchor = new();
    [VeloxProperty] private Size size = new();
    [VeloxProperty] private ObservableCollection<IWorkflowSlotViewModel> slots = [];
}
```

如需自定义节点，使用 `[WorkflowBuilder.Node<THelper>]` 特性声明（而非继承）：
