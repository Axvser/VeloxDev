# Node

`NodeViewModelBase` 是所有可执行节点的基类。

```csharp
public partial class NodeViewModelBase : IWorkflowNodeViewModel
{
    [VeloxProperty] private Anchor anchor = new();
    [VeloxProperty] private Size size = new();
    [VeloxProperty] private ObservableCollection<IWorkflowSlotViewModel> slots = [];
}
```

通过 `[WorkflowBuilder.Node<THelper>]` 特性在编译时注入自定义业务逻辑 Helper。
