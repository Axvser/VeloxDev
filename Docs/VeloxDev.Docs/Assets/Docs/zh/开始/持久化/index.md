# 持久化

VeloxDev.Core.Extension 为视图模型提供一组扩展方法，具备以下特点：
    - Dictionary<,> 可使用任何类型作为Key，不再局限于string
    - 唯有属性的setter和getter均为public时，该属性才会被序列化，并且序列化只处理属性

> **同步**

```csharp
using VeloxDev.MVVM.Serialization;

// serialize
var json = tree.Serialize();
File.WriteAllText(path, json);

// deserialize
var loadedJson = File.ReadAllText(path);
var loadedTree = loadedJson.Deserialize<TreeViewModel>();
```

> **异步**

```csharp
using VeloxDev.MVVM.Serialization;

// serialize
var json = await tree.SerializeAsync();
await File.WriteAllTextAsync(path, json);

// deserialize
var loadedJson = await File.ReadAllTextAsync(path);
var loadedTree = await loadedJson.DeserializeAsync<TreeViewModel>();
```

> **流式**

```csharp
using VeloxDev.MVVM.Serialization;

// serialize
await using var writeStream = File.Create(path);
await tree.SerializeToStreamAsync(writeStream);

// deserialize
await using var readStream = File.OpenRead(path);
var loadedTree = await readStream.DeserializeFromStreamAsync<TreeViewModel>();
```

> **配置速查**

| 目标/场景 | 配置写法 | 为什么这样配 | 是否推荐 |
|---|---|---|---|
| 调试、查看 JSON、做 diff | `SerializationOptions.Create().WithIndented().WithTypeNameHandling(TypeNameHandling.Auto).WithNullValueHandling(NullValueHandling.Include).WithDefaultValueHandling(DefaultValueHandling.Include)` | JSON 可读性最好，且能完整保留多态、空值、默认值，最适合排查工作流恢复问题 | 强烈推荐 |
| 正式保存到文件 | `SerializationOptions.Create().WithCompact().WithTypeNameHandling(TypeNameHandling.Auto).WithNullValueHandling(NullValueHandling.Include).WithDefaultValueHandling(DefaultValueHandling.Include)` | 文件更小，但仍保留完整恢复所需信息，适合工作流持久化 | 推荐 |
| 只追求最小体积 | `SerializationOptions.Create().WithCompact().WithNullValueHandling(NullValueHandling.Ignore).WithDefaultValueHandling(DefaultValueHandling.Ignore)` | 能缩小 JSON，但可能丢失恢复所需字段，不适合复杂工作流对象图 | 谨慎使用 |
| 有派生节点、接口属性、多态对象 | `WithTypeNameHandling(TypeNameHandling.Auto)` | 让反序列化时能恢复真实运行时类型；Workflow 场景通常需要 | 必配 |
| 希望恢复后状态完全一致 | `WithNullValueHandling(NullValueHandling.Include).WithDefaultValueHandling(DefaultValueHandling.Include)` | 保留 `null` 和默认值，避免反序列化后状态偏差 | 推荐 |
| 只想让 JSON 更易读 | `WithIndented()` | 仅改善可读性，不改变对象语义 | 按需 |
| 只想减小文件大小 | `WithCompact()` | 去掉缩进和空白，最简单的压缩方式 | 按需 |
| 同步 API 使用 | `var json = tree.Serialize(options);` / `var tree = json.Deserialize<TreeViewModel>(options);` | 适合简单保存/加载 | 推荐 |
| 安全加载 | `json.TryDeserialize<TreeViewModel>(options, out var tree)` | 外部文件或用户输入可能损坏时更稳妥，不会直接抛异常 | 强烈推荐 |
| 异步 API 使用 | `await tree.SerializeAsync(options)` / `await json.DeserializeAsync<TreeViewModel>(options)` | 适合 UI、命令、文件 IO 链路 | 推荐 |
| 流式/Stream API 使用 | `await tree.SerializeToStreamAsync(stream, options)` / `await stream.DeserializeFromStreamAsync<TreeViewModel>(options)` | 适合文件流、网络流、内存流 | 推荐 |
| 最稳妥的 Workflow 默认方案 | `SerializationOptions.Create().WithIndented().WithTypeNameHandling(TypeNameHandling.Auto).WithNullValueHandling(NullValueHandling.Include).WithDefaultValueHandling(DefaultValueHandling.Include)` | 如果你不确定怎么选，就用这一套；对工作流对象图最安全 | 默认首选 |