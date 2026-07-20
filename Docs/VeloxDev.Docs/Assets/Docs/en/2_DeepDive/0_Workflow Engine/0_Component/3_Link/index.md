# Link

A `WorkflowLinkViewModel` represents a visual connection line.

```csharp
public partial class WorkflowLinkViewModel
{
    public static WorkflowLinkViewModel Connect(
        SlotViewModel sender, SlotViewModel receiver);
}
```

Links are rendered as Bezier or polyline curves by the platform-specific view layer.
