# Business

Build domain-specific workflow components by creating custom subclasses and attaching Helpers via `[WorkflowBuilder]` attributes.

## Custom Node Helper

Override the virtual methods in `NodeHelper<T>` to inject business logic:

```csharp
using VeloxDev.WorkflowSystem;

// 1. Create a custom helper inheriting from NodeHelper<T>
public class MyNodeHelper : NodeHelper<MyNodeViewModel>
{
    // Called when this node receives data during execution
    public override Task WorkAsync(object? parameter, CancellationToken ct)
    {
        // Business logic here
        Console.WriteLine($"Processing: {parameter}");
        return Task.CompletedTask;
    }
}

// 2. Decorate the ViewModel with the helper type
[WorkflowBuilder.Node<MyNodeHelper>]
public partial class MyNodeViewModel
{
    public MyNodeViewModel() => InitializeWorkflow();

    [VeloxProperty] private string inputValue = "";
}
```

## Available `[WorkflowBuilder]` Attributes

| Attribute | Target | Helper Interface |
|-----------|--------|------------------|
| `[WorkflowBuilder.Tree<THelper>]` | Tree | `IWorkflowTreeViewModelHelper` |
| `[WorkflowBuilder.Node<THelper>]` | Node | `IWorkflowNodeViewModelHelper` |
| `[WorkflowBuilder.Slot<THelper>]` | Slot | `IWorkflowSlotViewModelHelper` |
| `[WorkflowBuilder.Link<THelper>]` | Link | `IWorkflowLinkViewModelHelper` |

The source generator reads the helper type and generates all required infrastructure (commands, wiring).
