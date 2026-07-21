# Business (Helper Pattern)

The four workflow components delegate their behavior to **Helper** objects. Helpers are injected by the source generator, implementing the **Strategy pattern** — users inherit default helpers and override methods to inject business logic.

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

## [WorkflowBuilder] Attribute Reference

| Attribute | Target | Helper Interface | Default Helper |
|-----------|--------|------------------|----------------|
| `[WorkflowBuilder.Tree<THelper>]` | Tree | `IWorkflowTreeViewModelHelper` | `TreeHelper<TTree>` |
| `[WorkflowBuilder.Node<THelper>]` | Node | `IWorkflowNodeViewModelHelper` | `NodeHelper<TNode>` |
| `[WorkflowBuilder.Slot<THelper>]` | Slot | `IWorkflowSlotViewModelHelper` | `SlotHelper` |
| `[WorkflowBuilder.Link<THelper>]` | Link | `IWorkflowLinkViewModelHelper` | `LinkHelper` |

## Standard Extension Points

`VeloxDev.WorkflowSystem.StandardEx` provides standard extension methods for use in helpers:

| Method | Purpose |
|--------|---------|
| `GetStandardCommands()` | Get all standard commands for a component |
| `StandardCreateSlot(slot)` | Create a slot with undo support |
| `StandardClosing/StandardClosingAsync()` | Lock all commands |
| `StandardClose/StandardCloseAsync()` | Clear all commands |

## Custom Helper Steps

```csharp
// 1. Inherit the default helper
public class HttpHelper<TNode> : NodeHelper<TNode> where TNode : IWorkflowNodeViewModel
{
    public override async Task WorkAsync(object? parameter, CancellationToken ct)
    {
        // Business logic entry point
    }
}

// 2. Specify via [WorkflowBuilder.*]
[WorkflowBuilder.Node<HttpHelper<NodeViewModel>>(workSemaphore: 5)]
public partial class NodeViewModel { ... }
```

> Without `[WorkflowBuilder.*]`, the class will not receive the workflow interface or any commands. This attribute is mandatory.

See sub-pages for each component's helper overridable methods.
