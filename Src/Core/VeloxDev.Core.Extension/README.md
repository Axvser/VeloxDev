# VeloxDev.Core.Extension

> VeloxDev.Core 尽量不依赖于任何第三方库，而 Extension 作为一个可选的扩展包，包含一组基于第三方库实现的高级功能，可以进一步优化使用体验

---

## 📚 包含内容

* 🔄 基于 Newtonsoft.Json 的 Workflow 序列化扩展

---

# WorkflowEx 序列化扩展库

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
