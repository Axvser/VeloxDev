# Slot

A `SlotViewModel` is a typed connection point on a Node.

```csharp
public partial class SlotViewModel
{
    [VeloxProperty] public string Title { get; set; }
    [VeloxProperty] public SlotType SlotType { get; set; } // Input / Output
}
```

Slots enforce direction: an Input slot can only connect to an Output slot. Custom slot providers (`ISlotProvider`) can expose multiple connectors dynamically.
