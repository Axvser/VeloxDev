# 智能体

为工作流添加 AI 智能体 — 智能体可通过自然语言创建/连接节点、修改属性。

---

## 第一步 — 安装

```shell
dotnet add package VeloxDev.Core.Extension
```

## 第二步 — 粘贴到 `Program.cs`

```csharp
using VeloxDev.AI;
using VeloxDev.AI.Workflow;
using VeloxDev.MVVM;
using VeloxDev.WorkflowSystem;

var tree = new TreeViewModelBase();
var ctrl = new ControllerNode();
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
var userMessage = Console.ReadLine() ?? "添加一个节点";
var response = await agent.RunAsync(userMessage);
Console.WriteLine($"智能体：{response?.Text}");

// 节点定义
public partial class ControllerNode : NodeViewModelBase
{
    public ControllerNode() => InitializeWorkflow();
    [VeloxProperty] private string _seed = "demo";
}
```

## 第三步 — 设置 API Key 并运行

```shell
set AI_API_KEY=sk-your-key-here
dotnet run
```

智能体可以：
- 创建/删除节点
- 连接/断开插槽
- 修改 `[VeloxProperty]` 值
- 执行撤销/重做
- 破坏性操作前请求用户确认
