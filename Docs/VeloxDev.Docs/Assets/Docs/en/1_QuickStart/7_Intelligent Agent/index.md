# Intelligent Agent

Bind an AI assistant to your workflow — the agent can create/connect nodes and modify properties via natural language.

---

## Step 1 — Install

```shell
dotnet add package VeloxDev.Core.Extension
```

## Step 2 — Create a Console App with the Agent

```shell
dotnet new console -n MyAgentDemo
cd MyAgentDemo
dotnet add package VeloxDev.Core.Extension
```

## Step 3 — Paste into `Program.cs`

```csharp
using VeloxDev.AI;
using VeloxDev.AI.Workflow;
using VeloxDev.MVVM;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.Compilation;

// ── Build a minimal workflow tree ──────────────────────────────────

var tree = new TreeDefaultViewModel();
var ctrl = new ControllerNode();
tree.Nodes.Add(ctrl);

// ── Create an agent scope from the tree ────────────────────────────

var scope = tree.AsAgentScope()
    .WithPromptLanguage(AgentLanguages.Chinese)
    .WithAutoDiscovery("VeloxDev.Core")
    .WithMaxToolCalls(200);

// ── Get the progressive context prompt and tool definitions ────────

var contextPrompt = scope.ProvideProgressiveContextPrompt();
var tools = scope.ProvideTools();

// ── Bind to an LLM (OpenAI-compatible API, e.g. DeepSeek, Azure) ──

var apiKey = Environment.GetEnvironmentVariable("AI_API_KEY")
    ?? throw new InvalidOperationException("Set AI_API_KEY");

var chatClient = new OpenAI.OpenAIClient(
    new OpenAI.ApiKeyCredential(apiKey),
    new OpenAI.OpenAIClientOptions { Endpoint = new Uri("https://api.deepseek.com") }
).GetChatClient("deepseek-chat")
 .AsIChatClient();

var agent = chatClient.AsAIAgent(instructions: contextPrompt, tools: tools);

// ── Let the agent talk to the workflow ─────────────────────────────

Console.Write("You: ");
var userMessage = Console.ReadLine() ?? "添加一个节点";

var response = await agent.RunAsync(userMessage);
Console.WriteLine($"Agent: {response?.Text}");

// ── Supporting node ViewModel ──────────────────────────────────────

public partial class ControllerNode : NodeDefaultViewModel
{
    public ControllerNode() => InitializeWorkflow();
    [VeloxProperty] private string _seed = "demo";
}
```

## Step 4 — Set Your API Key and Run

```shell
set AI_API_KEY=sk-your-key-here
dotnet run
```

The agent can:
- Create/delete nodes
- Connect/disconnect slots  
- Modify `[VeloxProperty]` values
- Execute undo/redo
- Request confirmation before destructive actions

## Safety Levels

```csharp
scope.WithInteractionSafety(2); // 0=auto, 1=caution, 2=confirm, 3=strict
```
