# Slot

> **Build**

```cs
[WorkflowBuilder.Slot<SlotHelper>]
public partial class SlotViewModel
{
    // Execute auto-generated methods during construction
    public SlotViewModel() => InitializeWorkflow();

    // ... Freely extend your workflow connector view model
}
```

> **data**

| Member                                               | Type                                             | Description                       |
| -------------------------------------------------- | ------------------------------------------------ | -------------------------------- |
| `Targets`                                        | `ObservableCollection<IWorkflowSlotViewModel>` | The target collection that the current Slot points to   |
| `Sources`                                        | `ObservableCollection<IWorkflowSlotViewModel>` | The source collection received by the current Slot |
| `Parent`                                         | `IWorkflowNodeViewModel?`                      | The owning Node                  |
| `Channel`                                        | `SlotChannel`                                  | Slot channel type              |
| `State`                                          | `SlotState`                                    | Slot connection state              |
| `Anchor`                                         | `Anchor`                                       | Slot anchor position             |

> **command**

| Member                                               | Type                                             | Description                       |
| -------------------------------------------------- | ------------------------------------------------ | -------------------------------- |
| `SetChannelCommand`                              | `IVeloxCommand`                                | Set channel command               |
| `SendConnectionCommand`                          | `IVeloxCommand`                                | Initiate connection command       |
| `ReceiveConnectionCommand`                       | `IVeloxCommand`                                | Receive connection command        |
| `DeleteCommand`                                  | `IVeloxCommand`                                | Delete Slot command               |