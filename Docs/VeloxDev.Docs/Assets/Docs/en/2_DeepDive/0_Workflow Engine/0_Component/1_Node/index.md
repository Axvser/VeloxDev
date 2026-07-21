# Node

The `NodeDefaultViewModel` is the base class for all executable nodes.

```csharp
public partial class NodeDefaultViewModel : IWorkflowNodeViewModel
{
    [VeloxProperty] private IWorkflowTreeViewModel? parent;
    [VeloxProperty] private Anchor anchor = new();
    [VeloxProperty] private Size size = new();
    [VeloxProperty] private ObservableCollection<IWorkflowSlotViewModel> slots = [];
}
```

Custom business logic is injected via an `IWorkflowNodeViewModelHelper`. The helper is attached via `[WorkflowBuilder.Node<THelper>]` at build time.
