# Weak Types Utilities

`VeloxDev.Core` provides four weak-reference utility types for holding object references without preventing GC collection.

---

## `WeakDelegate`

Weak reference wrapper for delegates. Uses `WeakReference` to hold the target and method, preventing delegate-based object leaks.

```csharp
var obj = new SomeClass();
var weakDelegate = new WeakDelegate(obj, nameof(SomeClass.MyMethod));
// Invoke silently skips if obj has been collected
weakDelegate.Invoke(args);
```

## `WeakCache<TKey, TValue>`

Key-value cache using `WeakReference<T>`. Entries automatically expire when the key object is collected.

```csharp
var cache = new WeakCache<object, string>();
var key = new object();
cache.Add(key, "value");
// After key is GC'd, TryGetValue returns false
```

## `WeakQueue<T>` / `WeakStack<T>`

Weak-reference-based queue and stack. Operations automatically skip collected entries.

```csharp
var queue = new WeakQueue<string>();
queue.Enqueue("item1");
var success = queue.TryDequeue(out var item);
```

## Use Cases

| Type | Use Case |
|------|----------|
| `WeakDelegate` | Event subscriptions without object leaks |
| `WeakCache` | Temporary caches tied to key-object lifetime |
| `WeakQueue` | Deferred task queues where losing tasks is acceptable |
| `WeakStack` | History stacks where entries may be GC'd |
