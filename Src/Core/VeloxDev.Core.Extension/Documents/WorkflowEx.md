# WorkflowEx 序列化扩展库

## 概述

WorkflowEx 是一个基于 Newtonsoft.Json 的高性能序列化扩展库，专门为工作流系统设计。它提供了同步、异步和流式序列化功能，支持复杂对象图的循环引用处理。

## 功能特性

### 🔄 序列化模式
- **同步序列化** - 传统的同步方法调用
- **异步序列化** - 基于 Task 的异步操作，避免线程阻塞
- **流式序列化** - 支持大文件流式处理，内存效率高

### 🛡️ 高级功能
- **循环引用处理** - 自动处理对象间的循环引用
- **接口字典支持** - 支持以接口类型为键的字典序列化
- **类型安全** - 完整的类型检查和异常处理
- **可取消操作** - 支持 CancellationToken 取消异步操作

## 快速开始

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