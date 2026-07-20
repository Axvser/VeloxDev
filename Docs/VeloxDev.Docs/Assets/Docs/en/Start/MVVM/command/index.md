# Command

> **Build**

The following code will ultimately generate a SaveCommand, regardless of whether it carries the "Async" suffix.

```csharp
[VeloxCommand] private void Save() { }
[VeloxCommand] private void Save(object? p) { }

[VeloxCommand] private Task SaveAsync() { }
[VeloxCommand] private Task SaveAsync(object? p, CancellationToken ct) { }
[VeloxCommand] private Task SaveAsync(object? p) { }
[VeloxCommand] private Task SaveAsync(CancellationToken ct) { }
```

[VeloxCommand] provides optional parameters

| Parameter Name | Type | Default | Description |
| --- | --- | --- | --- |
| name | string | Auto | Command name |  |
| canValidate | bool | false | Enable executable validation |  |
| semaphore | int | 1 | Maximum concurrent tasks |  |

> **Execute and Cancel**

No matter how you execute commands, the essence is waiting for a Task, so in most cases you don't need to worry about calling Execute instead of ExecuteAsync blocking the UI; they are almost equivalent.

```csharp
SaveCommand.Execute(null);

await SaveCommand.ExecuteAsync(null);
```

The cancellation of the command also provides two versions; here, the awaitable version is demonstrated.

```csharp
await SaveCommand.LockAsync(); // Lock, preventing new tasks from executing except the current one

// await SaveCommand.InterruptAsync(); Interrupt the currently executing task (excluding queued tasks)
await SaveCommand.ClearAsync(); // Interrupt all tasks

await SaveCommand.UnLockAsync();
```

> **Enable verification**

When you set canValidate: true, the CanExecuteSaveCommand callback will be generated internally. You can determine whether to allow command execution within this callback.

```csharp
[VeloxCommand(canValidate: true)]
private void Save()
{

}

private partial bool CanExecuteSaveCommand(object? parameter)
{
    
}
```

You can manually notify the UI to re-evaluate the executability of the command, at which point parameter is null. If the command is executed, parameter is the parameter received by the command.

```csharp
SaveCommand.Notify();
```

> **Callback Event**

You can monitor the total number of tasks currently executing, handle exceptions, and perform other operations. These callback events all carry the `CommandEventArgs` parameter.

| Hook | Description |
| --- | --- |
| Created | Task created |
| Enqueued | Task enqueued |
| Dequeued | Task dequeued |
| Started | Task started |
| Completed | Task completed |
| Failed | Task failed |
| Canceled | Task canceled |
| Exited | Task exited |