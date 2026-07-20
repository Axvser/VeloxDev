# 命令

> **构建**

下述代码最终都将生成 SaveCommand，无论是否持有 ”Async“ 后缀

```csharp
[VeloxCommand] private void Save() { }
[VeloxCommand] private void Save(object? p) { }

[VeloxCommand] private Task SaveAsync() { }
[VeloxCommand] private Task SaveAsync(object? p, CancellationToken ct) { }
[VeloxCommand] private Task SaveAsync(object? p) { }
[VeloxCommand] private Task SaveAsync(CancellationToken ct) { }
```

[VeloxCommand] 提供可选参数

| 参数名 | 类型 | 默认 | 描述 |
| --- | --- | --- | --- |
| name | string | Auto | 命令名称 |  |
| canValidate | bool | false | 启用可执行性验证 |  |
| semaphore | int | 1 | 最大任务并发数 |  |

> **执行与取消**

无论您以何种方式执行命令，本质都是等待Task，因此多数情况下不必担心调用 Execute 而非 ExecuteAsync 会阻塞UI，它们几乎等价

```csharp
SaveCommand.Execute(null);

await SaveCommand.ExecuteAsync(null);
```

命令的取消也同时提供两个版本，此处演示可等待版本

```csharp
await SaveCommand.LockAsync(); // 锁定，除了当前执行中的任务，不再允许新的任务执行

// await SaveCommand.InterruptAsync(); 中断当前执行中的任务 （ 不含排队中的任务 ）
await SaveCommand.ClearAsync(); // 中断所有任务

await SaveCommand.UnLockAsync();
```

> **启用验证**

当您设置 canValidate: true，内部将生成 CanExecuteSaveCommand 回调，您可在回调函数中判定是否允许执行命令

```csharp
[VeloxCommand(canValidate: true)]
private void Save()
{

}

private partial bool CanExecuteSaveCommand(object? parameter)
{
    
}
```

可以手动通知UI重新评估命令的可执行性，此时parameter为null，若执行命令，此时parameter是命令接收到的参数

```csharp
SaveCommand.Notify();
```

> **回调事件**

你可以监听当前执行中的任务总数、做异常处理等操作，这些回调事件均携带 CommandEventArgs 参数

| 钩子 | 描述 |
| --- | --- |
| Created | 任务创建 |
| Enqueued | 任务进入队列 |
| Dequeued | 任务离开队列 |
| Started | 任务开始 |
| Completed | 任务完成 |
| Failed | 任务失败 |
| Canceled | 任务取消 |
| Exited | 任务最终退出 |