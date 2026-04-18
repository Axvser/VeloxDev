# WeakTypes 测试

针对 `VeloxDev.WeakTypes` 命名空间的单元测试 —— 基于弱引用的集合类型，允许 GC 自动回收无用条目。

| 测试文件 | 测试目标 | 关键场景 |
|---|---|---|
| `WeakStackTests.cs` | `WeakStack<T>` | Push/Pop/Peek/Clear、LIFO 顺序、`PushRange`、null 守卫、枚举遍历 |
| `WeakQueueTests.cs` | `WeakQueue<T>` | Enqueue/Dequeue/Peek/Clear、FIFO 顺序、`EnqueueRange`、null 守卫、枚举遍历 |
| `WeakCacheTests.cs` | `WeakCache<TKey,TValue>` | AddOrUpdate/TryGet/Remove/ForeachCache、覆写、批量添加触发清理 |
| `WeakDelegateTests.cs` | `WeakDelegate<T>` | AddHandler/Invoke、Clone 重建、null 安全 |
