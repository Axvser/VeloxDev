# Node Helper

> **New Mechanism**

WorkAsync does not support return values and does not carry the initiator and receiver of propagated events, so it is more suitable for independent tasks. Therefore, the new version introduces a new ReceiveAsync entry point to address all these issues. When you use BroadcastAsync, this new entry point will respond.

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

> **underlying**

Inside BroadcastAsync, metadata is encapsulated as a WorkContext instance, while WorkCommand in the view model has already implemented parsing and service entry redirection.

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