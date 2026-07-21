# 持久化

将工作流序列化为 JSON —— 同一份数据可在 Desktop、Browser、Mobile 间共享。

---

## Demo 效果

```csharp
var json = tree.Serialize();          // Tree → JSON
var restored = json.Deserialize<TreeDefaultViewModel>(); // JSON → Tree
Console.WriteLine($"还原了 {restored.Nodes.Count} 个节点");
```

## 操作步骤

### 1. 安装

```shell
dotnet add package VeloxDev.Core.Extension
```

### 2. 序列化

```csharp
using VeloxDev.Extension.Serialization;

// 同步
string json = tree.Serialize();

// 异步
string jsonAsync = await tree.SerializeAsync();

// 格式化输出
string pretty = tree.Serialize(SerializationOptions.Create().WithIndented());
await File.WriteAllTextAsync("workflow.json", pretty);

// UTF8 字节
byte[] bytes = tree.SerializeToUtf8Bytes();
```

### 3. 反序列化

```csharp
// 安全反序列化
if (json.TryDeserialize<TreeDefaultViewModel>(out var restored))
    Console.WriteLine($"安全加载：{restored.Nodes.Count} 个节点");

// 失败抛异常
var tree = json.Deserialize<TreeDefaultViewModel>();

// 从文件加载
var fromFile = (await File.ReadAllTextAsync("workflow.json"))
    .Deserialize<TreeDefaultViewModel>();
```

## 完整 API

| 方法 | 说明 |
|------|------|
| `tree.Serialize()` | 序列化为 JSON |
| `tree.SerializeAsync()` | 异步序列化 |
| `json.Deserialize<T>()` | 反序列化（失败抛异常） |
| `json.TryDeserialize<T>(out var)` | 安全反序列化 |
| `tree.SerializeToUtf8Bytes()` | 序列化为 UTF8 字节 |
| `tree.SerializeToStreamAsync(stream)` | 序列化到流 |
| `new SerializationOptions().WithIndented()` | 缩进格式化 |
