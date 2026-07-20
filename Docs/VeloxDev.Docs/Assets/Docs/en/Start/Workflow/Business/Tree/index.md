# Tree

> **Build**

```csharp
public class YourTreeHelper() : TreeHelper<TreeViewModel>(200) // 200 indicates the size of a typical node, used to activate built-in virtualization support
{

}
```

> **data**

| Member                                                                               | Type                                            | Description                  |
| -------------------------------------------------------------------------------------- | ----------------------------------------------- | ---------------------------- |
| `VisibleItems`                                                                       | `ObservableCollection<IWorkflowViewModel>`    | Current visible items collection |
| `Viewport`                                                                           | `Viewport`                                    | Current viewport             |

> **Method**

| Member                                                                                   | Type                                            | Description             |
| -------------------------------------------------------------------------------------- | ----------------------------------------------- | ----------------------- |
| `Install(IWorkflowTreeViewModel tree)`                                               | `void`                                        | Install to Tree         |
| `Uninstall(IWorkflowTreeViewModel tree)`                                             | `void`                                        | Uninstall from Tree     |
| `CreateNode(IWorkflowNodeViewModel node)`                                            | `void`                                        | Create node             |
| `CreateLink(IWorkflowSlotViewModel sender, IWorkflowSlotViewModel receiver)`         | `IWorkflowLinkViewModel`                      | Create link             |
| `SetPointer(Anchor anchor)`                                                          | `void`                                        | Set pointer position    |
| `ValidateConnection(IWorkflowSlotViewModel sender, IWorkflowSlotViewModel receiver)` | `bool`                                        | Validate connection     |
| `SendConnection(IWorkflowSlotViewModel slot)`                                        | `void`                                        | Initiate connection     |
| `ReceiveConnection(IWorkflowSlotViewModel slot)`                                     | `void`                                        | Receive connection      |
| `ResetVirtualLink()`                                                                 | `void`                                        | Reset virtual link      |
| `Virtualize(Viewport viewport)`                                                      | `void`                                        | Trigger viewport virtualization |
| `Submit(IWorkflowActionPair actionPair)`                                             | `void`                                        | Submit action           |
| `Redo()`                                                                             | `void`                                        | Redo                    |
| `Undo()`                                                                             | `void`                                        | Undo                    |
| `ClearHistory()`                                                                     | `void`                                        | Clear history           |
| `MarkDirty()`                                                                        | `void`                                        | Mark dirty state        |

> **Event**

| Member                                                                               | Type                                            | Description             |
| -------------------------------------------------------------------------------------- | ----------------------------------------------- | ----------------------- |
| `NodeAdded`                                                                          | `event EventHandler<IWorkflowNodeViewModel>?` | Node added event        |
| `NodeRemoved`                                                                        | `event EventHandler<IWorkflowNodeViewModel>?` | Node removed event      |
| `LinkAdded`                                                                          | `event EventHandler<IWorkflowLinkViewModel>?` | Link added event        |
| `LinkRemoved`                                                                        | `event EventHandler<IWorkflowLinkViewModel>?` | Link removed event      |
| `VisibleItemAdded`                                                                   | `event EventHandler<IWorkflowViewModel>?`     | Visible item added event   |
| `VisibleItemRemoved`                                                                 | `event EventHandler<IWorkflowViewModel>?`     | Visible item removed event |