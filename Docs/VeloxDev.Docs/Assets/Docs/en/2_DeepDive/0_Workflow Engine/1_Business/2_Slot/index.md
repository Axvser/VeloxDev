# Slot Helper

`IWorkflowSlotViewModelHelper` — the behavior delegate interface for Slot components. Default implementation: `SlotHelper`.

---

## Interface Methods

| Method | When Called | Description |
|--------|-------------|-------------|
| `Install(component)` | Constructor | Bind commands |
| `Uninstall(component)` | Helper switch | Unbind |
| `SetChannel(channel)` | `SetChannelCommand` | Set channel type |
| `SendConnection()` | `SendConnectionCommand` | Initiate a connection |
| `ReceiveConnection()` | `ReceiveConnectionCommand` | Accept a connection |
| `Delete()` | `DeleteCommand` | Delete the Slot |
| `CloseAsync()` | Workflow stops | Cleanup |

## Default Implementation: `SlotHelper`

```csharp
public class SlotHelper : IWorkflowSlotViewModelHelper
{
	protected IWorkflowSlotViewModel? Component { get; private set; }
	public virtual void Install(IWorkflowSlotViewModel component) { Component = component; }
	public virtual void SetChannel(SlotChannel channel) { /* set */ }
	public virtual void SendConnection() { /* via Tree Helper */ }
	public virtual void ReceiveConnection() { /* via Tree Helper */ }
	public virtual void Delete() { /* remove */ }
	public virtual async Task CloseAsync() { /* cleanup */ }
}
```

## Custom Example

```csharp
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

The non-generic `SlotHelper` is for simple scenarios; the generic `SlotHelper<TSlot>` provides a typed `Component` property.
