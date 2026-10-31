---
name: veloxdev-drive-workflow-with-ai
description: Give an LLM control of a VeloxDev workflow graph — wire a WorkflowAgentScope onto a tree, hand the tools to an IChatClient through one context provider, set the host's policy gates and call budgets, document components for the model with [AgentContext], load switchable skills (embedded documents or Agent-Skills folders on disk), connect external MCP servers, and dispatch background sub-agents — including using the MCP or skill subsystem on its own, without the workflow layer. Use when connecting an LLM or agent to a workflow graph, or when adding MCP servers, agent skills or sub-agents.
---

## Responsibility

Turn a workflow `Tree` into a **tool surface** an LLM can operate: list and inspect nodes, create and delete them, connect slots, patch properties, run a compiled chain or reverse-compute one node's result — through the *same* undoable commands the GUI uses.

Package: **`VeloxDev.Core.Extension`** (`netstandard2.0;net8.0`). It also carries the compiled-graph and checkpoint helpers.

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

⚙ **`WithSynchronizationContext` is part of the contract.** Workflow components are UI-bound and every tool call is marshalled through it. Call it while on the UI thread; without it, tool calls touch the graph from a thread pool thread.

⚙ **The whole call stays on that context — including everything after an `await`.** The wrapper posts the call, and a posted callback resumes on the context, so a tool's post-await work runs where the components live. The tool bodies carry no `ConfigureAwait(false)` for that reason. It applies to your own `WithTools` / `WithQueryTools` tools too: an `AIFunction` gets the same wrapper; any other `AITool` type passes through untouched.

⚙ **The prompt is built on the agent invocation thread, not on that context.** Anything read there must be thread-safe. `McpStatusViewModel.Snapshot` and `SkillsViewModel.Snapshot` are immutable copies republished on the bound thread whenever the collection changes. Read `Servers` / `Skills` from your UI, and the snapshots from anywhere else.

⚙ **The context provider is the single source of tools, and it re-renders per turn.** The scope's own provider (`WorkflowAgentContextProvider`, from `CreateContextProvider()`) contributes the current tool set — the built-in and custom tools only — on each invocation; the attached subsystems' providers contribute theirs beside it; a connected MCP server's tools come from the MCP provider. A skill switched off, a tool registered late, or a server loaded mid-session reaches the model on the next turn without rebuilding the agent.

⚙ **Pass tools through the provider, not `ChatOptions.Tools`.** MAF unions that list with the providers' contribution and does not deduplicate by name, so a tool offered through both channels is sent to the model twice.

⚙ **`ProvideTools()` is a one-shot snapshot** for hosts that build the agent by hand; it does not pick up later registrations. `CreateToolkit()` returns one instance per scope, so the call counters and the snapshot history behind `GetChangesSinceSnapshot` stay consistent.

⚙ **`WithAutoDiscovery(AgentLanguages? language = null)` is how the model learns your node types.** It lists the compiled agent context tree's `Customer/` directories — components (filed by the four component interfaces), enums, interfaces and data types — and registers what the generator admitted for prompt rendering. There is no assembly scan behind it: a type nothing annotated, no component interface reaches and no member references is not in the tree. `ListCreatableTypes` and `CreateNode` resolve against that same compiled tree (framework and customer roots), independently of whether `WithAutoDiscovery` ran; `WithEnums` / `WithInterfaces` / `WithComponents` / `WithData` (each takes a `Type[]` and an optional `AgentLanguages?`) add its context block to the prompt.

⚙ **Customer context and framework context take their language differently.** `ProvideCustomerContext()` and `ProvideCustomerDataContext()` take no argument — they render each registered type in the language it was registered under. `ProvideFrameworkContext(language)` and `ProvideFrameworkDataContext(language)` take one.

## Policy — off by default

| Gate | Default | Unlocks |
|---|---|---|
| `WithAllowNodeExecution(bool)` | **off** | `ExecuteNode`, `ExecuteNodes`, `BroadcastNode`, `ReverseBroadcastNode`, `RunCompiledWorkflow`, `GetNodeResult`, `StartCompiledWorkflow`, `ContinueCompiledWorkflow`. The four handle operations (`GetCompiledRunStatus`, `PauseCompiledRun`, `ResumeCompiledRun`, `StopCompiledRun`) are **not** gated. |
| `WithAllowedGenericCommands(params string[])` | **empty** | `ExecuteCommandOnNode`, `ExecuteCommandById` — and it *narrows* them to the listed names |
| `WithSelectionHandler` / `WithConfirmationHandler` | **null** | registers `RequestSelection` / `RequestConfirmation`; a confirmation handler also backs every `Ask` decision and enables `EnterPlanMode` / `ExitPlanMode` |
| `WithPermissionMode(AgentPermissionMode)` | `Auto` | `Plan` read-only · `Manual` every non-read asks · `AutoEdit` edits run, execution asks · `Auto` only curation asks · `Bypass` nothing asks |
| `WithPermissionRule(decision, patterns…)` | none | the fine layer beneath the mode; `Deny` holds in every mode, `Bypass` included |
| `WithoutPermissionRules()` | — | removes every rule; the mode is untouched |
| `WithPermissionJudge(Func<ToolInvocation, PermissionDecision>)` | none | decides what `Auto` would otherwise settle; a judge that throws denies |
| `WithModePrompt(mode, text)` | none | appends host rules after the shipped Shared and mode text |
| `WithAutoMarkDirty(bool)` | off | marks the tree dirty after every non-query tool call |
| `WithMaxToolCalls` / `WithMaxReadToolCalls` / `WithMaxWriteToolCalls` | unlimited | pre-flight caps; exceeding one returns an error object rather than throwing |

`AgentPermissionMode` is `Plan` · `Manual` · `AutoEdit` · `Auto` (the default) · `Bypass`. A rule is `new AgentPermissionRule(PermissionDecision.X, pattern)`, where `X` is `Allow`, `Ask` or `Deny`; the pattern matches a tool name (`DeleteNode`, `Get*`), a category (`category:Edit`), or a source (`source:mcp:*`). Only MCP tools carry a source (`mcp:<server>`, `mcp:management`); skill and sub-agent tools carry a null source, so a `source:` rule matches neither today. `WithPermissionJudge` receives a `ToolInvocation` — `Name`, `Category` (an `AgentActionCategory`: `Read` / `Edit` / `Execute` / `Curate` / `Interact`), `Source`, `Arguments` — and runs only in `Auto`, for calls no rule settled. `SetPermissionMode` is the runtime switch and returns whether the mode moved; the policy is read per call.

⚙ **The handle operations sit outside the execution gate.** They act on a handle an earlier `StartCompiledWorkflow` / `ContinueCompiledWorkflow` produced.

⚙ **The mode is enforced inside the call wrapper**, on the two hooks every slice shares, so it reaches MCP-sourced and skill-sourced tools as well.

⚙ **A confirmation is how an `Ask` is answered.** With no handler registered, an unanswerable prompt denies. The old separate tool-approval switch is gone; it was the same axis as the mode.

⚙ **A spent budget is not a dead end.** The model always has `ResetToolCallLimit`, which puts the question to the user through the confirmation handler and reopens the tree's budget only on agreement; every refusal that names a limit points at it.

⚙ **`ClearHistory` drops the undo trail without touching the canvas.** The canvas is the state, the history is not.

## What the model gets

**69 built-in tools** in the unconditional category groups, all documented in [references/practices/tools.md](references/practices/tools.md), plus `ResetToolCallLimit` and up to four handler-gated tools (two interaction, two plan-mode) — **74** when both handlers are registered.

- **Query** (read-only) — `GetWorkflowSummary`, `GetFullTopology`, `ListNodes`, `GetNodeDetail`, `FindNodes`, `ResolveSlotId`, `ListSlotProperties`, `GetTypeSchema`, `GetComponentContext`, `ValidateWorkflow`, `CompileWorkflow`, `CompileNodeResult`, …
- **Mutation** — `CreateNode`, `DeleteNode`, `ConnectSlots` / `ConnectByProperty`, `DisconnectSlots`, `SetSlotChannel`, `PatchNodeProperties`, `SetEnumSlotCollection`, `Undo` / `Redo`, …
- **Execution** — node-level (`ExecuteNode`, `ExecuteNodes`, `BroadcastNode`, `ReverseBroadcastNode`), chain-level (`RunCompiledWorkflow`, plus the handle form `StartCompiledWorkflow` / `ContinueCompiledWorkflow` with `GetCompiledRunStatus` / `PauseCompiledRun` / `ResumeCompiledRun` / `StopCompiledRun`) and result-level (`GetNodeResult`).
- **Layout** — `GetLayoutInfo`, `PreviewLayout` (read-only dry run) and `ArrangeLayout` (one undoable action). They run the `IWorkflowLayoutSolver` registered on the tree's helper; `nodeIndex` scopes the run to that node's reachable subgraph.
- **Interaction** — `RequestSelection`, `RequestConfirmation`, plus `EnterPlanMode` / `ExitPlanMode`, registered only when the matching handler is wired.
- **Budget** — `ResetToolCallLimit`, always offered.

⚙ **Indices are unstable; ids are not.** Creating or deleting shifts node indices, and a selector change rebuilds a slot enumerator's slots. The shipped prompt tells the model to prefer `GetFullTopology`, `ResolveSlotId` and `ConnectByProperty`. Custom prompts say the same.

⚙ **Some mutations are runtime state, not history entries**: `MoveNode` / `SetNodePosition` / `ResizeNode` (they mirror the GUI drag, which writes continuously), `PatchNodeProperties` / `PatchComponentById` (direct property writes) and `SetSlotChannel` / `SetEnumSlotChannel` (channel assignment). Everything structural — create, delete, connect, disconnect, `SetEnumSlotCollection` — is undoable.

⚙ **Unmounted components are rejected, and operating on one elsewhere is a silent no-op.** A node must be in a tree before a tool can reach it.

## Where to read next

| You are about to… | Read |
|---|---|
| Understand the provider composition, thread hand-off and prompt build | [references/practices/wiring-and-policy.md](references/practices/wiring-and-policy.md) |
| Look up a built-in tool, its category and its result shape | [references/practices/tools.md](references/practices/tools.md) |
| Add switchable skills (embedded or disk) | [references/practices/skills.md](references/practices/skills.md) |
| Connect, reconfigure or gate MCP servers | [references/practices/mcp.md](references/practices/mcp.md) |
| Dispatch background sub-agents | [references/practices/subagents.md](references/practices/subagents.md) |
| Attach the framework-native todo / agent-mode / compaction providers | [references/practices/providers.md](references/practices/providers.md) |
| Expose a custom auto-layout solution to the Agent | [references/practices/layout-agent.md](references/practices/layout-agent.md) |

## Pitfalls

Read the symptom first, then the pitfall file.

| Symptom | Read |
|---|---|
| Mutations fail, or arrive on a background thread | [references/pitfalls/wiring.md](references/pitfalls/wiring.md) |
| A tool is refused and the reason is unclear | [references/pitfalls/permissions-and-budget.md](references/pitfalls/permissions-and-budget.md) |
| Every tool appears twice, or a subsystem's tool set is empty | [references/pitfalls/wiring.md](references/pitfalls/wiring.md) |
| An MCP server fails, duplicates tools, or installs software unexpectedly | [references/pitfalls/mcp.md](references/pitfalls/mcp.md) |
| A skill does not load, or a sub-agent cannot reach a capability | [references/pitfalls/skills-and-subagents.md](references/pitfalls/skills-and-subagents.md) |

## Checking your wiring

⚙ **Ask the agent to orient itself** — `GetWorkflowSummary`, then `GetFullTopology`. If those come back empty or error, the scope is not bound to the tree; if they work but mutations do not, check `WithSynchronizationContext`, the permission mode, or a refusal rule.

⚙ **Try a mutation the agent should be allowed to make and one it should not.** With node execution off, asking it to run a chain comes back `disabled by host policy` — that reply confirms the gates are wired.

⚙ **Watch the undo stack.** Every structural tool call the agent makes appears as one entry. A missing entry means the agent reached a code path you added rather than the library's commands.

⚙ `Examples/Workflow/Common/Lib/ViewModels/Workflow/Helper/AgentHelper.cs` is a complete working host — scope setup, an MCP panel, a sub-agent panel (`SubAgentScope.ForClient(chatClient).WithSubAgentDepth(3)`), per-conversation tool assembly and the permission-mode wiring.

⚙ The prompts that ship to the model are under `Src/Core/VeloxDev.Core.Extension/Resources/Workflow/{en,zh}/` — seven `Skills/`, five `References/` and six `Safety/` documents, selected by `ProvideProgressiveContextPrompt(language)`. Read them to know what the model has already been told before you add to it.
