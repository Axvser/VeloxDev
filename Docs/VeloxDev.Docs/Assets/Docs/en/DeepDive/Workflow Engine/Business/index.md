# Business

Build domain-specific workflow nodes by creating custom Node subclasses and Helpers.

```csharp
public class MyBusinessHelper : WorkflowHelper<MyBusinessNode>
{
    public override async Task ExecuteAsync(
        MyBusinessNode node, NetworkFlowContext context)
    {
        // Business logic here
        context.SetResult(Calculate(node.InputValue));
    }
}

[WorkflowBuilder.MyNode<MyBusinessHelper>]
public partial class MyBusinessNode { }
```

The `[WorkflowBuilder]` source generator reads the helper type and generates the ViewModel with all required infrastructure.
