---
name: veloxdev-drive-workflow-with-ai
description: Let an LLM inspect, build, wire and run a VeloxDev workflow graph — wire a WorkflowAgentScope onto a tree, hand the tools to an IChatClient, set the host's policy gates and call budgets, document components for the model with [AgentContext], and connect external MCP servers
---

## Responsibility

Turn a workflow `Tree` into a **tool surface** an LLM can operate: list and inspect nodes, create and delete them, connect slots, patch properties, run a compiled chain or reverse-compute one node's result — through the *same* undoable commands the GUI uses.

Package: **`VeloxDev.Core.Extension`**. It also carries the graph serializer, which is a separate reason to reference it.

## The wiring

```csharp
var scope = tree.AsAgentScope()                       // tree : IWorkflowTreeViewModel
    .WithPromptLanguage(AgentLanguages.English)
    .WithOutputLanguage(AgentLanguages.Chinese)
    .WithAutoDiscovery(assemblyName: "MyLib")         // find the concrete node/slot types
    .WithSynchronizationContext(SynchronizationContext.Current)
    .WithAllowNodeExecution(true);

var agent = chatClient.AsAIAgent(instructions: scope.ProvideProgressiveContextPrompt());

var response = await agent.RunAsync(message, session, new ChatClientAgentRunOptions
{
    ChatOptions = new ChatOptions { Tools = [.. scope.ProvideTools(), .. mcp.LoadedTools] }
});
```

⚙ **`WithSynchronizationContext` is not optional.** Workflow components are UI-bound and every tool call is marshalled through it. Call it while on the UI thread; without it, tool calls touch the graph from a thread pool thread.

⚙ **`ProvideTools()` is computed once and reused.** Rebuilding it per turn re-wraps every function. `Examples/Workflow/Common/Lib/ViewModels/Workflow/Helper/AgentHelper.cs` caches it as `_baseTools` and combines it with the MCP set per conversation.

⚙ **`WithAutoDiscovery` is how the model learns your node types.** `ListCreatableTypes` and `CreateNode` can only offer what was registered — by assembly name, by explicit type, or through `WithEnums` / `WithInterfaces` / `WithComponents` / `WithData`.

## Policy — off by default

| Gate | Default | Unlocks |
|---|---|---|
| `WithAllowNodeExecution(bool)` | **off** | `ExecuteNode`, `ExecuteNodes`, `BroadcastNode`, `ReverseBroadcastNode`, `RunCompiledWorkflow`, `GetNodeResult` |
| `WithAllowedGenericCommands(params string[])` | **empty** | `ExecuteCommandOnNode`, `ExecuteCommandById` — and it *narrows* them to the listed names |
| `WithSelectionHandler` / `WithConfirmationHandler` | **null** | registers `RequestSelection` / `RequestConfirmation` |
| `WithInteractionSafety(0…3)` | `1` | 0 Silent · 1 Cautious · 2 Balanced · 3 Strict |
| `WithAutoMarkDirty(bool)` | off | re-indexes after every mutating tool call, instead of requiring one `MarkDirty` at the end |
| `WithMaxToolCalls` / `WithMaxReadToolCalls` / `WithMaxWriteToolCalls` | unlimited | pre-flight caps; exceeding one returns an error object rather than throwing |

⚙ **`WithInteractionSafety` is prompt-only.** Levels 1–3 inject a safety policy into the system prompt and nothing else — no tool body consults the handler. A host that sets level 3 and believes destructive calls are gated is mistaken; gate them in `WithAllowedGenericCommands` or in your own handler.

⚙ **A session confirmation is keyed by a string the model supplies.** Once an operation key is approved, any later call reusing that key is auto-approved. Treat it as a UX affordance, not a security boundary.

⚙ **`ClearHistory` drops the undo trail without touching the canvas.** After it, the agent cannot undo its own earlier work — the canvas is the state, the history is not.

## What the model gets

62 tools in ten categories, all documented in [references/tools.md](references/tools.md). The shape worth knowing up front:

- **Query** (all read-only) — `GetWorkflowSummary`, `GetFullTopology`, `ListNodes`, `GetNodeDetail`, `FindNodes`, `ResolveSlotId`, `ListSlotProperties`, `GetTypeSchema`, `GetComponentContext`, `ValidateWorkflow`, `CompileWorkflow`, `CompileNodeResult`, …
- **Mutation** — `CreateNode`, `DeleteNode`, `ConnectSlots` / `ConnectByProperty`, `DisconnectSlots`, `SetSlotChannel`, `PatchNodeProperties`, `SetEnumSlotCollection`, `Undo` / `Redo`, …
- **Execution** — node-level, chain-level (`RunCompiledWorkflow`) and result-level (`GetNodeResult`).
- **Interaction** — `RequestSelection`, `RequestConfirmation`, registered only when the host wired the matching handler.

⚙ **Indices are unstable; ids are not.** Creating or deleting shifts node indices, and a selector change rebuilds a slot enumerator's slots. The shipped prompt tells the model to prefer `GetFullTopology`, `ResolveSlotId` and `ConnectByProperty`. Any custom prompt you write should say the same.

⚙ **Some mutations are deliberately not undoable**: `MoveNode` / `SetNodePosition` / `ResizeNode` (they mirror the GUI drag, which writes continuously) and `PatchNodeProperties` (direct property writes). Everything structural — create, delete, connect, disconnect, `SetSelector` — is.

⚙ **Unmounted components are rejected, and operating on one elsewhere is a silent no-op.** A node must be in a tree before a tool can reach it.

## Documenting your own components

```csharp
[AgentContext(AgentLanguages.English, "Input slot (receiver)")]
[AgentContext(AgentLanguages.Chinese, "输入槽（接收方）")]
[VeloxProperty] public partial MySlotViewModel InputSlot { get; set; }
```

`[AgentContext]` is `AllowMultiple` — decorate once per language. It goes on classes, enums, enum members, interfaces, properties, backing fields and commands; `[AgentCommandParameter(typeof(T))]` documents a command's parameter type.

⚙ **It changes nothing at runtime.** It is documentation that the context collector renders into the prompt — a markdown table — and `GetTypeSchema` re-injects as `developerInstructions`.

⚙ **Class-level contexts are marked authoritative** in the prompt. Use them for the rules a model cannot infer from a signature: what a value means, what a valid combination is, what a tool rejects.

⚙ `[SlotSelectors]` properties cannot be patched — the patcher refuses them and points at `SetEnumSlotCollection` instead.

## MCP

```csharp
var mcp = new McpScope()
    .WithMcpRoot(".evn/mcp")
    .WithSynchronizationContext(SynchronizationContext.Current);

var configs = new[]
{
    new McpServerConfiguration                                   // local stdio via npx
    {
        Name = "Filesystem", RunMode = McpServerRunMode.Npx,
        Package = "@modelcontextprotocol/server-filesystem", Arguments = ["C:/data"],
        Options = new { env = new { FILESYSTEM_ROOT = "C:/data" } },
    },
    new McpServerConfiguration                                   // remote Streamable HTTP
    {
        Name = "Microsoft Learn", RunMode = McpServerRunMode.Http,
        Endpoint = "https://learn.microsoft.com/api/mcp",
        Options = new { connectionTimeout = 30,
                        headers = new { Authorization = "Bearer <token>" } },
    },
};

var mcpTools = await mcp.LoadAsync(configs);
```

`RunMode` is `Npm` · `Npx` · `Uvx` · `Pip` · `Dotnet` · `Exe` · `Http`. Local modes install into `<root>/{node,python,dotnet,exe}` idempotently; HTTP uses Streamable HTTP with an SSE fallback.

⚙ **One failing server does not fail the batch.** A bad config sets that server's state to `Error`, raises `ServerError`, and returns an empty tool set; the rest load. Inspect `mcp.Status` (a bindable `McpStatusViewModel` with `Servers`, `ConnectedCount`, `ErrorCount`, `IsAllReady`).

⚙ **Unknown `Options` keys throw** rather than being ignored — `env` and `workingDirectory` on stdio, `headers` / `oauth` / `connectionTimeout` / `transportMode` / `ownsSession` on HTTP.

⚙ **MCP server tools bypass the tracking wrapper.** They are merged into `ChatOptions.Tools` directly, so they are **not** counted against `MaxToolCalls`, not marshalled onto your synchronization context, and do not raise `ToolCalled`. Only the four MCP *management* tools (`ListMcpServers`, `LoadMcpServers`, `UnloadMcpServer`, `DescribeMcpServer`) go through `scope.WithTools(...)` and get all three.

⚙ **Configs are fixed at load time; the model can only load, unload and inspect.** It cannot construct or reconfigure a server — that is the security boundary, and it is worth keeping.

Details and the full option surface are in [references/mcp.md](references/mcp.md).

## Checking your wiring

⚙ **Start by asking the agent to orient itself** — `GetWorkflowSummary`, then `GetFullTopology`. If those come back empty or error, the scope is not bound to the tree; if they work but mutations do not, it is `WithSynchronizationContext` or the auto-discovery list.

⚙ **Try a mutation the agent should be allowed to make and one it should not.** With node execution off, asking it to run a chain should come back `disabled by host policy` — that reply is the confirmation your gates are wired, not a failure.

⚙ **Watch the undo stack.** Every structural tool call the agent makes should appear as one entry. If nothing lands there, the agent is reaching a code path you added yourself rather than the library's commands.

⚙ `Examples/Workflow/Common/Lib/ViewModels/Workflow/Helper/AgentHelper.cs` is a complete working host — scope setup, an MCP panel, per-conversation tool assembly and the safety-prompt wiring. Read it before writing your own, whether from NuGet or from a checkout.

⚙ The prompts that ship to the model are under `Src/Core/VeloxDev.Core.Extension/Resources/Workflow/{en,zh}/` — seven `Skills/`, five `References/` and four `Safety/` documents, selected by `ProvideProgressiveContextPrompt(language)`. Read them to know what the model has already been told **before** you add to it, so your instructions do not fight the built-in ones.
