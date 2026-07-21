# Link Helper

`IWorkflowLinkViewModelHelper` — the behavior delegate interface for Link components. Default implementation: `LinkHelper`.

---

## Interface Methods

| Method | When Called | Description |
|--------|-------------|-------------|
| `Install(component)` | Constructor | Bind commands |
| `Uninstall(component)` | Helper switch | Unbind |
| `Delete()` | `DeleteCommand` | Delete the connection |
| `CloseAsync()` | Workflow stops | Cleanup |

## Default Implementation: `LinkHelper`

```csharp
public class LinkHelper : IWorkflowLinkViewModelHelper
{
	protected IWorkflowLinkViewModel? Component { get; private set; }
	public virtual void Install(IWorkflowLinkViewModel component) { Component = component; }
	public virtual void Uninstall(IWorkflowLinkViewModel component) { Component = default; }
	public virtual void Delete() { /* remove from Links collection */ }
	public virtual async Task CloseAsync() { /* cleanup */ }
}
```

## Custom Example

```csharp
public class CustomLinkHelper : LinkHelper<CustomLink>
{
	public override void Delete()
	{
		// Custom cleanup before deletion
		base.Delete();
	}
}

[WorkflowBuilder.Link<CustomLinkHelper>]
public partial class CustomLink
{
	public CustomLink() => InitializeWorkflow();
	[VeloxProperty] private bool usePolyline = true;
}
```

The generic `LinkHelper<TLink>` provides a typed `Component` property.
