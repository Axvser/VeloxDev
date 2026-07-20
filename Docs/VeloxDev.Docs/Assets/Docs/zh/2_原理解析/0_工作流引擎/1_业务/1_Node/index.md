# Node

通过 `[WorkflowBuilder.Node<THelper>]` 和自定义 Helper 注入业务逻辑。

```csharp
using VeloxDev.WorkflowSystem;

public class ProcessingHelper : NodeHelper<ProcessingNode>
{
    public override async Task WorkAsync(object? parameter, CancellationToken ct)
    {
        var input = parameter?.ToString() ?? "";
        var result = input.ToUpper();
        // 将结果广播到下游节点
        if (Component is not null)
            await Component.StandardBroadcastAsync(result, ct);
    }
}

[WorkflowBuilder.Node<ProcessingHelper>]
public partial class ProcessingNode
{
    public ProcessingNode() => InitializeWorkflow();

    [VeloxProperty] private string label = "处理器";
}
```

关键可重写方法：`WorkAsync`、`ReceiveAsync`、`BroadcastAsync`、`Install`、`Uninstall`。
