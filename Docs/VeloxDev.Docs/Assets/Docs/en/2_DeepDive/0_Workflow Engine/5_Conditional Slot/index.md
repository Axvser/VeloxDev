# Conditional Slot & Routing

The workflow engine supports **dynamic connection endpoints** — slots that are resolved at runtime based on data or conditions.

---

## Core Types

### `SlotEnumerator<TSlot>`

Default implementation of `IConditionalSlotProvider<TSlot>`. Manages a dynamic collection of `ConditionalSlot<TSlot>`.

```csharp
public partial class SlotEnumerator<TSlot> : IConditionalSlotProvider<TSlot>
	where TSlot : IWorkflowSlotViewModel, new()
{
	[VeloxProperty] private Dictionary<object, TSlot> conditionMap = [];
	[VeloxProperty] public partial ObservableCollection<ConditionalSlot<TSlot>> Items { get; set; }
}
```

### `ConditionalSlot<TSlot>`

Wraps a condition value with its backend Slot.

```csharp
public partial class ConditionalSlot<TSlot> where TSlot : IWorkflowSlotViewModel, new()
{
	[VeloxProperty] private string _name = string.Empty;   // condition name
	[VeloxProperty] private object? _value;                 // routing key
	[VeloxProperty] private TSlot _slot = new();            // backend slot
}
```

### `SlotDefinition`

Output entry produced by a custom `ISlotProvider`.

```csharp
public sealed class SlotDefinition(object value, string label = "")
{
	public object Value { get; }       // routing key
	public string Label { get; }      // display label
}
```

## Usage

```csharp
[WorkflowBuilder.Node<BoolSelectorHelper>]
public partial class BoolSelectorNode
{
	public BoolSelectorNode() => InitializeWorkflow();

	[VeloxProperty] public partial SlotEnumerator<SlotViewModel> Selector { get; set; }
}
```

The generator auto-detects `SlotEnumerator<TSlot>` properties and:
1. Creates the enumerator instance in `InitializeWorkflow()`
2. Calls `enumerator.Install(this, "PropertyName")` to bind to the parent node
3. Registers all `ConditionalSlot<TSlot>.Slot` instances in the node's `Slots` collection

## Interface

```csharp
public interface IConditionalSlotProvider<TSlot> where TSlot : IWorkflowSlotViewModel
{
	bool TrySelect(object? condition, [MaybeNullWhen(false)] out TSlot slot);
}
```

Full demo: [Examples/Workflow/Common/Lib/ViewModels/Workflow/BoolSelectorNodeViewModel.cs](https://github.com/Axvser/VeloxDev/tree/master/Examples/Workflow/Common/Lib/ViewModels/Workflow/BoolSelectorNodeViewModel.cs)
