# Node Helper

`IWorkflowNodeViewModelHelper` — the behavior delegate interface for Node components. Default implementation: `NodeHelper<TNode>`. This is the most commonly customized helper.

---

## Interface Methods

| Method | When Called | Description |
|--------|-------------|-------------|
| `Install(component)` | Constructor | Bind all standard commands |
| `Uninstall(component)` | Helper switch | Unbind commands |
| `Move(offset)` | `MoveCommand` | Update Anchor |
| `SetAnchor(anchor)` | `SetAnchorCommand` | Set Anchor |
| `SetSize(size)` | `SetSizeCommand` | Set Size |
| `CreateSlot(slot)` | `CreateSlotCommand` | Register a new Slot |
| `Delete()` | `DeleteCommand` | Remove self and cleanup |
| `WorkAsync(parameter, ct)` | `WorkCommand` | **Business entry point** (default: no-op) |
| `ReceiveAsync(parameter, sender, receiver, ct)` | Data received from upstream | Default: calls `WorkAsync` |
| `BroadcastAsync(parameter, ct)` | `BroadcastCommand` | Forward to all output Slots |
| `ReverseBroadcastAsync(parameter, ct)` | `ReverseBroadcastCommand` | Broadcast upstream |
| `CloseAsync()` | Workflow stops | Cleanup resources |

## Default Implementation: `NodeHelper<TNode>`

```csharp
public class NodeHelper<TNode> : IWorkflowNodeViewModelHelper where TNode : IWorkflowNodeViewModel
{
	protected TNode? Component { get; private set; }
	public virtual void Install(IWorkflowNodeViewModel component) { Component = (TNode)component; }
	public virtual void Uninstall(IWorkflowNodeViewModel component) { Component = default; }
	public virtual void Move(Offset offset) { /* update Anchor */ }
	public virtual void SetAnchor(Anchor anchor) { /* set */ }
	public virtual void SetSize(Size size) { /* set */ }
	public virtual void CreateSlot(IWorkflowSlotViewModel slot) { /* register */ }
	public virtual void Delete() { Component?.GetHelper().CloseAsync(); }
	public virtual Task WorkAsync(object? parameter, CancellationToken ct) => Task.CompletedTask;
	public virtual async Task ReceiveAsync(object? parameter, IWorkflowSlotViewModel sender, IWorkflowSlotViewModel receiver, CancellationToken ct)
		=> await WorkAsync(parameter, ct);
	public virtual async Task BroadcastAsync(object? parameter, CancellationToken ct) { /* forward */ }
	public virtual async Task CloseAsync() { /* cleanup */ }
}
```

## Custom Example

```csharp
public class ProcessingHelper : NodeHelper<ProcessingNode>
{
	public override async Task WorkAsync(object? parameter, CancellationToken ct)
	{
		var input = parameter?.ToString() ?? "";
		var result = input.ToUpper();
		if (Component is not null)
			await Component.BroadcastCommand.ExecuteAsync(result, ct);
	}
}

[WorkflowBuilder.Node<ProcessingHelper>]
public partial class ProcessingNode
{
	public ProcessingNode() => InitializeWorkflow();
	[VeloxProperty] private string label = "Processor";
}
```

The `workSemaphore` parameter controls concurrent execution count for WorkAsync (default 1). Full examples at [Examples/Workflow/Common/Lib/ViewModels/Workflow/Helper/](https://github.com/Axvser/VeloxDev/tree/master/Examples/Workflow/Common/Lib/ViewModels/Workflow/Helper)
