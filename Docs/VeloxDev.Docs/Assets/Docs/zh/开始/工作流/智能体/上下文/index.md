# 自然语言注释

---

## 目录

1. [`[AgentContext]` — 自然语言注释的核心](#1-agentcontext--自然语言注释的核心)
2. [`[AgentCommandParameter]` — 命令参数类型标注](#2-agentcommandparameter--命令参数类型标注)
3. [`[SlotSelectors]` — 枚举选择器限定](#3-slotselectors--枚举选择器限定)
4. [`AgentLanguages` — 多语言支持](#4-agentlanguages--多语言支持)
5. [`AgentContextReader` — 运行时读取注释](#5-agentcontextreader--运行时读取注释)
6. [`[VeloxProperty]` / `[VeloxCommand]` — 与 Agent 系统的协作](#6-veloxproperty--veloxcommand--与-agent-系统的协作)
7. [完整示例：构建一个可被 AI 理解的组件](#7-完整示例构建一个可被-ai-理解的组件)

---

## 1. `[AgentContext]` — 自然语言注释的核心

### 定义

```csharp
[AttributeUsage(AttributeTargets.All, Inherited = false, AllowMultiple = true)]
public class AgentContextAttribute(AgentLanguages language, string context) : Attribute
{
    public AgentLanguages Language { get; }
    public string Context { get; }
}
```

**关键设计要点：**
- `AllowMultiple = true` — 同一元素可标注多种语言的描述
- `AttributeTargets.All` — 可标注在类、接口、属性、方法、枚举、枚举字段等所有元素上
- 两个参数：**语言** + **自然语言描述文本**

### 1.1 标注接口

接口是 Agent 了解组件契约的主要入口。

```csharp
using VeloxDev.AI;

/// <summary>
/// 给人类开发者看的 XML 文档注释
/// </summary>
[AgentContext(AgentLanguages.Chinese, "工作流Node组件接口，维护节点的空间信息、Slot集合以及广播行为")]
[AgentContext(AgentLanguages.English, "Workflow Node component interface, maintaining node geometry, slot collection, and broadcast behaviors")]
public interface IWorkflowNodeViewModel : IWorkflowViewModel
{
    // ...
}
```

**解释：**
- 中文和英文描述分别提供给使用不同语言的 Agent
- Agent 读取 `[AgentContext]` 后就能理解该接口的用途
- XML 文档注释（`///`）是给人看的，`[AgentContext]` 是给 AI Agent 看的

### 1.2 标注实现类

```csharp
[AgentContext(AgentLanguages.Chinese, "工作流Node组件接口的默认实现类")]
[AgentContext(AgentLanguages.English, "The default implementation class of the workflow Node component interface")]
public sealed partial class NodeViewModelBase : IWorkflowNodeViewModel, IWorkflowIdentifiable
{
    // ...
}
```

**解释：**
- 当 Agent 需要创建或查询 `IWorkflowNodeViewModel` 的实例时，会看到这个实现类的描述
- `sealed` 类告诉 Agent 这是最终实现，不可继承

### 1.3 标注属性

属性上的 `[AgentContext]` 告诉 Agent 该属性的含义、取值范围和业务约束。

```csharp
[AgentContext(AgentLanguages.Chinese, "当前Node在画布中的锚点坐标")]
[AgentContext(AgentLanguages.English, "The anchor position of the current node on the canvas")]
public Anchor Anchor { get; set; }
```

**在描述中嵌入"隐含规则"：**

```csharp
[AgentContext(AgentLanguages.Chinese, "是否处于活跃状态")]
[AgentContext(AgentLanguages.English, "Indicates whether the workflow is currently running.")]
[VeloxProperty] private bool isActive = false;
```

**在描述中嵌入"默认值"供 Agent 解析：**

```csharp
[AgentContext(AgentLanguages.Chinese, "布尔选择器节点，将输入路由到 True 或 False 输出口。默认大小为 260*200")]
[AgentContext(AgentLanguages.English, "Bool selector node that routes input to True or False output slot based on Condition. Default size: 260×200")]
public partial class BoolSelectorNodeViewModel
```

```csharp
[AgentContext(AgentLanguages.Chinese, "派生的Node组件之一，作为任务执行者，默认大小为 320*260")]
[AgentContext(AgentLanguages.English, "A derived Node component that acts as a task executor. Default size: 320×260. Never use Size(0,0).")]
public partial class NodeViewModel
```

> **💡 关键技巧：** 在描述中嵌入"默认大小 320×260"、"Never use Size(0,0)" 等约束信息，Agent 会自动解析并遵守。

### 1.4 标注枚举类型

```csharp
[AgentContext(AgentLanguages.Chinese, "定义工作流插槽在两个方向上的连接容量")]
[AgentContext(AgentLanguages.English, "Defines the connection capacity of a workflow slot in two independent directions")]
[Flags]
public enum SlotChannel : int
{
    [AgentContext(AgentLanguages.Chinese, "无权限：默认值，表示不允许建立任何连接。")]
    [AgentContext(AgentLanguages.English, "None: Default value indicating no connections are allowed.")]
    None = 0,

    [AgentContext(AgentLanguages.Chinese, "单一目标：当前插槽最多只能主动连接到 1 个目标插槽")]
    [AgentContext(AgentLanguages.English, "One Target: This slot may actively connect to at most 1 target slot")]
    OneTarget = 1,

    [AgentContext(AgentLanguages.Chinese, "多目标：当前插槽最多可以主动连接到 2 个目标插槽")]
    [AgentContext(AgentLanguages.English, "Multiple Targets: This slot may actively connect to up to 2 target slots")]
    MultipleTargets = 2,
}
```

**解释：**
- Agent 读取枚举定义后能理解每个枚举值的业务含义
- 这样 Agent 可以做出更准确的决策（例如知道 `None` 表示不允许连接）
- 支持 `[Flags]` 枚举，Agent 可以理解组合值

### 1.5 标注数据类/结构体

```csharp
[AgentContext(AgentLanguages.Chinese, "用于在工作流系统中描述组件的空间位置")]
[AgentContext(AgentLanguages.English, "Used to describe the spatial position of components in the workflow system")]
public sealed partial class Anchor(double left = 0d, double top = 0d, int layer = 0)
{
    [VeloxProperty]
    [AgentContext(AgentLanguages.Chinese, "水平坐标，单位为像素")]
    [AgentContext(AgentLanguages.English, "Horizontal coordinate, in pixels")]
    private double _horizontal = left;

    [VeloxProperty]
    [AgentContext(AgentLanguages.Chinese, "垂直坐标，单位为像素")]
    [AgentContext(AgentLanguages.English, "Vertical coordinate, in pixels")]
    private double _vertical = top;
}
```

**解释：**
- 数据类（值类型）的描述告诉 Agent 这些数据结构的含义
- 属性级别的描述使 Agent 知道每个字段的单位（像素）和范围

### 1.6 标注命令属性

当属性表示一个可执行命令时，`[AgentContext]` 描述该命令的语义：

```csharp
[AgentContext(AgentLanguages.Chinese, "正向广播数据，参数为Nullable")]
[AgentContext(AgentLanguages.English, "Broadcast data forward, parameter is Nullable")]
[AgentCommandParameter]
public IVeloxCommand BroadcastCommand { get; }
```

```csharp
[AgentContext(AgentLanguages.Chinese, "设置锚点坐标，参数为Anchor")]
[AgentContext(AgentLanguages.English, "Set anchor command, parameter is Anchor")]
[AgentCommandParameter(typeof(Anchor))]
public IVeloxCommand SetAnchorCommand { get; }
```

**解释：**
- 描述中说明命令的用途和参数类型
- 配合 `[AgentCommandParameter]` 使用（见下节）

---

## 2. `[AgentCommandParameter]` — 命令参数类型标注

### 定义

```csharp
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Method | AttributeTargets.Field, AllowMultiple = false)]
public class AgentCommandParameterAttribute : Attribute
{
    public Type? ParameterType { get; }
    public AgentCommandParameterAttribute() => ParameterType = null;          // 无参数
    public AgentCommandParameterAttribute(Type parameterType) => ParameterType = parameterType;  // 指定参数类型
}
```

### 2.1 无参数命令

```csharp
[AgentContext(AgentLanguages.English, "Delete the current connection, parameter is Null")]
[AgentCommandParameter]                     // ← ParameterType = null
public IVeloxCommand DeleteCommand { get; }
```

Agent 知道调用此命令不需要传递参数。

### 2.2 带类型参数命令

```csharp
[AgentContext(AgentLanguages.English, "Set anchor command, parameter is Anchor")]
[AgentCommandParameter(typeof(Anchor))]     // ← ParameterType = typeof(Anchor)
public IVeloxCommand SetAnchorCommand { get; }
```

Agent 知道：
1. 调用此命令需要传入一个 `Anchor` 类型的参数
2. 可以从 `AgentContextCollector` 获取 `Anchor` 的结构描述
3. 构造正确的 JSON 参数体

### 2.3 多参数命令（通过协议类）

```csharp
// 定义协议类
public record struct MoveNodeProtocol(
    string NodeId,
    double NewLeft,
    double NewTop
);

// 在接口中引用
[AgentContext(AgentLanguages.Chinese, "移动节点到新位置，参数为MoveNodeProtocol")]
[AgentContext(AgentLanguages.English, "Move node to new position, parameter is MoveNodeProtocol")]
[AgentCommandParameter(typeof(MoveNodeProtocol))]
public IVeloxCommand MoveNodeCommand { get; }
```

---

## 3. `[SlotSelectors]` — 枚举选择器限定

### 定义

```csharp
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class SlotSelectorsAttribute : Attribute
{
    public Type[] AllowedEnumTypes { get; }           // 编译时安全
    public string[] AllowedEnumTypeNames { get; }     // 序列化友好

    public SlotSelectorsAttribute(params Type[] allowedEnumTypes);
    // ⚠️ 注意：C# 不允许 params 同时支持 Type[] 和 string[]，
    // 实际字符串重载通过运行时反射解析类型名
}
```

### 3.1 编译时安全：`typeof` 重载

```csharp
// 单个选择器类型
[VeloxProperty]
[SlotSelectors(typeof(bool))]
public partial SlotEnumerator<SlotViewModel> OutputSlots { get; set; }

// 多个选择器类型
[VeloxProperty]
[SlotSelectors(typeof(NetworkRequestMethod), typeof(VoltageRange), typeof(ModelProtocol))]
public partial SlotEnumerator<SlotViewModel> OutputSlots { get; set; }
```

**解释：**
- `SlotEnumerator` 是一种特殊的集合属性，每个枚举值对应一个输出插槽
- `[SlotSelectors]` 告诉 Agent 哪些枚举类型可作为路由选择器
- Agent 调用 `SetEnumSlotCollection` 工具时，会自动读取此注解

### 3.2 序列化友好：字符串重载

```csharp
// 当 Type 引用不可用时（跨程序集、JSON 配置、动态加载）
[SlotSelectors("MyNamespace.MyEnum", "MyNamespace.OtherEnum")]
public partial SlotEnumerator<SlotViewModel> OutputSlots { get; set; }
```

**适用场景：**
- 跨程序集序列化（`Type` 引用可能失效）
- JSON 配置文件驱动的类型注册
- 动态加载的插件系统

### 3.3 底层发现机制

```csharp
// WorkflowAgentScope 内部自动收集 SlotSelector 中的枚举类型
private void CollectSlotSelectorTypes(SlotSelectorsAttribute? sel, AgentLanguages lang)
{
    if (sel == null) return;
    foreach (var et in sel.AllowedEnumTypes)
        TryRegisterEnum(et, lang);

    // 字符串重载：Type.GetType 按名解析
    if (sel.AllowedEnumTypes.Length == 0)
    {
        foreach (var name in sel.AllowedEnumTypeNames)
        {
            var resolved = Type.GetType(name, throwOnError: false);
            if (resolved != null)
                TryRegisterEnum(resolved, lang);
        }
    }
}
```

---

## 4. `AgentLanguages` — 多语言支持

### 定义

`AgentLanguages` 枚举支持 **33 种语言**，涵盖全球主要语言：

```csharp
public enum AgentLanguages
{
    English,       // 英语
    Chinese,       // 简体中文
    TraditionalChinese,
    Japanese,
    Korean,
    Spanish,
    French,
    German,
    // ... 共 33 种
}
```

### 4.1 语言代码解析

```csharp
// 从字符串解析（如 "zh-cn", "en", "ja-JP"）
var lang = AgentLanguagesExtensions.FromCultureName("zh-cn");
// → AgentLanguages.Chinese
```

### 4.2 多语言标注模式

```csharp
// 推荐：中英双语标注
[AgentContext(AgentLanguages.Chinese, "创建节点，参数为IWorkflowNodeViewModel")]
[AgentContext(AgentLanguages.English, "Create node command, parameter is IWorkflowNodeViewModel")]
[AgentCommandParameter(typeof(IWorkflowNodeViewModel))]
public IVeloxCommand CreateNodeCommand { get; }
```

**设计原则：**
- **中文开发者**用中文标注，**英文开发者**用英文标注
- Agent 根据用户的 `WithPromptLanguage` / `WithOutputLanguage` 设置读取对应语言
- 未找到匹配语言的注释时，Agent 会尝试使用已注册的其他语言

---

## 5. `AgentContextReader` — 运行时读取注释

### 定义

```csharp
public static class AgentContextReader
{
    // 读取类型上的指定语言注释
    public static string[] GetContexts(Type type, AgentLanguages language);

    // 读取成员上的指定语言注释
    public static string[] GetContexts(MemberInfo member, AgentLanguages language);

    // 判断是否有任何 AgentContext 注释
    public static bool HasAgentContext(MemberInfo member);
}
```

### 5.1 读取类型注释

```csharp
using VeloxDev.AI;

var descriptions = AgentContextReader.GetContexts(
    typeof(IWorkflowNodeViewModel),
    AgentLanguages.English);

// 输出: ["Workflow Node component interface, maintaining ..."]
foreach (var desc in descriptions)
    Console.WriteLine(desc);
```

### 5.2 读取属性注释

```csharp
var propInfo = typeof(IWorkflowNodeViewModel).GetProperty(nameof(IWorkflowNodeViewModel.Anchor))!;
var propDescs = AgentContextReader.GetContexts(propInfo, AgentLanguages.Chinese);

// 输出: ["当前Node在画布中的锚点坐标"]
```

### 5.3 判断是否存在注释

```csharp
if (AgentContextReader.HasAgentContext(someMember))
{
    // 该成员有 Agent 注释，可以被 Agent 发现
}
```

### 5.4 实际应用：自定义上下文收集

```csharp
// 构建自定义的 Agent 上下文提示
var sb = new StringBuilder();
sb.AppendLine("## Available Types");

foreach (var type in myTypes)
{
    if (!AgentContextReader.HasAgentContext(type))
        continue;

    var descs = AgentContextReader.GetContexts(type, AgentLanguages.English);
    sb.AppendLine($"- **{type.Name}**: {string.Join("; ", descs)}");

    // 收集属性
    foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
    {
        if (!AgentContextReader.HasAgentContext(prop))
            continue;

        var propDescs = AgentContextReader.GetContexts(prop, AgentLanguages.English);
        sb.AppendLine($"  - `{prop.Name}`: {string.Join("; ", propDescs)}");
    }
}
```

---

## 6. `[VeloxProperty]` / `[VeloxCommand]` — 与 Agent 系统的协作

虽然 `[VeloxProperty]` 和 `[VeloxCommand]` 本质上属于 MVVM 源码生成器，但它们与 Agent 系统紧密协作：

### 6.1 `[VeloxProperty]` — Agent 可读写的属性

```csharp
[VeloxProperty]                              // ← 生成完整 MVVM 属性
[AgentContext(AgentLanguages.English, "Output slot (sender)")]
public partial SlotViewModel OutputSlot { get; set; }
```

**Agent 工具依赖：**
- `ListProperties` — 列出所有 `[VeloxProperty]` 属性，读取 `[AgentContext]` 描述
- `GetProperty` — 读取属性值
- `SetProperty` / `PatchProperties` — 写入属性值（受 `ComponentPatcher` 安全规则约束）

### 6.2 `[VeloxCommand]` — Agent 可调用的命令

```csharp
[VeloxCommand]                               // ← 生成 IVeloxCommand
[AgentContext(AgentLanguages.English, "Starts the workflow: broadcasts SeedPayload")]
private async Task OpenWorkflow(object? parameters, CancellationToken ct)
{
    // ...
}
```

**Agent 工具依赖：**
- `ListCommands` — 列出所有命令及 `[AgentContext]` 描述
- `ExecuteCommand` — 调用命令（参数通过 `[AgentCommandParameter]` 指定类型）

### 6.3 带验证的命令

```csharp
[VeloxCommand(canValidate: true)]
private Task Minus(object? sender, CancellationToken ct)
{
    Index--;
    return Task.CompletedTask;
}

// 必须实现此分部方法：
private partial bool CanExecuteMinusCommand(object? parameter) => _index > 0;
```

Agent 在调用命令前会检查 `CanExecute`，如果返回 `false` 则不会执行。

---

## 7. 完整示例：构建一个可被 AI 理解的组件

以下是一个完整的、符合最佳实践的组件定义：

```csharp
using VeloxDev.AI;
using VeloxDev.MVVM;
using VeloxDev.WorkflowSystem;

namespace MyWorkflow.Nodes;

// ── 1. 枚举定义 ──
[AgentContext(AgentLanguages.Chinese, "数据源类型")]
[AgentContext(AgentLanguages.English, "Data source type")]
public enum DataSourceType
{
    [AgentContext(AgentLanguages.English, "HTTP REST API endpoint")]
    HttpApi,

    [AgentContext(AgentLanguages.English, "Local file system path")]
    FileSystem,

    [AgentContext(AgentLanguages.English, "In-memory data buffer")]
    MemoryBuffer,
}

// ── 2. 数据协议 ──
[AgentContext(AgentLanguages.English, "Configuration for a data source node")]
public sealed partial class DataSourceConfig
{
    [VeloxProperty]
    [AgentContext(AgentLanguages.English, "Data source URL or file path")]
    private string _source = "";

    [VeloxProperty]
    [AgentContext(AgentLanguages.English, "Polling interval in milliseconds")]
    private int _pollingIntervalMs = 1000;
}

// ── 3. 接口定义 ──
[AgentContext(AgentLanguages.Chinese, "数据源节点接口，从外部系统获取数据")]
[AgentContext(AgentLanguages.English, "Data source node interface that fetches data from external systems")]
public interface IDataSourceNodeViewModel : IWorkflowNodeViewModel
{
    [AgentContext(AgentLanguages.English, "The data source type")]
    DataSourceType SourceType { get; set; }

    [AgentContext(AgentLanguages.English, "Output data slot")]
    [VeloxProperty]
    SlotViewModel OutputSlot { get; set; }

    [AgentContext(AgentLanguages.English, "Fetch data command, parameter is DataSourceConfig")]
    [AgentCommandParameter(typeof(DataSourceConfig))]
    IVeloxCommand FetchCommand { get; }
}

// ── 4. 实现类 ──
[AgentContext(AgentLanguages.Chinese, "数据源节点的默认实现，默认大小 300×200")]
[AgentContext(AgentLanguages.English, "Default data source node implementation. Default size: 300×200")]
[WorkflowBuilder.Node<MyNodeHelper>(workSemaphore: 1)]
public partial class DataSourceNodeViewModel : IDataSourceNodeViewModel
{
    [VeloxProperty]
    [AgentContext(AgentLanguages.English, "Data source type selection")]
    private DataSourceType _sourceType = DataSourceType.HttpApi;

    [VeloxProperty]
    [AgentContext(AgentLanguages.English, "Output slot (sender)")]
    private SlotViewModel _outputSlot = new();

    [VeloxCommand]
    [AgentContext(AgentLanguages.English, "Fetch data from the configured source")]
    private async Task Fetch(DataSourceConfig? config, CancellationToken ct)
    {
        // ... 实现逻辑
    }
}
```

**当 Agent 遇到这个组件时，它会获得以下信息：**

| 信息 | 来源 |
|------|------|
| "这是一个数据源节点" | 接口和类的 `[AgentContext]` |
| "默认大小 300×200" | 类的 `[AgentContext]` 描述文本 |
| "可以选 HttpApi/FileSystem/MemoryBuffer" | `DataSourceType` 枚举 + 每个字段的 `[AgentContext]` |
| "有 Fetch 命令，参数是 DataSourceConfig" | `FetchCommand` + `[AgentCommandParameter]` |
| "DataSourceConfig 有 Source 和 PollingIntervalMs" | `DataSourceConfig` 类的 `[VeloxProperty]` 属性 |
| "Api/FileSystem/MemoryBuffer 分别是什么" | 枚举字段的 `[AgentContext]` |

---

## 最佳实践总结

| 原则 | 说明 | 示例 |
|------|------|------|
| **双语标注** | 同时标注中文和英文 | `[AgentContext(AgentLanguages.Chinese, "...")]` + `[AgentContext(AgentLanguages.English, "...")]` |
| **嵌入默认值** | 在描述中包含默认尺寸、位置等 | `"默认大小 300×200"`、`"Default size: 320×260"` |
| **嵌入约束** | 在描述中包含不允许的值 | `"Never use Size(0,0)"` |
| **具体而非抽象** | 描述具体行为而非抽象概念 | ❌ `"处理数据"` → ✅ `"从 HTTP API 获取数据并广播到输出口"` |
| **标注枚举字段** | 枚举的每个字段都应标注 | 让 Agent 理解每个选项的语义 |
| **配合 CommandParameter** | 命令必须标注参数类型 | `[AgentCommandParameter(typeof(MyProtocol))]` |
| **一致的语言** | 同一元素的同语言描述保持风格一致 | |

---