# 弱类型工具集

`VeloxDev.Core` 提供四个弱引用相关的工具类型，用于安全持有不影响 GC 回收的对象引用。

---

## `WeakDelegate`

弱引用委托包装。使用 `WeakReference` 持有目标和方法，避免委托导致的对象无法回收。

```csharp
var obj = new SomeClass();
var weakDelegate = new WeakDelegate(obj, nameof(SomeClass.MyMethod));
// obj 被回收后，Invoke 静默跳过
weakDelegate.Invoke(args);
```

- 构造函数：`WeakDelegate(target, methodName)`
- 方法：`Invoke(params object?[] args)`
- 方法：`InvokeAsync(params object?[] args)`（Task 返回）

## `WeakCache<TKey, TValue>`

使用 `WeakReference<T>` 的键值缓存。当键对象被回收时，对应条目自动失效。

```csharp
var cache = new WeakCache<object, string>();
var key = new object();
cache.Add(key, "value");
cache.TryGetValue(key, out var val); // true
// key 被 GC 回收后 TryGetValue 返回 false
```

## `WeakQueue<T>`

基于 `WeakReference<T>` 的队列。出队时自动跳过已被回收的条目。

```csharp
var queue = new WeakQueue<string>();
queue.Enqueue("item1");
queue.Enqueue("item2");
var success = queue.TryDequeue(out var item); // 获取存活项
```

## `WeakStack<T>`

基于 `WeakReference<T>` 的栈。弹出时自动跳过已被回收的条目。

```csharp
var stack = new WeakStack<string>();
stack.Push("item1");
stack.Push("item2");
var success = stack.TryPop(out var item);
```

---

## 适用场景

| 类型 | 适用场景 |
|------|----------|
| `WeakDelegate` | 事件订阅中避免委托导致的对象泄漏 |
| `WeakCache` | 生命周期由键对象控制的临时缓存 |
| `WeakQueue`  | 延迟任务队列，接受任务丢失 |
| `WeakStack`  | 历史记录栈，允许条目被 GC 回收 |
