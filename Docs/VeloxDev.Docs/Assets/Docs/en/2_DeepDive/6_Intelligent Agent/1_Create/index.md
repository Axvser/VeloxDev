# Creating an Agent

Bind `WorkflowAgentScope` to any `IChatClient` (OpenAI, Azure OpenAI, DeepSeek, etc.) to create an intelligent agent that can manipulate workflows via natural language.

---

## End-to-End Setup

```csharp
var scope = tree.AsAgentScope()
	.WithPromptLanguage(AgentLanguages.English)
	.WithAutoDiscovery("VeloxDev.Core")
	.WithMaxToolCalls(200)
	.WithConfirmationLevel(ConfirmationLevel.Level1);

// Get context and tools
var contextPrompt = scope.ProvideProgressiveContextPrompt();
var tools = scope.ProvideTools();

// Bind to LLM
var apiKey = Environment.GetEnvironmentVariable("AI_API_KEY")
	?? throw new InvalidOperationException("Set AI_API_KEY");

var chatClient = new OpenAI.OpenAIClient(
	new OpenAI.ApiKeyCredential(apiKey),
	new OpenAI.OpenAIClientOptions { Endpoint = new Uri("https://api.deepseek.com") }
).GetChatClient("deepseek-chat")
 .AsIChatClient();

var agent = chatClient.AsAIAgent(
	instructions: contextPrompt,
	tools: tools);

// Execute
var response = await agent.RunAsync("Connect node A's output to node B's input");
```

## What the MAF Framework Provides Automatically

| Source Annotation | LLM Tool Exposure |
|------------------|-------------------|
| `[VeloxProperty]` | Read/write property tool |
| `[VeloxCommand]` | Callable tool with optional typed parameter |
| `[AgentContext]` | Human-readable tool descriptions |
| `[AgentCommandParameter]` | Typed parameter definitions |
| `[SlotSelectors]` | Constrained enum selector values |

## Key Scenarios

| Scenario | Code |
|----------|------|
| **Create & configure node** | `scope.ExecuteToolAsync("CreateAndConfigureNode", args)` |
| **Connect slots** | `scope.ExecuteToolAsync("ConnectSlots", args)` |
| **Patch properties** | `scope.ExecuteToolAsync("PatchNodeProperties", args)` |
| **List slot properties** | `scope.ExecuteToolAsync("ListSlotProperties", args)` |
| **Enumerate workflow state** | Automatically via `ProvideProgressiveContextPrompt()` |
