# VeloxDev.Core

`VeloxDev.Core` 是 VeloxDev 的基础包，负责提供整个框架共享的抽象、生成能力与可复用基础设施。

它主要面向两类场景：

- 你希望直接使用 VeloxDev 中与平台无关的能力
- 你希望基于核心抽象实现自己的平台适配层或扩展模块

## 包含的能力

- `MVVM`：基于特性的通知属性与命令生成
- `Workflow`：工作流树、节点、插槽、连线及其辅助模板
- `AI`：通用的 Agent 反射基础设施——语义上下文标注、属性读写、方法调用、命令发现执行、类型解析（零第三方依赖，适用于所有 .NET 项目）
- `TransitionSystem`：动画状态、属性路径、插值器、调度与播放抽象
- `DynamicTheme`：主题注册、缓存、切换与渐变切换抽象
- `MonoBehaviour`：按帧驱动的生命周期模型
- `AOT Reflection`：面向 AOT / 裁剪场景的反射保留支持
- `AOP`：支持目标框架下的切面代理基础能力

## 什么时候只安装 `VeloxDev.Core`

适合以下情况：

- 你只需要 `MVVM`、`Workflow`、`AI`、`MonoBehaviour`、`AOT Reflection` 等平台无关能力
- 你希望理解并直接构建在核心抽象之上
- 你打算自行实现 UI 平台适配层

## 什么时候还需要扩展包

| 场景 | 所需包 |
|------|--------|
| 动画运行时、带动画的主题切换、View 侧交互 | 平台适配包（`VeloxDev.WPF` / `Avalonia` / `WinUI` / `MAUI` / `WinForms`） |
| Agent 以 MAF `AITool` 操作任意对象（属性/命令/方法） | `VeloxDev.Core.Extension`（提供 `AgentObjectToolkit`） |
| Agent 接管工作流（30+ 个 MAF Function） | `VeloxDev.Core.Extension`（提供 `WorkflowAgentToolkit`） |
| JSON 序列化 ViewModel | `VeloxDev.Core.Extension`（提供 `ComponentModelEx`） |

## 仓库与示例

- GitHub: https://github.com/Axvser/VeloxDev
- Wiki: https://axvser.github.io/VeloxDev.Wiki/
- Examples: 请参考仓库中的 `Examples` 目录

---

## AI — Agent 反射基础设施

`VeloxDev.Core.AI` 提供了一组零第三方依赖的通用反射工具，适用于任何 .NET 项目中的 Agent 场景——不限于工作流框架。

### 标注特性

```csharp
// 为任何类型或成员标注面向 Agent 的语义描述（支持多语言、多条）
[AgentContext(AgentLanguages.Chinese, "派生的Node组件之一，作为任务执行者，默认大小为 200*100")]
public partial class NodeViewModel
{
    [AgentContext(AgentLanguages.Chinese, "标题")]
    [VeloxProperty] private string title = "Workflow Step";
}

// 标注命令所期望的参数类型
[AgentCommandParameter(typeof(MoveNodeProtocol))]
ICommand MoveCommand { get; }
```

### 通用反射工具

| 类 | API | 说明 |
|---|---|---|
| `AgentPropertyAccessor` | `DiscoverProperties` / `GetPropertyValue` / `SetPropertyValue` / `SetProperties` / `CopyScalarProperties` | 反射属性读写，支持批量 Patch、类型转换、黑名单过滤 |
| `AgentMethodInvoker` | `DiscoverMethods` / `Invoke` / `InvokeStatic` | 反射方法调用，支持重载解析、参数默认值填充 |
| `AgentCommandDiscoverer` | `DiscoverCommands` / `Execute` / `CanExecuteCommand` / `FindBackingCommand` | ICommand 发现与执行，读取 `[AgentCommandParameter]` 和 `[AgentContext]` |
| `AgentContextReader` | `GetContexts(Type)` / `GetContexts(MemberInfo)` / `HasAgentContext` | 读取 `[AgentContext]` 语义上下文 |
| `AgentTypeResolver` | `ResolveType(string)` | 按全名在所有已加载程序集中查找类型 |

### 仅使用 Core（不依赖 Extension）

```csharp
// 直接使用静态方法——不需要 MAF、不需要 JSON 库
var props = AgentPropertyAccessor.DiscoverProperties(myViewModel, AgentLanguages.Chinese);
AgentPropertyAccessor.SetPropertyValue(myViewModel, "Title", "新标题");

var commands = AgentCommandDiscoverer.DiscoverCommands(myViewModel, AgentLanguages.Chinese);
AgentCommandDiscoverer.Execute(myViewModel, "Delete");

AgentMethodInvoker.Invoke(myService, "ProcessData", 42, "hello");

var descriptions = AgentContextReader.GetContexts(typeof(NodeViewModel), AgentLanguages.Chinese);
// → ["派生的Node组件之一，作为任务执行者，默认大小为 200*100"]

var type = AgentTypeResolver.ResolveType("Demo.ViewModels.NodeViewModel");
```

### 生成 MAF `AITool`（需要 Extension）

安装 `VeloxDev.Core.Extension` 后，可以将任意对象一行代码包装为 10 个 MAF 工具：

```csharp
// 方式一：扩展方法
var tools = myViewModel.AsAgentTools(AgentLanguages.Chinese);

// 方式二：带黑名单
var rejected = new HashSet<string> { "Parent", "RuntimeId" };
var toolkit = myViewModel.AsAgentToolkit(AgentLanguages.Chinese, rejected);
var tools = toolkit.CreateTools();

// 传入 Agent
var agent = client.AsAIAgent(
    instructions: "你是一个操作 ViewModel 的助手。",
    tools: tools);
```

生成的 10 个工具：

| 工具 | 说明 |
|------|------|
| `GetComponentInfo` | 获取类型名和 `[AgentContext]` 描述 |
| `ListProperties` | 列出所有属性（含类型、读写状态、当前值、描述） |
| `GetProperty` | 读取单个属性值 |
| `SetProperty` | 设置单个属性 |
| `PatchProperties` | JSON 批量修改属性 |
| `ListCommands` | 列出所有 ICommand（含参数类型、CanExecute、描述） |
| `ExecuteCommand` | 执行命名命令 |
| `ListMethods` | 列出所有公共方法 |
| `InvokeMethod` | 调用命名方法 |
| `ResolveType` | 按全名解析 .NET 类型 |

---

## Workflow Agent

### 语义上下文标注

```csharp
[AgentContext(AgentLanguages.Chinese, "派生的Node组件之一，作为任务执行者，默认大小为 200*100")]
[WorkflowBuilder.Node<HttpHelper<NodeViewModel>>(workSemaphore: 5)]
public partial class NodeViewModel
{
    public NodeViewModel() => InitializeWorkflow();

    [AgentContext(AgentLanguages.Chinese, "输入口")]
    [VeloxProperty] public partial SlotViewModel InputSlot { get; set; }

    [AgentContext(AgentLanguages.Chinese, "输出口")]
    [VeloxProperty] public partial SlotViewModel OutputSlot { get; set; }

    [AgentContext(AgentLanguages.Chinese, "标题")]
    [VeloxProperty] private string title = "Workflow Step";
}
```

### 运行时接管（需要 Extension，30+ 个 MAF Function）

```csharp
var scope = tree.AsAgentScope()
    .WithComponents(AgentLanguages.Chinese, typeof(NodeViewModel), typeof(SlotViewModel), ...)
    .WithMaxToolCalls(50);

// 渐进式 Prompt：预加载组件描述（含默认尺寸），Agent 从第一轮就拥有完整知识
var prompt = scope.ProvideProgressiveContextPrompt(AgentLanguages.Chinese);
var tools = scope.ProvideTools();

var agent = client.AsAIAgent(instructions: prompt, tools: tools);
```

> 完整的工作流 Agent 架构、安全机制与 AITool 列表请参阅 [`VeloxDev.Core.Extension` README](../VeloxDev.Core.Extension/README.md)。
