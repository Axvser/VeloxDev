# Tree Helper

`IWorkflowTreeViewModelHelper` — the behavior delegate interface for Tree components. Default implementation: `TreeHelper<TTree>`.

---

## Interface Methods

| Method | Description |
|--------|-------------|
| `CreateNode(node)` | Add a node to the Nodes collection |
| `Submit(actionPair)` | Execute an action and push onto the undo stack |
| `Undo()` | Pop the undo stack and reverse-execute |
| `Redo()` | Pop the redo stack and execute |
| `SendConnection(slot)` | Set VirtualLink.Sender |
| `ReceiveConnection(slot)` | Set VirtualLink.Receiver |
| `ResetVirtualLink()` | Reset the virtual connection |
| `SetPointer(anchor)` | Set pointer position |
| `Install(component)` | Install the helper (bind commands, events) |
| `Uninstall(component)` | Uninstall the helper |
| `CloseAsync()` | Notify all nodes to close |

## Default Implementation: `TreeHelper<TTree>`

```csharp
public class TreeHelper<TTree> : IWorkflowTreeViewModelHelper where TTree : IWorkflowTreeViewModel
{
	public TTree? Component { get; private set; }
	public virtual void Install(IWorkflowTreeViewModel component) { Component = (TTree)component; }
	public virtual void CreateNode(IWorkflowNodeViewModel node) { Component?.Nodes.Add(node); }
	public virtual void Submit(IWorkflowActionPair actionPair) { /* execute + push undo */ }
	public virtual void Undo() { /* pop undo stack */ }
	public virtual void Redo() { /* pop redo stack */ }
	// ...
}
```

All methods are `virtual`. The `Component` property references the associated Tree instance.

## Custom Example

```csharp
public class AgentHelper : TreeHelper<TreeViewModel>
{
	public override void CreateNode(IWorkflowNodeViewModel node)
	{
		base.CreateNode(node);
		// Custom logic
	}
}

[WorkflowBuilder.Tree<AgentHelper>]
public partial class TreeViewModel
{
	public TreeViewModel() => InitializeWorkflow();
	[VeloxProperty] private bool isWorkflowRunning = false;
}
```
