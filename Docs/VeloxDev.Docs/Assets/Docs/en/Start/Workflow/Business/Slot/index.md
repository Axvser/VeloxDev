# Slot

> **build**

```csharp
public class YourSlotHelper() : SlotHelper<SlotViewModel>
{

}
```

> **method**

| Member                                      | Type                                            | Description            |
| ------------------------------------------- | ----------------------------------------------- | ---------------------- |
| `Install(IWorkflowSlotViewModel slot)`    | `void`                                        | Install to Slot        |
| `Uninstall(IWorkflowSlotViewModel slot)`  | `void`                                        | Uninstall from Slot    |
| `SetChannel(SlotChannel channel)`         | `void`                                        | Set channel            |
| `UpdateState()`                           | `void`                                        | Update state           |
| `SendConnection()`                        | `void`                                        | Initiate connection    |
| `ReceiveConnection()`                     | `void`                                        | Accept connection      |
| `Delete()`                                | `void`                                        | Delete Slot            |

> **Event**

| Member                                       | Type                                            | Description               |
| ------------------------------------------ | ----------------------------------------------- | ------------------------- |
| `TargetAdded`                            | `event EventHandler<IWorkflowSlotViewModel>?` | Target Slot added event   |
| `TargetRemoved`                          | `event EventHandler<IWorkflowSlotViewModel>?` | Target Slot removed event |
| `SourceAdded`                            | `event EventHandler<IWorkflowSlotViewModel>?` | Source Slot added event   |
| `SourceRemoved`                          | `event EventHandler<IWorkflowSlotViewModel>?` | Source Slot removed event |