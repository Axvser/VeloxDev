# Node

> **💡API changes have occurred in V5.7.0, please refer to the 【Version】 section for details.**

> **Build**

```csharp
public class YourNodeHelper() : NodeHelper<NodeViewModel>
{

}
```

> **Method**

| Member                                       | Type                                            | Description               |
| ------------------------------------------ | ----------------------------------------------- | ------------------ |
| `Install(IWorkflowNodeViewModel node)`                                                                                            | `void`                                        | Install to Node      |
| `Uninstall(IWorkflowNodeViewModel node)`                                                                                          | `void`                                        | Uninstall from Node     |
| `CreateSlot(IWorkflowSlotViewModel slot)`                                                                                         | `void`                                        | Create Slot        |
| `Move(Offset offset)`                                                                                                             | `void`                                        | Move node         |
| `SetAnchor(Anchor newValue)`                                                                                                      | `void`                                        | Set anchor         |
| `SetSize(Size newValue)`                                                                                                          | `void`                                        | Set size         |
| `WorkAsync(object? parameter, CancellationToken ct)`                                                                              | `Task`                                        | Execute node work     |
| `BroadcastAsync(object? parameter, CancellationToken ct)`                                                                         | `Task`                                        | Forward broadcast         |
| `ReverseBroadcastAsync(object? parameter, CancellationToken ct)`                                                                  | `Task`                                        | Reverse broadcast         |
| `ValidateBroadcastAsync(IWorkflowSlotViewModel sender, IWorkflowSlotViewModel receiver, object? parameter, CancellationToken ct)` | `Task<bool>`                                  | Validate broadcast legality |
| `Delete()`                                                                                                                        | `void`                                        | Delete node         |

> **event**

| Member                                     | Type                                            | Description          |
| ------------------------------------------ | ----------------------------------------------- | -------------------- |
| `SlotAdded`                                | `event EventHandler<IWorkflowSlotViewModel>?`   | Slot added event     |
| `SlotRemoved`                              | `event EventHandler<IWorkflowSlotViewModel>?`   | Slot removed event   |