---
name: veloxdev-drive-workflow-with-ai
description: Give an LLM control of a VeloxDev workflow graph — wire a WorkflowAgentScope onto a tree, hand the tools to an IChatClient through one context provider, set the host's policy gates and call budgets, document components for the model with [AgentContext], load switchable skills (embedded documents or Agent-Skills folders on disk), and connect external MCP servers — including using the MCP or skill subsystem on its own, without the workflow layer
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
    .WithAllowNodeExecution(true)
    .WithSkills("skills")                             // switchable skills: embedded + a disk root
    .WithMcps(mcp);                                   // MCP servers join the tool set per turn

var agent = chatClient.AsAIAgent(new ChatClientAgentOptions
{
    ChatOptions = new ChatOptions { Instructions = scope.ProvideProgressiveContextPrompt() },
    AIContextProviders = scope.CreateContextProviders(),
});

var response = await agent.RunAsync(message, session);
```

⚙ **`WithSynchronizationContext` is not optional.** Workflow components are UI-bound and every tool call is marshalled through it. Call it while on the UI thread; without it, tool calls touch the graph from a thread pool thread.

⚙ **The whole call stays on that context — including everything after an `await`.** The wrapper posts the call, and a posted callback resumes on the context, so a tool's post-await work (verifying a connection, the second node of an `ExecuteNodes` batch, the execution engine driving a compiled chain) runs where the components live. This is why the tool bodies carry no `ConfigureAwait(false)`: adding one silently moves exactly that work onto a thread-pool thread. It applies to your own `WithTools` / `WithQueryTools` tools too — they get the same wrapper.

⚙ **The prompt is not rendered on that context.** The agent invocation thread builds each turn's instructions and tool list, so anything read there must be thread-safe. That is what `McpStatusViewModel.Snapshot` and `SkillsViewModel.Snapshot` are for: immutable copies republished on the bound thread whenever the collection changes. Read `Servers` / `Skills` from your UI, and the snapshots from anywhere else.

⚙ **The context provider is the single source of tools, and it re-renders per turn.** `CreateContextProviders()` returns a `WorkflowAgentContextProvider` that contributes the current skill text and the current tool set — built-in, custom, and every connected MCP server's — on each invocation. That is what lets a skill switched off, a tool registered late, or a server loaded mid-session reach the model on the next turn without rebuilding the agent.

⚙ **Never also pass the tools through `ChatOptions.Tools`.** MAF unions that list with the providers' contribution and does not deduplicate by name, so a tool offered through both channels is sent to the model twice.

⚙ **`ProvideTools()` still exists, but it is a one-shot snapshot** for hosts that build the agent by hand — it does not pick up later registrations. `CreateToolkit()` returns one instance per scope, so the call counters and the snapshot history behind `GetChangesSinceSnapshot` stay consistent.

⚙ **`WithAutoDiscovery` is how the model learns your node types.** `ListCreatableTypes` and `CreateNode` can only offer what was registered — by assembly name, by explicit type, or through `WithEnums` / `WithInterfaces` / `WithComponents` / `WithData`.

## MCP and Skills without the workflow layer

Neither subsystem needs a tree. Each exports a context provider that contributes its own prompt text and its own tools, so a host that only wants MCP — or only wants skills — attaches one provider and is done:

```csharp
var mcp = new McpScope()
    .WithSynchronizationContext(SynchronizationContext.Current)
    .WithServers(config);                       // what the Agent may load by name
var agent = chatClient.AsAIAgent([mcp.CreateContextProvider()]);

var skills = new SkillScope().WithSkillRoot("skills");
skills.Refresh();
var agent2 = chatClient.AsAIAgent([skills.CreateContextProvider()]);
```

The workflow layer is a composition of exactly these: `scope.CreateContextProviders()` returns `[workflow, skills, mcp]`, and each subsystem provider is handed the **workflow scope's own** tool policy.

⚙ **The policy is passed in by whoever composes, and that is the whole point.** Standalone, a subsystem is handed a thread-only policy — its tools are marshalled and nothing else. Composed into a workflow agent, it is handed that scope's policy, so its tools count against `MaxToolCalls` / `MaxReadToolCalls` / `MaxWriteToolCalls`, raise `ToolCalled`, and mark the tree dirty like any other. A subsystem never decides this for itself.

⚙ **Do not register a subsystem's management tools yourself.** The provider contributes them; registering them again through `WithTools` / `WithQueryTools` puts every one of them in the prompt twice, because the framework unions tool lists without deduplicating by name. That is also why `AsAIAgent(providers, instructions)` has no tools parameter.

⚙ **The MCP management tools' prompt text follows the self-service level.** It is generated, not fixed: at `Closed` it says the model cannot add a server, and at the higher rungs it describes what `AddMcpServer` will and will not do. Changing the level re-renders on the next turn.

⚙ **A skill provider's render is keyed on the scope's version *and* its prompt language.** The language is not part of the version, so `scope.WithPromptLanguage(...)` has to reach `SkillScope` for a language change to take effect — the workflow scope propagates it for you, and a standalone host sets it with `SkillScope.WithPromptLanguage`.

⚙ **Two providers built from one scope share a session-state key, and the framework rejects that at agent construction.** That is deliberate — the alternative is both contributing everything twice, silently. Build one provider per scope and share the scope instead.

## Policy — off by default

| Gate | Default | Unlocks |
|---|---|---|
| `WithAllowNodeExecution(bool)` | **off** | `ExecuteNode`, `ExecuteNodes`, `BroadcastNode`, `ReverseBroadcastNode`, `RunCompiledWorkflow`, `GetNodeResult` |
| `WithAllowedGenericCommands(params string[])` | **empty** | `ExecuteCommandOnNode`, `ExecuteCommandById` — and it *narrows* them to the listed names |
| `WithSelectionHandler` / `WithConfirmationHandler` | **null** | registers `RequestSelection` / `RequestConfirmation` |
| `WithInteractionSafety(0…3)` | `1` | 0 Silent · 1 Cautious · 2 Balanced · 3 Strict |
| `WithAutoMarkDirty(bool)` | off | marks the tree dirty after every non-query tool call, instead of requiring one `MarkDirty` at the end |
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

## Skills

`WithSkills(rootPath)` puts skills under dynamic management: the library's own embedded prompt documents plus every skill folder found under `rootPath`. Each becomes a bindable row in `scope.Skills.Status.Skills` that the host **and the agent** can switch on and off, and the change reaches the model on the next turn.

```csharp
scope.WithSkills("skills");    // relative paths resolve against AppContext.BaseDirectory
```

That is the whole wiring: the skill context provider contributes the corpus *and* the four tools. They are `ListSkills`, `load_skill`, `UnloadSkill` and `read_skill_resource`.

⚙ **They are classified as query tools wherever they come from** — contributed by the provider, they are charged to `MaxReadToolCalls` and never mark the tree dirty, because switching a skill changes what the model is shown and never the graph. That classification used to depend on the host registering them through `WithQueryTools`; it is now taken from `SkillAgentToolkit.ToolNames`, so it holds either way.

⚙ **Two injection shapes, chosen by origin, and not interchangeable.** Embedded documents are architectural prompt text: when enabled, their body is injected **in full**. Disk skills follow the Agent Skills convention: only `name` and `description` are advertised, and the body arrives through `load_skill`. That is what keeps a large external skill library from costing every turn.

⚙ **A disk skill is `<root>/<name>/SKILL.md` with `---` frontmatter carrying `name` and `description`** — the same layout as a Claude Skill. The directory name must equal the frontmatter `name` (lowercase kebab-case, ≤ 64 chars; description ≤ 1024). A `references/` folder beside the `SKILL.md` is counted but not read until `read_skill_resource` asks for one, and resource paths are confined to the skill directory.

⚙ **A malformed skill is reported, not swallowed and not fatal.** It appears in the list with `State == Error`, an `Error` message, and `IsActive == false`, so one broken folder cannot take the others down or silently vanish. Check `scope.Skills.Status.ErrorCount`.

⚙ **All skills start enabled,** so adding a root never silently empties the prompt. `Refresh()` rediscovers and preserves the flags — call it when a root's contents changed; do **not** call it per turn, since it always advances `Skills.Version` and invalidates the provider's render.

⚙ **With skills attached, the static prompt no longer carries the corpus.** `ProvideProgressiveContextPrompt()` omits it and the provider renders it instead; leaving it in both would send every document twice.

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

⚙ **Attach the MCP scope with `WithMcps` so its tools are wrapped like every other tool.** Reached that way, a connected server's tools are counted against `MaxToolCalls`, marshalled onto your synchronization context, and raise `ToolCalled`. If you instead merge `mcp.LoadedTools` into `ChatOptions.Tools` by hand, none of that happens — and they will also duplicate, because the provider already offers them.

⚙ **By default the model can only load, unload and inspect pre-registered servers.** It cannot author a configuration — that is the boundary `McpSelfServiceLevel.Closed` draws, and the default. `WithSelfService(level)` opens it in rungs: `RemoteConfirmed` lets it add **remote (Http)** servers with your confirmation; `AllConfirmed` adds local ones too, each still confirmed; `Unrestricted` stops asking. At the two middle rungs the prompt cannot be answered when no confirmation handler is registered, and an unanswerable prompt **denies**.

⚙ **A local rung means the model can cause a package to be installed and launched on the machine.** That is what separates `RemoteConfirmed` from `AllConfirmed`; pick the rung deliberately.

Details and the full option surface are in [references/mcp.md](references/mcp.md).

## Checking your wiring

⚙ **Start by asking the agent to orient itself** — `GetWorkflowSummary`, then `GetFullTopology`. If those come back empty or error, the scope is not bound to the tree; if they work but mutations do not, it is `WithSynchronizationContext` or the auto-discovery list.

⚙ **Try a mutation the agent should be allowed to make and one it should not.** With node execution off, asking it to run a chain should come back `disabled by host policy` — that reply is the confirmation your gates are wired, not a failure.

⚙ **Watch the undo stack.** Every structural tool call the agent makes should appear as one entry. If nothing lands there, the agent is reaching a code path you added yourself rather than the library's commands.

⚙ `Examples/Workflow/Common/Lib/ViewModels/Workflow/Helper/AgentHelper.cs` is a complete working host — scope setup, an MCP panel, per-conversation tool assembly and the safety-prompt wiring. Read it before writing your own, whether from NuGet or from a checkout.

⚙ The prompts that ship to the model are under `Src/Core/VeloxDev.Core.Extension/Resources/Workflow/{en,zh}/` — seven `Skills/`, five `References/` and four `Safety/` documents, selected by `ProvideProgressiveContextPrompt(language)`. Read them to know what the model has already been told **before** you add to it, so your instructions do not fight the built-in ones.
