# Natural Language Comments

---

## Contents

1. [`[AgentContext]` — the core of natural language comments](#1-agentcontext--自然语言注释的核心)
2. [`[AgentCommandParameter]` — Command Parameter Type Annotation](#2-agentcommandparameter--命令参数类型标注)
3. [`[SlotSelectors]` — Enum Selector Qualifier](#3-slotselectors--枚举选择器限定)
4. [`AgentLanguages` — Multilingual Support](#4-agentlanguages--多语言支持)
5. [`AgentContextReader` — Reading Annotations at Runtime](#5-agentcontextreader--运行时读取注释)
6. [`[VeloxProperty]` / `[VeloxCommand]` — Collaboration with the Agent System](#6-veloxproperty--veloxcommand--与-agent-系统的协作)
7. [Complete Example: Building an AI-Understandable Component](#7-完整示例构建一个可被-ai-理解的组件)

---

## 1. `[AgentContext]` — the core of natural language comments

### Definition

```csharp
[AttributeUsage(AttributeTargets.All, Inherited = false, AllowMultiple = true)]
public class AgentContextAttribute(AgentLanguages language, string context) : Attribute
{
    public AgentLanguages Language { get; }
    public string Context { get; }
}
```

**Key Design Points:**
- `AllowMultiple = true` — The same element can be annotated with descriptions in multiple languages.
- `AttributeTargets.All` — can be applied to all elements such as classes, interfaces, properties, methods, enums, enum fields, etc.
- Two parameters: **language** + **natural language description text**

### 1.1 Annotation Interface

The interface is the main entry point for the Agent to understand the component contract.

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

**Explanation:**
- Chinese and English descriptions are provided respectively to Agents using different languages.
- After Agent reads `[AgentContext]`, it can understand the purpose of the interface.
- XML documentation comments (`///`) are for humans, `[AgentContext]` is for AI agents.

### 1.2 Annotation Implementation Class

```csharp
[AgentContext(AgentLanguages.Chinese, "工作流Node组件接口的默认实现类")]
[AgentContext(AgentLanguages.English, "The default implementation class of the workflow Node component interface")]
public sealed partial class NodeViewModelBase : IWorkflowNodeViewModel, IWorkflowIdentifiable
{
    // ...
}
```

**Explanation:**
- When the Agent needs to create or query an instance of `IWorkflowNodeViewModel`, it will see the description of this implementation class.
- `sealed` class tells the Agent that this is the final implementation and cannot be inherited.

### 1.3 Annotation Properties

`[AgentContext]` on a property tells the Agent the meaning, value range, and business constraints of the property.

```csharp
[AgentContext(AgentLanguages.Chinese, "当前Node在画布中的锚点坐标")]
[AgentContext(AgentLanguages.English, "The anchor position of the current node on the canvas")]
public Anchor Anchor { get; set; }
```

**Embed "implicit rules" in the description:**

```csharp
[AgentContext(AgentLanguages.Chinese, "是否处于活跃状态")]
[AgentContext(AgentLanguages.English, "Indicates whether the workflow is currently running.")]
[VeloxProperty] private bool isActive = false;
```

**Embed "default value" in the description for Agent parsing:**

```csharp
[AgentContext(AgentLanguages.Chinese, "布尔选择器节点，将输入路由到 True 或 False 输出口。默认大小为 260*200")]
[AgentContext(AgentLanguages.English, "Bool selector node that routes input to True or False output slot based on Condition. Default size: 260×200")]
public partial class BoolSelectorNodeViewModel
```

```csharp
[AgentContext(AgentLanguages.Chinese, "A derived Node component that acts as a task executor. Default size: 320*260")]
[AgentContext(AgentLanguages.English, "A derived Node component that acts as a task executor. Default size: 320×260. Never use Size(0,0).")]
public partial class NodeViewModel
``` **💡 Key tip:** Embed constraint information like "default size 320×260", "Never use Size(0,0)" in the description, and the Agent will automatically parse and comply.

### 1.4 Annotated Enum Types

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

**Explanation:**
- After reading the enum definition, the Agent can understand the business meaning of each enum value.
- This way the agent can make more accurate decisions (e.g., knowing that `None` indicates that connection is not allowed).
- Supports `[Flags]` enums, Agent can understand combined values.

### 1.5 Annotated Data Classes/Structs

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

**Explanation:**
- The description of data classes (value types) tells the Agent the meaning of these data structures.
Attribute-level descriptions enable the Agent to know the unit (pixels) and range of each field.

### 1.6 Annotating Command Attributes

When a property represents an executable command, `[AgentContext]` describes the semantics of the command:

```csharp
[AgentContext(AgentLanguages.Chinese, "Broadcast data forward, parameter is Nullable")]
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

**Explanation:**
- The description explains the purpose of the command and the parameter types.
- Use with `[AgentCommandParameter]` (see next section)

---

## 2. `[AgentCommandParameter]` — Command parameter type annotation

### Definition

```csharp
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Method | AttributeTargets.Field, AllowMultiple = false)]
public class AgentCommandParameterAttribute : Attribute
{
    public Type? ParameterType { get; }
    public AgentCommandParameterAttribute() => ParameterType = null;          // no parameters
    public AgentCommandParameterAttribute(Type parameterType) => ParameterType = parameterType;  // specify parameter type
}
```

### 2.1 No-Parameter Commands

```csharp
[AgentContext(AgentLanguages.English, "Delete the current connection, parameter is Null")]
[AgentCommandParameter]                     // ← ParameterType = null
public IVeloxCommand DeleteCommand { get; }
```

The agent knows that calling this command does not require passing parameters.

### 2.2 Commands with Type Parameters

```csharp
[AgentContext(AgentLanguages.English, "Set anchor command, parameter is Anchor")]
[AgentCommandParameter(typeof(Anchor))]     // ← ParameterType = typeof(Anchor)
public IVeloxCommand SetAnchorCommand { get; }
```

Agent knows:
To call this command, you need to pass a parameter of type `Anchor`.
2. You can get the structure description of `Anchor` from `AgentContextCollector`.
3. Construct the correct JSON parameter body

### 2.3 Multi-parameter Commands (via Protocol Class)

```csharp
// Define the protocol class
public record struct MoveNodeProtocol(
    string NodeId,
    double NewLeft,
    double NewTop
);

// Referenced in the interface
[AgentContext(AgentLanguages.Chinese, "移动节点到新位置，参数为MoveNodeProtocol")]
[AgentContext(AgentLanguages.English, "Move node to new position, parameter is MoveNodeProtocol")]
[AgentCommandParameter(typeof(MoveNodeProtocol))]
public IVeloxCommand MoveNodeCommand { get; }
```

---

## 3. `[SlotSelectors]` — Enum Selector Qualification

### Definition

```csharp
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class SlotSelectorsAttribute : Attribute
{
    public Type[] AllowedEnumTypes { get; }           // Compile-time safety
    public string[] AllowedEnumTypeNames { get; }     // Serialization-friendly

    public SlotSelectorsAttribute(params Type[] allowedEnumTypes);
    // ⚠️ Note: C# does not allow params to support both Type[] and string[] simultaneously,
    // The actual string overload resolves type names via runtime reflection.
}
```

### 3.1 Compile-time safety: `typeof` overload

```csharp
// Single selector type
[VeloxProperty]
[SlotSelectors(typeof(bool))]
public partial SlotEnumerator<SlotViewModel> OutputSlots { get; set; }

// Multiple selector types
[VeloxProperty]
[SlotSelectors(typeof(NetworkRequestMethod), typeof(VoltageRange), typeof(ModelProtocol))]
public partial SlotEnumerator<SlotViewModel> OutputSlots { get; set; }
```

**Explanation:**
- `SlotEnumerator` is a special collection property where each enumerated value corresponds to an output slot.
- `[SlotSelectors]` tells the Agent which enum types can be used as route selectors.
- When the Agent calls the `SetEnumSlotCollection` tool, it will automatically read this annotation.

### 3.2 Serialization-Friendly: String Overloading

```csharp
// When Type references are unavailable (cross-assembly, JSON configuration, dynamic loading)
[SlotSelectors("MyNamespace.MyEnum", "MyNamespace.OtherEnum")]
public partial SlotEnumerator<SlotViewModel> OutputSlots { get; set; }
```

**Use cases:**
- Cross-assembly serialization (`Type` references may become invalid)
- JSON configuration file-driven type registration
- Dynamically loaded plugin system

### 3.3 Underlying Discovery Mechanism

```csharp
// WorkflowAgentScope internally automatically collects enum types in SlotSelector
private void CollectSlotSelectorTypes(SlotSelectorsAttribute? sel, AgentLanguages lang)
{
    if (sel == null) return;
    foreach (var et in sel.AllowedEnumTypes)
        TryRegisterEnum(et, lang);

    // String overload: resolve by name via Type.GetType
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

## 4. `AgentLanguages` — Multilingual Support

### Definition

The `AgentLanguages` enumeration supports **33 languages**, covering major global languages:

```csharp
public enum AgentLanguages
{
    English,       // English
    Chinese,       // Simplified Chinese
    TraditionalChinese,
    Japanese,
    Korean,
    Spanish,
    French,
    German,
    // ... 33 in total
}
```

### 4.1 Language Code Parsing

```csharp
// Parse from string (e.g., "zh-cn", "en", "ja-JP")
var lang = AgentLanguagesExtensions.FromCultureName("zh-cn");
// → AgentLanguages.Chinese
```

### 4.2 Multilingual Annotation Mode

```csharp
// Recommended: bilingual annotation in Chinese and English
[AgentContext(AgentLanguages.Chinese, "创建节点，参数为IWorkflowNodeViewModel")]
[AgentContext(AgentLanguages.English, "Create node command, parameter is IWorkflowNodeViewModel")]
[AgentCommandParameter(typeof(IWorkflowNodeViewModel))]
public IVeloxCommand CreateNodeCommand { get; }
```

**Design principles:**
- **Chinese developers** mark in Chinese, **English developers** mark in English.
- Agent reads the corresponding language based on the user's `WithPromptLanguage` / `WithOutputLanguage` settings.
- When a comment in a matching language is not found, the Agent will try to use other registered languages.

---

## 5. `AgentContextReader` — Runtime Annotation Reading

### Definition

```csharp
public static class AgentContextReader
{
    // Read the specified language annotation on the type
    public static string[] GetContexts(Type type, AgentLanguages language);

    // Read the specified language annotation on the member
    public static string[] GetContexts(MemberInfo member, AgentLanguages language);

    // Determine if there is any AgentContext annotation
    public static bool HasAgentContext(MemberInfo member);
}
```

### 5.1 Read Type Annotations

```csharp
using VeloxDev.AI;

var descriptions = AgentContextReader.GetContexts(
    typeof(IWorkflowNodeViewModel),
    AgentLanguages.English);

// Output: ["Workflow Node component interface, maintaining ..."]
foreach (var desc in descriptions)
    Console.WriteLine(desc);
```

### 5.2 Read Attribute Comments

```csharp
var propInfo = typeof(IWorkflowNodeViewModel).GetProperty(nameof(IWorkflowNodeViewModel.Anchor))!;
var propDescs = AgentContextReader.GetContexts(propInfo, AgentLanguages.Chinese);

// Output: ["Current node's anchor coordinates on canvas"]
```

### 5.3 Checking for Comments

```csharp
if (AgentContextReader.HasAgentContext(someMember))
{
    // This member has an Agent comment, so it can be discovered by the Agent.
}
```

### 5.4 Practical Application: Custom Context Collection

```csharp
// Build a custom Agent context prompt
var sb = new StringBuilder();
sb.AppendLine("## Available Types");

foreach (var type in myTypes)
{
    if (!AgentContextReader.HasAgentContext(type))
        continue;

    var descs = AgentContextReader.GetContexts(type, AgentLanguages.English);
    sb.AppendLine($"- **{type.Name}**: {string.Join("; ", descs)}");

    // Collect properties
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

## 6. `[VeloxProperty]` / `[VeloxCommand]` — Collaboration with the Agent System

Although `[VeloxProperty]` and `[VeloxCommand]` essentially belong to the MVVM source generator, they work closely with the Agent system:

### 6.1 `[VeloxProperty]` — Agent's readable and writable properties

```csharp
[VeloxProperty]                              // ← Generate complete MVVM property
[AgentContext(AgentLanguages.English, "Output slot (sender)")]
public partial SlotViewModel OutputSlot { get; set; }
```

**Agent Tool Dependencies:**
- `ListProperties` — Lists all `[VeloxProperty]` properties and reads the `[AgentContext]` description
- `GetProperty` — Read property value
- `SetProperty` / `PatchProperties` — write property values (subject to `ComponentPatcher` security rules)

### 6.2 `[VeloxCommand]` — Commands invokable by Agent

```csharp
[VeloxCommand]                               // ← Generates IVeloxCommand
[AgentContext(AgentLanguages.English, "Starts the workflow: broadcasts SeedPayload")]
private async Task OpenWorkflow(object? parameters, CancellationToken ct)
{
    // ...
}
```

**Agent tool dependencies:**
- `ListCommands` — List all commands and description of `[AgentContext]`
- `ExecuteCommand` — call command (parameters specify type via `[AgentCommandParameter]`)

### 6.3 Commands with Verification

```csharp
[VeloxCommand(canValidate: true)]
private Task Minus(object? sender, CancellationToken ct)
{
    Index--;
    return Task.CompletedTask;
}

// Must implement this partial method:
private partial bool CanExecuteMinusCommand(object? parameter) => _index > 0;
```

The Agent checks `CanExecute` before invoking a command; if it returns false, the command will not be executed.

---

## 7. Complete Example: Building a Component That AI Can Understand

Below is a complete, best-practice component definition:

```csharp
using VeloxDev.AI;
using VeloxDev.MVVM;
using VeloxDev.WorkflowSystem;

namespace MyWorkflow.Nodes;

// ── 1. Enum definitions ──
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

// ── 2. Data protocol ──
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

// ── 3. Interface definition ──
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

// ── 4. Implementation class ──
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
        // ... implementation logic
    }
}
```

**When the Agent encounters this component, it obtains the following information:**

| Information | Source |
|------|------|
| "This is a data source node" | `[AgentContext]` of interfaces and classes |
| "Default size 300×200" | Description text of class `[AgentContext]` |
| "Can choose HttpApi/FileSystem/MemoryBuffer" | `DataSourceType` enum + `[AgentContext]` of each field |
| "Has Fetch command, parameter is DataSourceConfig" | `FetchCommand` + `[AgentCommandParameter]` |
| "DataSourceConfig has Source and PollingIntervalMs" | `[VeloxProperty]` properties of `DataSourceConfig` class |
| "What are Api/FileSystem/MemoryBuffer respectively?" | `[AgentContext]` of enum fields |

---

## Best Practices Summary

| Principle | Explanation | Example |
|------|------|------|
| **Bilingual Annotation** | Label both Chinese and English simultaneously | `[AgentContext(AgentLanguages.Chinese, "...")]` + `[AgentContext(AgentLanguages.English, "...")]` |
| **Embed Default Values** | Include default sizes, positions, etc. in the description | `"Default size 300×200"`、`"Default size: 320×260"` |
| **Embed Constraints** | Include disallowed values in the description | `"Never use Size(0,0)"` |
| **Specific Rather than Abstract** | Describe concrete behavior rather than abstract concepts | ❌ `"Process data"` → ✅ `"Get data from HTTP API and broadcast to output"` |
| **Annotate Enum Fields** | Each field of the enum should be annotated | Let the Agent understand the semantics of each option |
| **Coordinate with CommandParameter** | Commands must annotate the parameter type | `[AgentCommandParameter(typeof(MyProtocol))]` |
| **Consistent Language** | Descriptions in the same language for the same element should maintain consistent style | |

---