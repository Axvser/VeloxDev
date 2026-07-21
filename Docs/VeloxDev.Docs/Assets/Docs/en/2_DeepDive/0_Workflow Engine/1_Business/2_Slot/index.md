# Business Slot

Extend `SlotDefaultViewModel` with `[WorkflowBuilder.Slot<THelper>]` for custom slot behavior.

```csharp
using VeloxDev.WorkflowSystem;

public class CustomSlotHelper : SlotHelper<CustomSlot>
{
    public override void SetChannel(SlotChannel channel)
    {
        base.SetChannel(channel);
        if (Component is not null)
            Component.OnPropertyChanged(nameof(Component.Channel));
    }
}

[WorkflowBuilder.Slot<CustomSlotHelper>]
public partial class CustomSlot
{
    public CustomSlot() => InitializeWorkflow();
}
```
