# Node Helper

`IWorkflowNodeViewModelHelper` — Node 组件的行为委托接口，默认实现为 `NodeHelper<TNode>`。这是最常自定义的 Helper。

---

## Helper 接口：`IWorkflowNodeViewModelHelper`

| 方法 | 调用时机 | 说明 |
|------|----------|------|
| `Install(component)` | 构造函数 | 绑定所有标准命令 |
| `Uninstall(component)` | Helper 切换 | 解绑命令 |
| `Move(offset)` | `MoveCommand` | 更新 Anchor |
| `SetAnchor(anchor)` | `SetAnchorCommand` | 设置 Anchor |
| `SetSize(size)` | `SetSizeCommand` | 设置 Size |
| `CreateSlot(slot)` | `CreateSlotCommand` | 注册新 Slot |
| `Delete()` | `DeleteCommand` | 移除自身并清理 |
| `WorkAsync(parameter, ct)` | `WorkCommand` | **业务入口**（默认空） |
| `ReceiveAsync(parameter, sender, receiver, ct)` | 收到上游数据 | 默认调用 `WorkAsync` |
| `BroadcastAsync(parameter, ct)` | `BroadcastCommand` | 转发到所有输出 Slot |
| `ReverseBroadcastAsync(parameter, ct)` | `ReverseBroadcastCommand` | 向输入方向广播 |
| `CloseAsync()` | 工作流停止 | 清理资源 |

## 默认实现：`NodeHelper<TNode>`

```csharp
public class NodeHelper<TNode> : IWorkflowNodeViewModelHelper where TNode : IWorkflowNodeViewModel
{
    protected TNode? Component { get; private set; }

    public virtual void Install(IWorkflowNodeViewModel component) { Component = (TNode)component; }
    public virtual void Uninstall(IWorkflowNodeViewModel component) { Component = default; }
    public virtual void Move(Offset offset) { /* 更新 Anchor */ }
    public virtual void SetAnchor(Anchor anchor) { /* 设置 */ }
    public virtual void SetSize(Size size) { /* 设置 */ }
    public virtual void CreateSlot(IWorkflowSlotViewModel slot) { /* 注册 Slot */ }
    public virtual void Delete() { Component?.GetHelper().CloseAsync(); }

    public virtual Task WorkAsync(object? parameter, CancellationToken ct) => Task.CompletedTask;
    public virtual async Task ReceiveAsync(object? parameter, IWorkflowSlotViewModel sender, IWorkflowSlotViewModel receiver, CancellationToken ct)
        => await WorkAsync(parameter, ct);
    public virtual async Task BroadcastAsync(object? parameter, CancellationToken ct) { /* 转发 */ }
    public virtual async Task ReverseBroadcastAsync(object? parameter, CancellationToken ct) { /* 反向 */ }
    public virtual async Task CloseAsync() { /* 清理 */ }
}
```

## 自定义示例

```csharp
public class ProcessingHelper : NodeHelper<ProcessingNode>
{
    public override async Task WorkAsync(object? parameter, CancellationToken ct)
    {
        var input = parameter?.ToString() ?? "";
        var result = input.ToUpper();
        // 将结果广播到下游节点
        if (Component is not null)
            await Component.BroadcastCommand.ExecuteAsync(result, ct);
    }
}

[WorkflowBuilder.Node<ProcessingHelper>]
public partial class ProcessingNode
{
    public ProcessingNode() => InitializeWorkflow();
    [VeloxProperty] private string label = "处理器";
}
```

`workSemaphore` 参数控制 WorkAsync 的并发执行数（默认 1）。完整示例见 [Examples/Workflow/Common/Lib/ViewModels/Workflow/Helper/](https://github.com/Axvser/VeloxDev/tree/master/Examples/Workflow/Common/Lib/ViewModels/Workflow/Helper)
