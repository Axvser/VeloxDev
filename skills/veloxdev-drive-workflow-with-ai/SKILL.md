---
name: veloxdev-drive-workflow-with-ai
description: Give an LLM control of a VeloxDev workflow graph — wire a WorkflowAgentScope onto a tree, hand the tools to an IChatClient through one context provider, set the host's policy gates and call budgets, document components for the model with [AgentContext], load switchable skills (embedded documents or Agent-Skills folders on disk), connect external MCP servers, and dispatch background sub-agents — including using the MCP or skill subsystem on its own, without the workflow layer. Use when connecting an LLM or agent to a workflow graph, or when adding MCP servers, agent skills or sub-agents.
---
## Responsibility

Turn a workflow `Tree` into a **tool surface** an LLM can operate: list and inspect nodes, create and delete them, connect slots, patch properties, run a compiled chain or reverse-compute one node's result — through the *same* undoable commands the GUI uses.

Package: **`VeloxDev.Core.Extension`** (`netstandard2.0;net8.0`). It also carries the compiled-graph and checkpoint helpers, which are a separate reason to reference it.

## The wiring

```csharp
var scope = tree.AsAgentScope()                       // tree : IWorkflowTreeViewModel
    .WithPromptLanguage(AgentLanguages.English)
    .WithOutputLanguage(AgentLanguages.Chinese)
    .WithAutoDiscovery()                              // register the concrete node/slot types
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

⚙ **The whole call stays on that context — including everything after an `await`.** The wrapper posts the call, and a posted callback resumes on the context, so a tool's post-await work (verifying a connection, the second node of an `ExecuteNodes` batch, the execution engine driving a compiled chain) runs where the components live. This is why the tool bodies carry no `ConfigureAwait(false)`: adding one silently moves exactly that work onto a thread-pool thread. It applies to your own `WithTools` / `WithQueryTools` tools too — an `AIFunction` gets the same wrapper (any other `AITool` type passes through untouched).

⚙ **The prompt is not rendered on that context.** The agent invocation thread builds each turn's instructions and tool list, so anything read there must be thread-safe. That is what `McpStatusViewModel.Snapshot` and `SkillsViewModel.Snapshot` are for: immutable copies republished on the bound thread whenever the collection changes. Read `Servers` / `Skills` from your UI, and the snapshots from anywhere else.

⚙ **The context provider is the single source of tools, and it re-renders per turn.** The scope's own provider (a `WorkflowAgentContextProvider`, from `CreateContextProvider()`) contributes the current tool set — the built-in and custom tools only — on each invocation, and the attached subsystems' providers contribute theirs beside it; a connected MCP server's tools come from the MCP provider, not from this one. That is what lets a skill switched off, a tool registered late, or a server loaded mid-session reach the model on the next turn without rebuilding the agent.

⚙ **Never also pass the tools through `ChatOptions.Tools`.** MAF unions that list with the providers' contribution and does not deduplicate by name, so a tool offered through both channels is sent to the model twice.

⚙ **`ProvideTools()` still exists, but it is a one-shot snapshot** for hosts that build the agent by hand — it does not pick up later registrations. `CreateToolkit()` returns one instance per scope, so the call counters and the snapshot history behind `GetChangesSinceSnapshot` stay consistent.

⚙ **`WithAutoDiscovery(AgentLanguages? language = null)` is how the model learns your node types.** It lists the compiled agent context tree's `Customer/` directories — components (filed by the four component interfaces), enums, interfaces and data types — and registers what the generator admitted for prompt rendering. There is no assembly scan behind it: a type nothing annotated, no component interface reaches and no member references is not in the tree. `ListCreatableTypes` and `CreateNode` resolve against that same compiled tree (framework and customer roots), independently of whether `WithAutoDiscovery` ran, so a name the tree does not carry cannot be created; `WithEnums` / `WithInterfaces` / `WithComponents` / `WithData` (each takes a `Type[]` and an optional `AgentLanguages?`) only add its context block to the prompt.

⚙ **Customer context and framework context take their language differently.** `ProvideCustomerContext()` and `ProvideCustomerDataContext()` take no argument — they render each registered type in the language it was registered under. `ProvideFrameworkContext(language)` and `ProvideFrameworkDataContext(language)` take one.

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

The workflow layer is a composition of exactly these: `scope.CreateContextProviders()` returns the compaction provider first when the host asked for one, then the scope's own provider, then one per attached subsystem — skills, MCP and sub-agents — then the framework-native todo and agent-mode providers, then any factory registered with `WithContextProvider`. Each subsystem provider is handed the **workflow scope's own** tool policy.

⚙ **The policy is passed in by whoever composes, and that is the whole point.** Standalone, a subsystem is handed a thread-only policy — its tools are marshalled and nothing else. Composed into a workflow agent, it is handed that scope's policy, so its tools count against `MaxToolCalls` / `MaxReadToolCalls` / `MaxWriteToolCalls`, raise `ToolCalled`, and mark the tree dirty like any other. A subsystem never decides this for itself.

⚙ **Do not register a subsystem's management tools yourself.** The provider contributes them; registering them again through `WithTools` / `WithQueryTools` puts every one of them in the prompt twice, because the framework unions tool lists without deduplicating by name. That is also why `AsAIAgent(providers, instructions)` has no tools parameter.

⚙ **The MCP management tools that change the server set are always registered on a normal scope; the session's permission mode decides whether they run.** `AddMcpServer` and `SetMcpServerArguments` used to appear only on a rung of a ladder of their own, which left a model able to say only that it had no such tool — nothing the user could act on. Now a refused call names the mode or the rule that refused it. The one question a rule about a *tool* cannot answer is which **kind** of server to add, so the tool asks the same policy about a synthesised call named `mcp-add:local` / `mcp-add:http`: a local package installs and runs software, an Http endpoint does not, and a host forbids one without forbidding the other by writing `WithPermissionRule(Deny, "mcp-add:local")`.

⚙ **A skill provider's render is keyed on the scope's version *and* its prompt language.** The language is not part of the version, so `scope.WithPromptLanguage(...)` has to reach `SkillScope` for a language change to take effect — the workflow scope propagates it for you, and a standalone host sets it with `SkillScope.WithPromptLanguage`.

⚙ **Two providers built from one scope share a session-state key, and the framework rejects that at agent construction.** That is deliberate — the alternative is both contributing everything twice, silently. Build one provider per scope and share the scope instead.

## Policy — off by default

| Gate                                                                        | Default         | Unlocks                                                                                                                                                                                                                                                                                                                                |
| --------------------------------------------------------------------------- | --------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `WithAllowNodeExecution(bool)`                                            | **off**   | `ExecuteNode`, `ExecuteNodes`, `BroadcastNode`, `ReverseBroadcastNode`, `RunCompiledWorkflow`, `GetNodeResult`, `StartCompiledWorkflow`, `ContinueCompiledWorkflow` — the four handle operations (`GetCompiledRunStatus`, `PauseCompiledRun`, `ResumeCompiledRun`, `StopCompiledRun`) are **not** gated |
| `WithAllowedGenericCommands(params string[])`                             | **empty** | `ExecuteCommandOnNode`, `ExecuteCommandById` — and it *narrows* them to the listed names                                                                                                                                                                                                                                        |
| `WithSelectionHandler` / `WithConfirmationHandler`                      | **null**  | registers `RequestSelection` / `RequestConfirmation`; a confirmation handler also backs every `Ask` decision and enables `EnterPlanMode` / `ExitPlanMode`                                                                                                                                                                    |
| `WithPermissionMode(AgentPermissionMode)`                                 | `Auto`        | `Plan` read-only · `Manual` every non-read asks · `AutoEdit` edits run, execution asks · `Auto` only curation asks · `Bypass` nothing asks                                                                                                                                                                               |
| `WithPermissionRule(decision, patterns…)`                                | none            | the fine layer beneath the mode; `Deny` holds in every mode, `Bypass` included                                                                                                                                                                                                                                                     |
| `WithoutPermissionRules()`                                                | —              | removes every rule; the mode is untouched                                                                                                                                                                                                                                                                                              |
| `WithPermissionJudge(Func<ToolInvocation,PermissionDecision>)`            | none            | decides what `Auto` would otherwise settle; a judge that throws denies                                                                                                                                                                                                                                                               |
| `WithModePrompt(mode, text)`                                              | none            | appends host rules after the shipped Shared and mode text — it does not replace them                                                                                                                                                                                                                                                  |
| `WithAutoMarkDirty(bool)`                                                 | off             | marks the tree dirty after every non-query tool call, instead of requiring one `MarkDirty` at the end                                                                                                                                                                                                                                |
| `WithMaxToolCalls` / `WithMaxReadToolCalls` / `WithMaxWriteToolCalls` | unlimited       | pre-flight caps; exceeding one returns an error object rather than throwing                                                                                                                                                                                                                                                            |

`AgentPermissionMode` is `Plan` · `Manual` · `AutoEdit` · `Auto` (the default) · `Bypass`. A rule is `new AgentPermissionRule(PermissionDecision.X, pattern)`, where `X` is `Allow`, `Ask` or `Deny`; the pattern matches a tool name (`DeleteNode`, `Get*`), a category (`category:Edit`), or a source (`source:mcp:*`). Only MCP tools carry a source (`mcp:<server>`, `mcp:management`); skill and sub-agent tools are wrapped with a null source, so a `source:` rule matches neither today. `WithPermissionJudge` receives a `ToolInvocation` — `Name`, `Category` (an `AgentActionCategory`: `Read` / `Edit` / `Execute` / `Curate` / `Interact`), `Source`, `Arguments` — and runs only in `Auto`, for calls no rule settled. `SetPermissionMode` is the runtime switch and returns whether the mode moved; the policy is read per call, so the very next call obeys a mid-session change.

⚙ **The handle operations sit outside the execution gate.** `GetCompiledRunStatus`, `PauseCompiledRun`, `ResumeCompiledRun` and `StopCompiledRun` act on a handle an earlier `StartCompiledWorkflow` / `ContinueCompiledWorkflow` produced, so they do not check `AllowNodeExecution`; the two start tools and the rest of the execution family do.

⚙ **The mode is enforced, not described.** The gate runs inside the call wrapper, on the two hooks every slice shares, so it reaches MCP-sourced and skill-sourced tools as well — which the per-capability flags inside tool bodies never could. Reading a mode's policy text and calling that enforcement is the mistake this replaced: the old numbered level injected prompt text and registered two interaction tools, and no tool body consulted it.

⚙ **The old tool-approval switch is gone: it was the same axis as the mode.** Its gate wrapped every non-query call, which is exactly what `Manual` does — and a host that set a strict level without it was asking for nothing, because the level was prompt text. A tool call is now put to `WithConfirmationHandler` when the mode says `Ask` for its category; with no handler registered that becomes a denial (an unanswerable prompt denies).

⚙ **A spent budget is not a dead end.** The model always has `ResetToolCallLimit`, which puts the question to the user through the confirmation handler and reopens the tree's budget only on agreement; every refusal that names a limit points at it.

⚙ **`ClearHistory` drops the undo trail without touching the canvas.** After it, the agent cannot undo its own earlier work — the canvas is the state, the history is not.

## What the model gets

**69 built-in tools** in the unconditional category groups, all documented in [references/tools.md](references/tools.md), plus `ResetToolCallLimit` and up to four handler-gated tools (two interaction, two plan-mode) — **74** when both handlers are registered. The shape worth knowing up front:

- **Query** (all read-only) — `GetWorkflowSummary`, `GetFullTopology`, `ListNodes`, `GetNodeDetail`, `FindNodes`, `ResolveSlotId`, `ListSlotProperties`, `GetTypeSchema`, `GetComponentContext`, `ValidateWorkflow`, `CompileWorkflow`, `CompileNodeResult`, …
- **Mutation** — `CreateNode`, `DeleteNode`, `ConnectSlots` / `ConnectByProperty`, `DisconnectSlots`, `SetSlotChannel`, `PatchNodeProperties`, `SetEnumSlotCollection`, `Undo` / `Redo`, …
- **Execution** — node-level (`ExecuteNode`, `ExecuteNodes`, `BroadcastNode`, `ReverseBroadcastNode`), chain-level (`RunCompiledWorkflow`, plus the handle form `StartCompiledWorkflow` / `ContinueCompiledWorkflow` with `GetCompiledRunStatus` / `PauseCompiledRun` / `ResumeCompiledRun` / `StopCompiledRun`) and result-level (`GetNodeResult`).
- **Layout** — `GetLayoutInfo`, `PreviewLayout` (read-only dry run) and `ArrangeLayout` (one undoable action). They run the `IWorkflowLayoutSolver` registered on the tree's helper, so a host's custom solution is what the Agent previews and applies; `nodeIndex` scopes the run to that node's reachable subgraph.
- **Interaction** — `RequestSelection`, `RequestConfirmation`, plus `EnterPlanMode` / `ExitPlanMode`, registered only when the matching handler (for the plan pair, the confirmation handler) is wired.
- **Budget** — `ResetToolCallLimit`, always offered, whatever the configured categories.

⚙ **Indices are unstable; ids are not.** Creating or deleting shifts node indices, and a selector change rebuilds a slot enumerator's slots. The shipped prompt tells the model to prefer `GetFullTopology`, `ResolveSlotId` and `ConnectByProperty`. Any custom prompt you write should say the same.

⚙ **Some mutations are deliberately not undoable**: `MoveNode` / `SetNodePosition` / `ResizeNode` (they mirror the GUI drag, which writes continuously) and `PatchNodeProperties` / `PatchComponentById` (direct property writes). Everything structural — create, delete, connect, disconnect, `SetEnumSlotCollection` — is undoable.

⚙ **Unmounted components are rejected, and operating on one elsewhere is a silent no-op.** A node must be in a tree before a tool can reach it.

## Custom auto-layout

The layout solution belongs to the **tree**, not to the Agent and not to the demo. A tree helper that implements `IWorkflowLayoutHost` exposes `LayoutSolver`; register one solution there and every entry point uses it — the host's `node.Arrange(options)`, the demo's right-click menu, and the Agent's `GetLayoutInfo` / `PreviewLayout` / `ArrangeLayout`.

### What the default solution assumes

⚙ **The default pipeline is a hierarchy layout over an untyped edge list.** `WorkflowLayoutProblem` stores node rectangles and directed edges; it has no record of which slot a link came from, so every link is a parent→child edge to `HierarchyAlignmentStage`. The default fits a graph whose links all mean one thing (a workflow's output→input). When a graph mixes semantics — a timeline/sequence chain beside ownership fan-out — the sequence successor enters its predecessor's fan-out group, and the two structures interleave into columns and crossings that no gap setting fixes.

⚙ **The demos use the solution node-scoped.** Every shipped demo entry is `node.Arrange(...)`, invoked from the node's right-click menu, with `Downstream` / `Upstream` / `Both` and the start node pinned; none offers a whole-tree arrange. `nodeIndex` is optional on the Agent tools because the solver accepts the whole tree, not because omitting it is the recommended run for a mixed-semantics graph.

⚙ **When the default does not fit, the seam is a stage or planner.** Classify or filter the edges before the default stages, or add a stage that writes the positions the hierarchy stage cannot know about. Replacing the whole `IWorkflowLayoutSolver` is the larger step, for a different snapshot→solve→apply composition — not the first response to "the default produced a wide graph".

```csharp
var helper = (IWorkflowLayoutHost)tree.GetHelper();

// Replace only the planner and/or cost; the default solver still snapshots, solves and applies.
helper.LayoutSolver = new DefaultWorkflowLayoutSolver(
    planner: new MyPlanner(),
    cost: new DefaultWorkflowLayoutCost());

// Or replace the whole composition.
helper.LayoutSolver = new MyLayoutSolver();
```

A planner picks the stages for one problem:

```csharp
sealed class MyPlanner : IWorkflowLayoutPlanner
{
    public IReadOnlyList<IWorkflowLayoutStage> CreateStages(
        WorkflowLayoutProblem problem, WorkflowLayoutOptions options)
        => [new MyLayeringStage(), new HierarchyAlignmentStage(), new TileSeparationStage()];
}
```

A stage only reads the immutable `problem` and `options`, and writes its own arrays in `WorkflowLayoutState` (`X`/`Y`, plus the reserved `Rank`/`Order`/`RouteX`/`RouteY`). It must not read the model, the view, or `Problem.Sources`, and it must never write a pinned node; `WorkflowLayoutPipeline` copies pinned positions back after the stages run. `IWorkflowLayoutCost` measures the solved arrays only — do not read a curve the view published before the solve.

A full `IWorkflowLayoutSolver` owns the whole composition (`snapshot → solve → apply`). If you implement one, honour `WorkflowLayoutOptions.DryRun`: `PreviewLayout` sets it and expects no anchor to change. `ArrangeLayout` expects the solver to apply; the default applier writes all moves as one `WorkflowActionPair`, so a custom solver should keep that one-undo contract.

Useful knobs live on `WorkflowLayoutOptions`: `StartNode`/`Traversal`/`MaxDepth`/`PinStartNode` for a scoped run, `ColumnGap`/`RowGap`/`PositionTolerance` for spacing, and `PositionTolerance = 0` for strict canonical placement. `GetLayoutInfo` reports the registered solver and whether it differs from `DefaultWorkflowLayoutSolver` (`custom: true`), so an Agent can tell a host's solution from the fallback.

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

⚙ **They are classified as query tools for the budget wherever they come from** — contributed by the provider, they are charged to `MaxReadToolCalls` and never mark the tree dirty, because switching a skill changes what the model is shown and never the graph. That classification used to depend on the host registering them through `WithQueryTools`; it is now taken from `SkillAgentToolkit.ToolNames`, so it holds either way. For **permissions**, though, they carry the default `AgentActionCategory.Execute`, so `Plan` denies them and `AutoEdit` asks — the budget and the permission gate read different things. The same split applies to the five sub-agent tools: query for the budget, `Execute` for the mode.

⚙ **Two injection shapes, chosen by origin, and not interchangeable.** Embedded documents are architectural prompt text: when enabled, their body is injected **in full**. Disk skills follow the Agent Skills convention: only `name` and `description` are advertised, and the body arrives through `load_skill`. That is what keeps a large external skill library from costing every turn.

⚙ **A disk skill is `<root>/<name>/SKILL.md` with `---` frontmatter carrying `name` and `description`** — the same layout as a Claude Skill. The directory name must equal the frontmatter `name` (lowercase kebab-case, ≤ 64 chars; description ≤ 1024). A `references/` folder beside the `SKILL.md` is counted but not read until `read_skill_resource` asks for one, and resource paths are confined to the skill directory.

⚙ **A malformed skill is reported, not swallowed and not fatal.** It appears in the list with `State == Error`, an `Error` message, and `IsActive == false`, so one broken folder cannot take the others down or silently vanish. Check `scope.Skills.Status.ErrorCount`.

⚙ **All skills start enabled,** so adding a root never silently empties the prompt. `Refresh()` rediscovers and preserves the flags — call it when a root's contents changed; do **not** call it per turn, since it always advances `Skills.Version` and invalidates the provider's render.

⚙ **With skills attached before the skeleton renders, the static prompt no longer carries the corpus.** `ProvideProgressiveContextPrompt()` omits it and the provider renders it instead; leaving it in both would send every document twice. Attaching skills *after* that string was built leaves the corpus frozen in: the provider sees it was already delivered and contributes only the documents withdrawn since.

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
        Options = new Dictionary<string, object?>
        {
            ["env"] = new Dictionary<string, object?> { ["FILESYSTEM_ROOT"] = "C:/data" },
        },
    },
    new McpServerConfiguration                                   // remote Streamable HTTP
    {
        Name = "Microsoft Learn", RunMode = McpServerRunMode.Http,
        Endpoint = "https://learn.microsoft.com/api/mcp",
        Options = new Dictionary<string, object?>
        {
            ["connectionTimeout"] = 30,
            ["headers"] = new Dictionary<string, object?> { ["Authorization"] = "Bearer <token>" },
        },
    },
};

var mcpTools = await mcp.LoadAsync(configs);
```

`RunMode` is `Npm` · `Npx` · `Uvx` · `Pip` · `Dotnet` · `Exe` · `Http`. Local modes install into `<root>/{node,py,dotnet,exe}` idempotently; HTTP uses Streamable HTTP with an SSE fallback.

⚙ **One failing server does not fail the batch.** A bad config sets that server's state to `Error`, raises `ServerError`, and returns an empty tool set; the rest load. Inspect `mcp.Status` (a bindable `McpStatusViewModel` with `Servers`, `ConnectedCount`, `ErrorCount`, `IsAllReady`).

⚙ **Unknown `Options` keys throw** rather than being ignored — `env` and `workingDirectory` on stdio, `headers` / `oauth` / `connectionTimeout` / `transportMode` / `ownsSession` on HTTP.

⚙ **Attach the MCP scope with `WithMcps` so its tools are wrapped like every other tool.** Reached that way, a connected server's tools are counted against `MaxToolCalls`, marshalled onto your synchronization context, and raise `ToolCalled`. If you instead merge `mcp.LoadedTools` into `ChatOptions.Tools` by hand, none of that happens — and they will also duplicate, because the provider already offers them.

⚙ **The model can add servers and change their launch arguments, and the session's mode says whether it may.** `AddMcpServer` connects one the host did not pre-register; `SetMcpServerArguments` changes a local server's arguments in place, keeping its tools' identity. Under the default `Auto` only this curation asks; under `Manual` and `AutoEdit` it asks too; `Plan` refuses it; under `Bypass` nothing does. What no mode opens is a `Deny` rule — `WithPermissionRule(Deny, "mcp-add:local")` forbids installing software on this machine whatever the mode. The mode is a runtime setting: `SetPermissionMode` from a binding, and the prompt follows on the next turn.

⚙ **Adding a local server means the model can cause a package to be installed and launched on the machine.** The rule to forbid that is `WithPermissionRule(Deny, "mcp-add:local")`: it holds in every mode, `Bypass` included, while an Http endpoint is unaffected. The tool asks the same policy about a synthesised `mcp-add:local` / `mcp-add:http` call because the difference lives in the arguments, not the tool name.

Details and the full option surface are in [references/mcp.md](references/mcp.md).

## Sub-agents

`WithSubAgents(scope)` attaches the third subsystem (skills, MCP, sub-agents). The model gains five tools — `SpawnSubAgent`, `WaitSubAgents`, `GetSubAgentResult`, `ListSubAgents`, `CancelSubAgent` — and uses them to dispatch background children, then collect their reports. The pair is deliberately dispatch-and-poll rather than one blocking call: `SpawnSubAgent` returns at once, and the child runs on a background task while the host's UI thread stays free.

```csharp
var subAgents = SubAgentScope.ForClient(chatClient)   // the host owns the chat client
    .WithSubAgentDepth(3);                            // bound the tree depth; the budget alone only terminates it
scope.WithSubAgents(subAgents);
```

⚙ **The grant is a whitelist, and omitting it means "inherit everything".** `allowedTools`, `allowedSkills` and `allowedMcpServers` each default to the parent's currently-enabled set; an empty array grants none. A child is never shown a capability it does not hold — a request it cannot satisfy is reported in `dropped` with the wall named. Skills and MCP servers are handed down as narrowed *views*, not by name lists: the child's `ListSkills` / `ListMcpServers` shows only what it was granted.

⚙ **The budget is one ledger for the whole tree, and every grant is strictly smaller than what remains** — that is what makes any spawn depth terminate. `WithSubAgentDepth(n)` is what makes it usable; set both, or a single unlimited root is bounded only by the fallback `SpawnBudget` (default 64), not by the depth you intended.

⚙ **A child holds its own share, not the parent's.** A reset the child itself makes (`ResetToolCallLimit`) goes up through the tree and reaches the user, so a stuck child can reopen the budget rather than sit rejected; a parent's reset does not reach down into a child. The interaction configuration is inherited — the whole permission policy (mode, rules, judge), the per-mode prompt overrides and the two handlers — so `RequestSelection` / `RequestConfirmation` really exist on the child rather than being listed and unreachable.

## Framework-native providers

Three capabilities are the Agent Framework's own, attached through the scope rather than reimplemented: `WithTodoTracking()` (the model's `todos_*` plan, so a long task keeps its place), `WithAgentModes(options)` (`mode_get` / `mode_set`, with the modes and starting mode the host supplies), and `WithContextCompaction(maxContextWindowTokens, maxOutputTokens)` (old tool results summarised and old turns dropped as the window fills — pass the model's real numbers). Their tools are deliberately not wrapped: they neither read nor write the tree, so no budget accounting, UI marshalling or dirty marking applies. `WithContextProvider(factory)` adds a provider of your own alongside them.

## Checking your wiring

⚙ **Start by asking the agent to orient itself** — `GetWorkflowSummary`, then `GetFullTopology`. If those come back empty or error, the scope is not bound to the tree; if they work but mutations do not, it is `WithSynchronizationContext`, the permission mode, or a refusal rule.

⚙ **Try a mutation the agent should be allowed to make and one it should not.** With node execution off, asking it to run a chain should come back `disabled by host policy` — that reply is the confirmation your gates are wired, not a failure.

⚙ **Watch the undo stack.** Every structural tool call the agent makes should appear as one entry. If nothing lands there, the agent is reaching a code path you added yourself rather than the library's commands.

⚙ `Examples/Workflow/Common/Lib/ViewModels/Workflow/Helper/AgentHelper.cs` is a complete working host — scope setup, an MCP panel, a sub-agent panel (`SubAgentScope.ForClient(chatClient).WithSubAgentDepth(3)`), per-conversation tool assembly and the permission-mode wiring. Read it before writing your own, whether from NuGet or from a checkout.

⚙ The prompts that ship to the model are under `Src/Core/VeloxDev.Core.Extension/Resources/Workflow/{en,zh}/` — seven `Skills/`, five `References/` and six `Safety/` documents, selected by `ProvideProgressiveContextPrompt(language)`. Read them to know what the model has already been told **before** you add to it, so your instructions do not fight the built-in ones.
