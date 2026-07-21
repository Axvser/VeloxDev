# Slot

A `SlotDefaultViewModel` is a typed connection point on a Node.

```csharp
using VeloxDev.MVVM;
using VeloxDev.WorkflowSystem;

public sealed partial class SlotDefaultViewModel : IWorkflowSlotViewModel
{
    [VeloxProperty] private ObservableCollection<IWorkflowSlotViewModel> targets = [];
    [VeloxProperty] private ObservableCollection<IWorkflowSlotViewModel> sources = [];
    [VeloxProperty] private SlotChannel channel = SlotChannel.OneBoth;
    [VeloxProperty] private SlotState state = SlotState.StandBy;
    [VeloxProperty] private Anchor anchor = new();
    [VeloxProperty] private IWorkflowNodeViewModel? parent = null;
}
```

Key properties:

| Property | Type | Description |
|----------|------|-------------|
| `Targets` | `ObservableCollection<IWorkflowSlotViewModel>` | Slots this slot connects **to** |
| `Sources` | `ObservableCollection<IWorkflowSlotViewModel>` | Slots connecting **to** this slot |
| `Channel` | `SlotChannel` | Direction constraint (`Input`, `Output`, `OneBoth`, `None`) |
| `State` | `SlotState` | Connection state (`StandBy`, `Connected`, `Linking`, `Error`) |
| `Anchor` | `Anchor` | Spatial position on the canvas |
| `Parent` | `IWorkflowNodeViewModel?` | The containing node |
