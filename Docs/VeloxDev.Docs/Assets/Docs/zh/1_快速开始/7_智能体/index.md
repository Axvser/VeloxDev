# 智能体

为工作流中的组件标记 AI 上下文，再创建智能体——通过**自然语言**操控工作流。

---

## Demo 效果

```
你：添加一个节点并连接到控制器
智能体：完成！已创建 NodeX 并连接到 ControllerNode 的默认输出口
```

智能体通过你标注的 `[AgentContext]`、`[AgentCommandParameter]`、`[SlotSelectors]` 等特性理解工作流结构，自动调用工具完成操作。

## 一、在工作流组件中标注 AI 上下文

`[AgentContext]` 可以为任意组件、属性、方法附加 AI 描述，支持多语言：

```csharp
using VeloxDev.AI;
using VeloxDev.MVVM;
using VeloxDev.WorkflowSystem;

[AgentContext(AgentLanguages.Chinese, "派生的Node组件之一，作为任务执行者")]
[AgentContext(AgentLanguages.English, "A derived Node component that acts as a task executor.")]
[WorkflowBuilder.Node<NodeHelper>]
public partial class MyNode
{
    public MyNode() => InitializeWorkflow();

    [AgentContext(AgentLanguages.Chinese, "输入口")]
    [AgentContext(AgentLanguages.English, "Input slot.")]
    [VeloxProperty] public partial SlotViewModel InputSlot { get; set; }

    [AgentContext(AgentLanguages.Chinese, "执行延迟毫秒数")]
    [AgentContext(AgentLanguages.English, "Simulated delay in ms.")]
    [VeloxProperty] private int delayMilliseconds = 1200;

    [AgentContext(AgentLanguages.Chinese, "启动处理")]
    [AgentContext(AgentLanguages.English, "Start processing.")]
    [VeloxCommand]
    private async Task Process(CancellationToken ct) { ... }
}
```

`[AgentCommandParameter]` 指定命令参数类型：

```csharp
[AgentContext(AgentLanguages.Chinese, "设置目标URL")]
[AgentCommandParameter(typeof(string))]
[VeloxCommand]
private async Task SetUrl(object? parameter, CancellationToken ct) { ... }
```

`[SlotSelectors]` 声明条件 Slot 的允许枚举类型：

```csharp
[SlotSelectors(typeof(MyEnum))]
[VeloxProperty] public partial SlotEnumerator<SlotViewModel> OutputSlots { get; set; }
```

## 二、安装并创建智能体

```shell
dotnet add package VeloxDev.Core.Extension
```

```csharp
using VeloxDev.AI;
using VeloxDev.AI.Workflow;
using VeloxDev.MVVM;
using VeloxDev.WorkflowSystem;

var tree = new TreeDefaultViewModel();
var ctrl = new MyNode();
tree.Nodes.Add(ctrl);

// 创建智能体作用域
var scope = tree.AsAgentScope()
    .WithPromptLanguage(AgentLanguages.Chinese)
    .WithAutoDiscovery("VeloxDev.Core")
    .WithMaxToolCalls(200);

// 获取上下文提示和工具定义
var contextPrompt = scope.ProvideProgressiveContextPrompt();
var tools = scope.ProvideTools();

// 绑定到 LLM（OpenAI 兼容 API）
var apiKey = Environment.GetEnvironmentVariable("AI_API_KEY")
    ?? throw new InvalidOperationException("请设置 AI_API_KEY");

var chatClient = new OpenAI.OpenAIClient(
    new OpenAI.ApiKeyCredential(apiKey),
    new OpenAI.OpenAIClientOptions { Endpoint = new Uri("https://api.deepseek.com") }
).GetChatClient("deepseek-chat")
 .AsIChatClient();

var agent = chatClient.AsAIAgent(instructions: contextPrompt, tools: tools);

Console.Write("你：");
var response = await agent.RunAsync(Console.ReadLine() ?? "添加一个节点");
Console.WriteLine($"智能体：{response?.Text}");
```

## 智能体能力一览

| 能力 | 所需标注 |
|------|---------|
| 创建/删除节点 | `[WorkflowBuilder.Node]`（自动发现） |
| 读写属性值 | `[VeloxProperty]` + `[AgentContext("描述")]` |
| 调用命令 | `[VeloxCommand]` + `[AgentContext("描述")]` |
| 连接/断开 Slot | `[AgentContext]` 描述 Slot 用途 |
| 条件 Slot 路由 | `[SlotSelectors(typeof(Enum))]` |
| 撤销/重做 | 自动（操作栈） |
| 安全确认 | 破坏性操作前请求用户批准 |

完整示例见 [Examples/Workflow/Common/Lib/](https://github.com/Axvser/VeloxDev/tree/master/Examples/Workflow/Common/Lib/)
