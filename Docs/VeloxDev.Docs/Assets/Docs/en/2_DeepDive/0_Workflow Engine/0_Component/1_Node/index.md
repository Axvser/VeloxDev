# Node

`NodeDefaultViewModel` is the default implementation of `IWorkflowNodeViewModel`, encapsulating core node state and commands.

```csharp
public sealed partial class NodeDefaultViewModel : IWorkflowNodeViewModel
{
    [VeloxProperty] private IWorkflowTreeViewModel? parent;
    [VeloxProperty] private Anchor anchor = new();
    [VeloxProperty] private Size size = new();
    [VeloxProperty] private ObservableCollection<IWorkflowSlotViewModel> slots = [];
}
```

For custom nodes, use the `[WorkflowBuilder.Node<THelper>]` attribute (instead of inheritance):
