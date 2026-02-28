# VeloxDev.Core.Extension

> VeloxDev.Core 尽量不依赖于任何第三方库，而 Extension 作为一个可选的扩展包，包含一组基于第三方库实现的高级功能，可以进一步优化使用体验

---

## 📚 包含内容

* 🔄 基于 Newtonsoft.Json 的 ViewModel 序列化扩展

---

# ViewModel 序列化

> 相对于默认的序列化行为，此扩展提供了一组新特性以便于在MVVM模式下实施正反序列化。

> ① 支持抽象(抽象类、接口、装箱)，序列化将保留原始类型信息

> ② 支持复杂字典Key，支持字典嵌套

> ③ 支持引用处理

> ④ 仅处理实现了 INotifyPropertyChanged 的目标

> ⑤ 仅处理同时具备 public getter 与 public setter 的属性

> 下方是对于 VeloxDev.Core 工作流系统中，视图模型的序列化示例

### 同步处理

```csharp
using VeloxDev.Core.Extension;

// 定义工作流模型 ( 此处只是示例，具体见 VeloxDev.Core - Examples - Workflow - V3 )
public class MyWorkflow : IWorkflowTreeViewModel
{
    public string Name { get; set; }
    public List<WorkflowNode> Nodes { get; set; }
}

// 创建实例
var workflow = new MyWorkflow { Name = "示例工作流" };

// 同步序列化
string json = workflow.Mutualize();

// 同步反序列化
bool success = json.TryDeMutualize(out var result);
```

### 异步处理

```csharp
// 异步序列化
string json = await workflow.MutualizeAsync();

// 异步反序列化（元组方式）
var (success, result) = await json.TryDeMutualizeAsync<MyWorkflow>();

// 异步反序列化（异常方式）
try
{
    var result = await json.DeMutualizeAsync<MyWorkflow>();
}
catch (JsonMutualizationException ex)
{
    Console.WriteLine($"反序列化失败: {ex.Message}");
}
```

### 流式异步处理

```csharp
// 序列化到文件流
using var fileStream = File.Create("workflow.json");
await workflow.MutualizeToStreamAsync(fileStream);

// 从文件流反序列化
using var readStream = File.OpenRead("workflow.json");
var result = await readStream.DeMutualizeFromStreamAsync<MyWorkflow>();

// 流式处理大文件
var (success, workflow) = await readStream.TryDeMutualizeFromStreamAsync<MyWorkflow>();
```
