# Link

A `LinkViewModelBase` represents a visual connection line between two Slots.

```csharp
using VeloxDev.MVVM;
using VeloxDev.WorkflowSystem;

public sealed partial class LinkViewModelBase : IWorkflowLinkViewModel
{
    [VeloxProperty] private IWorkflowSlotViewModel sender = new SlotViewModelBase();
    [VeloxProperty] private IWorkflowSlotViewModel receiver = new SlotViewModelBase();
    [VeloxProperty] private bool isVisible = false;
}
```

Links are created by setting `Sender` and `Receiver`, then adding to the Tree's `Links` collection. The platform-specific view layer renders them as Bezier or polyline curves.
