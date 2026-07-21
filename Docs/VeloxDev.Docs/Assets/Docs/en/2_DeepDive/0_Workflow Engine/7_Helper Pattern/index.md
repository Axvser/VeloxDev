# Helper Pattern Deep Dive

The four workflow components (Tree / Node / Slot / Link) delegate their behavior to **Helper** objects. Helpers are injected by the source generator, implementing a full Strategy pattern.

---

## Mechanism

```csharp
// User code
[WorkflowBuilder.Node<HttpHelper<NodeViewModel>>(workSemaphore: 5)]
public partial class NodeViewModel { ... }

// Generator output (.g.cs)
public partial class NodeViewModel : IWorkflowNodeViewModel
{
	private IWorkflowNodeViewModelHelper helper = new HttpHelper<NodeViewModel>();
	public void InitializeWorkflow() { helper.Install(this); }
}
```

## Helper Types

| Component | Helper Interface | Default | Generic |
|-----------|-----------------|---------|---------|
| Node | `IWorkflowNodeViewModelHelper` | `NodeHelper<TNode>` | TNode: node type |
| Tree | `IWorkflowTreeViewModelHelper` | `TreeHelper<TTree>` | TTree: tree type |
| Slot | `IWorkflowSlotViewModelHelper` | `SlotHelper` | — |
| Link | `IWorkflowLinkViewModelHelper` | `LinkHelper` | — |

## Custom Helper

```csharp
public class HttpHelper<TNode> : NodeHelper<TNode> where TNode : IWorkflowNodeViewModel
{
	public override async Task WorkAsync(object? parameter, CancellationToken ct)
	{
		var url = GetUrlFromProperty();
		var httpClient = new HttpClient();
		var response = await httpClient.GetAsync(url, ct);
	}
}

[WorkflowBuilder.Node<HttpHelper<NodeViewModel>>(workSemaphore: 5)]
public partial class NodeViewModel { ... }
```

## Overridable Methods (NodeHelper)

| Method | When Called | Default |
|--------|-------------|---------|
| `Install(component)` | Constructor (`InitializeWorkflow()`) | Binds all standard commands |
| `WorkAsync(parameter, ct)` | `WorkCommand` executed | Empty (business logic entry point) |
| `ReceiveAsync(parameter, sender, receiver, ct)` | Data received from upstream | Calls `WorkAsync` |
| `BroadcastAsync(parameter, ct)` | `BroadcastCommand` triggered | Forwards to all output slots |
| `CloseAsync()` | Workflow stops | Cleans resources |

Full examples: [Examples/Workflow/Common/Lib/ViewModels/Workflow/Helper/](https://github.com/Axvser/VeloxDev/tree/master/Examples/Workflow/Common/Lib/ViewModels/Workflow/Helper)
