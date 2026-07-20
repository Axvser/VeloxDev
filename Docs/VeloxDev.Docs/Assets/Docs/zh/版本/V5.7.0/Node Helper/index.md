# Node Helper

> **新机制**

WorkAsync 不支持返回值，也不携带传播事件的发起者和接收者，因此更适合独立任务。因此，新版本引入了一个全新的 ReceiveAsync 入口点，弥补了所有这些问题。当您使用 BroadcastAsync 时，这个新入口点将响应。

```csharp
public virtual Task WorkAsync(
        object? parameter,
        CancellationToken ct)
        => Task.CompletedTask;

    public virtual Task<object?> ReceiveAsync(
        object? parameter,
        IWorkflowSlotViewModel sender,
        IWorkflowSlotViewModel receiver,
        CancellationToken ct)
        => Task.FromResult<object?>(null);
```

> **底层**

BroadcastAsync 内部将元数据封装为 WorkContext 实例，而视图模型中的 WorkCommand 已经实现了解析和服务入口重定向。

```csharp
[VeloxCommand]
private async Task Work(object? parameter, CancellationToken ct)
{
    if (parameter is WorkContext ctx && ctx.Sender is not null && ctx.Receiver is not null)
    {
        WorkResult = await Helper.ReceiveAsync(ctx.Parameter, ctx.Sender, ctx.Receiver, ct);
    }
    else
    {
        await Helper.WorkAsync(parameter, ct);
        WorkResult = null;
    }
}
```