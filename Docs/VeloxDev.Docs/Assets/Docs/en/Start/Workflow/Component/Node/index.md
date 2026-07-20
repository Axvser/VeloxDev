# Node

> **Build**

```csharp
[WorkflowBuilder.Node<NodeHelper>]
public partial class NodeViewModel
{
    // Methods automatically generated during construction phase
    public NodeViewModel() => InitializeWorkflow();

    // ... Freely extend your workflow node view model
}
```

> **Special member**

In the definition phase of the node view model, there are two special members:
- `IWorkflowSlotViewModel` derived classes must be marked with `[VeloxProperty]`.
- `SlotEnumerator<T>` can use `bool` or `enum` as conditional branches; multiple slots are generated inside the collection based on the conditions, and it must also be marked with `[VeloxProperty]`.

Thus, later in the view you can directly render multiple connectors from the `InputSlot` property and the `OutputSlots` collection. Note that the UI is responsible for tracking the coordinates (Anchor) of the slots and passing them back to the view model. If the UI lacks this coordinate tracking, the start and end positions of your `Link` component will not be calculated correctly.

```csharp
[VeloxProperty] public partial SlotViewModel InputSlot { get; set; }

[VeloxProperty] public partial SlotEnumerator<SlotViewModel> OutputSlots { get; set; }

public NodeViewModel()
{
    InitializeWorkflow();

    OutputSlots.SetSelector(typeof(bool)); // Set condition branches for SlotEnumerator, using bool as an example, it will internally generate two Slot components named True and False.
}
```

SlotEnumerator<T> implements the IEnumerable interface, with sub-elements defined as follows: Name is the name of the enum member, Value is the enum value, Slot is the automatically constructed Slot component.

```csharp
public partial class ConditionalSlot<TSlot>
    where TSlot : IWorkflowSlotViewModel, new()
{
    [VeloxProperty] private string _name = string.Empty;
    [VeloxProperty] private object? _value;
    [VeloxProperty] private TSlot _slot = new();
}
```

> **data**

| Member                                             | Type                                             | Description               |
| -------------------------------------------------- | ------------------------------------------------ | ------------------------- |
| `Parent`                                         | `IWorkflowTreeViewModel?`                      | Parent tree               |
| `Anchor`                                         | `Anchor`                                       | Node anchor coordinates   |
| `Size`                                           | `Size`                                         | Node size                 |
| `Slots`                                          | `ObservableCollection<IWorkflowSlotViewModel>` | Node's slot collection    |

> **command**

| Member                                               | Type                                             | Description                 |
| ---------------------------------------------------- | ------------------------------------------------ | --------------------------- |
| `MoveCommand`                                      | `IVeloxCommand`                                | Move node command           |
| `SetAnchorCommand`                                 | `IVeloxCommand`                                | Set anchor command          |
| `SetSizeCommand`                                   | `IVeloxCommand`                                | Set size command            |
| `CreateSlotCommand`                                | `IVeloxCommand`                                | Create Slot command         |
| `DeleteCommand`                                    | `IVeloxCommand`                                | Delete node command         |
| `WorkCommand`                                      | `IVeloxCommand`                                | Execute work command        |
| `BroadcastCommand`                                 | `IVeloxCommand`                                | Forward broadcast command   |
| `ReverseBroadcastCommand`                          | `IVeloxCommand`                                | Reverse broadcast command   |