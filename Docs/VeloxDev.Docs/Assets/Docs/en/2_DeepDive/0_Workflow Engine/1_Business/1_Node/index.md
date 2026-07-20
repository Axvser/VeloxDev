# Business Node

Extend `NodeViewModelBase` with `[WorkflowBuilder.Node<THelper>]` and override helper methods.

```csharp
using VeloxDev.WorkflowSystem;

public class ProcessingHelper : NodeHelper<ProcessingNode>
{
    public override async Task WorkAsync(object? parameter, CancellationToken ct)
    {
        // Transform the incoming data
        var input = parameter?.ToString() ?? "";
        var result = Process(input);
        // Forward result to next node via broadcast
        if (Component is not null)
            await Component.StandardBroadcastAsync(result, ct);
    }

    private string Process(string input) => input.ToUpper();
}

[WorkflowBuilder.Node<ProcessingHelper>]
public partial class ProcessingNode
{
    public ProcessingNode() => InitializeWorkflow();

    [VeloxProperty] private string label = "Processor";
}
```
