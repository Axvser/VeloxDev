# Intelligent Agent

Mark workflow components with AI context attributes, then create an agent — manipulate workflows via **natural language**.

---

## Demo

```
You: Add a node and connect it to the controller
Agent: Done! Created NodeX and connected to ControllerNode's default output slot
```

The agent understands the workflow structure through `[AgentContext]`, `[AgentCommandParameter]`, and `[SlotSelectors]` annotations.

## 1. Annotating Components with AI Context

`[AgentContext]` attaches descriptions to any component, property, or method — supports multiple languages:

```csharp
using VeloxDev.AI;
using VeloxDev.MVVM;
using VeloxDev.WorkflowSystem;

[AgentContext(AgentLanguages.English, "A derived Node component.")]
[AgentContext(AgentLanguages.Chinese, "派生的Node组件。")]
[WorkflowBuilder.Node<NodeHelper>]
public partial class MyNode
{
    public MyNode() => InitializeWorkflow();

    [AgentContext(AgentLanguages.English, "Input slot.")]
    [VeloxProperty] public partial SlotViewModel InputSlot { get; set; }

    [AgentContext(AgentLanguages.English, "Simulated delay in ms.")]
    [VeloxProperty] private int delayMilliseconds = 1200;
}
```

`[AgentCommandParameter]` specifies the parameter type for a command:

```csharp
[AgentCommandParameter(typeof(string))]
[VeloxCommand]
private async Task SetUrl(object? parameter, CancellationToken ct) { ... }
```

`[SlotSelectors]` declares allowed enum selector types for conditional slots:

```csharp
[SlotSelectors(typeof(MyEnum))]
[VeloxProperty] public partial SlotEnumerator<SlotViewModel> OutputSlots { get; set; }
```

## 2. Install & Create the Agent

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

var scope = tree.AsAgentScope()
    .WithPromptLanguage(AgentLanguages.English)
    .WithAutoDiscovery("VeloxDev.Core")
    .WithMaxToolCalls(200);

var contextPrompt = scope.ProvideProgressiveContextPrompt();
var tools = scope.ProvideTools();

// Bind to LLM (OpenAI-compatible API)
var apiKey = Environment.GetEnvironmentVariable("AI_API_KEY")
    ?? throw new InvalidOperationException("Set AI_API_KEY");

var chatClient = new OpenAI.OpenAIClient(
    new OpenAI.ApiKeyCredential(apiKey),
    new OpenAI.OpenAIClientOptions { Endpoint = new Uri("https://api.deepseek.com") }
).GetChatClient("deepseek-chat")
 .AsIChatClient();

var agent = chatClient.AsAIAgent(instructions: contextPrompt, tools: tools);

Console.Write("You: ");
var response = await agent.RunAsync(Console.ReadLine() ?? "Add a new node");
Console.WriteLine($"Agent: {response?.Text}");
```

## Agent Capabilities

| Capability | Required Annotation |
|-----------|-------------------|
| Create/Delete nodes | `[WorkflowBuilder.Node]` (auto-discovered) |
| Read/Write properties | `[VeloxProperty]` + `[AgentContext("description")]` |
| Invoke commands | `[VeloxCommand]` + `[AgentContext("description")]` |
| Connect/Disconnect slots | `[AgentContext]` describing slot purpose |
| Conditional slot routing | `[SlotSelectors(typeof(Enum))]` |
| Undo/Redo | Automatic (operation stack) |
| Safety confirmation | Built-in (Level 1+ requires confirmation) |

Full example: [Examples/Workflow/Common/Lib/](https://github.com/Axvser/VeloxDev/tree/master/Examples/Workflow/Common/Lib/)
