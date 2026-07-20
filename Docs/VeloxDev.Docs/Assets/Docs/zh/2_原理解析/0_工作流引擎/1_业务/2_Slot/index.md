# Slot

通过 `[WorkflowBuilder.Slot<THelper>]` 扩展 `SlotViewModelBase`。

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
