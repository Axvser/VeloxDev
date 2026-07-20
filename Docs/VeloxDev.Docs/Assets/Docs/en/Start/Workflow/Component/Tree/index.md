# Tree

> **Build**

```csharp
[WorkflowBuilder.Tree<TreeHelper>]
public partial class TreeViewModel
{
    // Execute auto-generated methods during construction phase
    public TreeViewModel() => InitializeWorkflow();

    // …… Freely extend your workflow tree view model
}
```

> **Data**

| Member                                             | Type                                                                                              | Description                  |
| -------------------------------------------------- | ------------------------------------------------------------------------------------------------- | ---------------------------- |
| `VirtualLink`                                    | `IWorkflowLinkViewModel`                                                                         | Virtual link during connection establishment |
| `Nodes`                                          | `ObservableCollection<IWorkflowNodeViewModel>`                                                   | All Node components          |
| `Links`                                          | `ObservableCollection<IWorkflowLinkViewModel>`                                                   | All Link components          |
| `Layout`                                          | 'CanvasLayout'                                                                                   | Canvas layout                |

> **command**

| Member               | Type                                                                                               | Description                     |
| -------------------- | -------------------------------------------------------------------------------------------------- | ------------------------------- |
| `CreateNodeCommand`  | `IVeloxCommand`                                                                                  | Create node command             |
| `SetPointerCommand`  | `IVeloxCommand`                                                                                  | Update pointer position command |
| `ResetVirtualLinkCommand` | `IVeloxCommand`                                                                                  | Reset virtual link command      |
| `RedoCommand`        | `IVeloxCommand`                                                                                  | Redo command                    |
| `UndoCommand`        | `IVeloxCommand`                                                                                  | Undo command                    |