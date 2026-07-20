# Node

The `WorkflowNodeViewModel` is the base class for all executable nodes.

```csharp
public partial class WorkflowNodeViewModel : IWorkflowNodeViewModel
{
    [VeloxProperty] public Size Size { get; set; }
    [VeloxProperty] public ObservableCollection<SlotViewModel> Slots { get; set; }
    // Slot[0] = input, Slot[1] = output by convention
}
```

Custom business logic is injected via a `Helper<T>` generic parameter. The helper is generated into the ViewModel at build time.
