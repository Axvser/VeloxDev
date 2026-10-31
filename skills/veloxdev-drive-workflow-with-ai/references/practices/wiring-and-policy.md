# Wiring, providers and policy

## The provider composition

`scope.CreateContextProviders()` returns the providers in a fixed order:

1. the compaction provider, when the host asked for one
2. the scope's own `WorkflowAgentContextProvider`
3. the skills provider, when attached
4. the MCP provider, when attached
5. the sub-agent provider, when attached
6. the framework-native todo provider, when attached
7. the framework-native agent-mode provider, when attached
8. any factory registered with `WithContextProvider`

The order is fixed rather than the order the host attached things in, because the framework concatenates what providers contribute; the prompt always reads "context first, instructions after".

⚙ **The scope's provider re-renders per turn.** It contributes the current tool set — built-in and custom tools — on each invocation. A skill switched off, a tool registered late, or a server loaded mid-session reaches the model on the next turn without rebuilding the agent.

⚙ **Each subsystem provider is handed the workflow scope's own tool policy.** Standalone, a subsystem is handed a thread-only policy — its tools are marshalled and nothing else. Composed into a workflow agent, it is handed that scope's policy, so its tools count against `MaxToolCalls` / `MaxReadToolCalls` / `MaxWriteToolCalls`, raise `ToolCalled`, and mark the tree dirty like any other. A subsystem never decides this for itself.

⚙ **A subsystem's management tools are contributed by its provider.** Registering them again through `WithTools` / `WithQueryTools` puts every one of them in the prompt twice, because the framework unions tool lists without deduplicating by name. That is also why `AsAIAgent(providers, instructions)` has no tools parameter.

⚙ **Two providers built from one scope share a session-state key, and the framework rejects that at agent construction.** Build one provider per scope and share the scope instead.

## Threading

⚙ **`WithSynchronizationContext` marshals every tool call onto the context.** Call it while on the UI thread.

⚙ **The whole call stays on that context — including everything after an `await`.** The wrapper posts the call, and a posted callback resumes on the context, so a tool's post-await work (verifying a connection, the second node of an `ExecuteNodes` batch, the execution engine driving a compiled chain) runs where the components live. The tool bodies carry no `ConfigureAwait(false)` for that reason. Your own `WithTools` / `WithQueryTools` tools get the same wrapper when they are `AIFunction`; any other `AITool` type passes through untouched.

⚙ **The prompt is built on the agent invocation thread, not on that context.** Anything read there must be thread-safe. `McpStatusViewModel.Snapshot` and `SkillsViewModel.Snapshot` are immutable copies republished on the bound thread whenever the collection changes. Read `Servers` / `Skills` from your UI, and the snapshots from anywhere else.

## Policy

The policy is read per call, so the next call obeys a mid-session change.

| Gate | Default | Effect |
|---|---|---|
| `WithAllowNodeExecution(bool)` | off | The eight execution tools that start or drive node code re-check this at call time and return `disabled by host policy` when it is off. The four handle operations are not gated. |
| `WithAllowedGenericCommands(params string[])` | empty | Narrows `ExecuteCommandOnNode` / `ExecuteCommandById` to the listed command names. |
| `WithSelectionHandler` / `WithConfirmationHandler` | null | Registers the interaction tools. A confirmation handler also backs every `Ask` decision and enables the plan-mode pair. |
| `WithPermissionMode(AgentPermissionMode)` | `Auto` | The mode matrix below. |
| `WithPermissionRule(decision, patterns…)` | none | The fine layer beneath the mode; `Deny` holds in every mode, including `Bypass`. |
| `WithoutPermissionRules()` | — | Removes every rule; the mode is untouched. |
| `WithPermissionJudge(Func<ToolInvocation, PermissionDecision>)` | none | Decides what `Auto` would otherwise settle; a judge that throws denies. |
| `WithModePrompt(mode, text)` | none | Appends host rules after the shipped Shared and mode text. |
| `WithAutoMarkDirty(bool)` | off | Marks the tree dirty after every non-query tool call. |
| `WithMaxToolCalls` / `WithMaxReadToolCalls` / `WithMaxWriteToolCalls` | unlimited | Pre-flight caps; exceeding one returns an error object rather than throwing. |

### The mode matrix

| Mode | `Read` / `Interact` | `Edit` | `Execute` | `Curate` |
|---|---|---|---|---|
| `Plan` | allow | deny | deny | deny |
| `Manual` | allow | ask | ask | ask |
| `AutoEdit` | allow | allow | ask | ask |
| `Auto` (default) | allow | allow | allow | ask |
| `Bypass` | allow | allow | allow | allow |

⚙ **A rule is `new AgentPermissionRule(PermissionDecision.X, pattern)`**, where `X` is `Allow`, `Ask` or `Deny`. The pattern matches a tool name (`DeleteNode`, `Get*`), a category (`category:Edit`), or a source (`source:mcp:*`). Only MCP tools carry a source (`mcp:<server>`, `mcp:management`); skill and sub-agent tools carry a null source.

⚙ **`WithPermissionJudge` receives a `ToolInvocation`** — `Name`, `Category`, `Source`, `Arguments` — and runs only in `Auto`, for calls no rule settled.

⚙ **`SetPermissionMode` is the runtime switch** and returns whether the mode moved. The prompt follows on the next turn.

⚙ **A confirmation is how an `Ask` is answered.** With no handler registered, an unanswerable prompt denies.

⚙ **The mode is enforced inside the call wrapper**, on the two hooks every slice shares, so it reaches MCP-sourced and skill-sourced tools as well.

⚙ **A spent budget is not a dead end.** The model always has `ResetToolCallLimit`, which puts the question to the user through the confirmation handler and reopens the tree's budget only on agreement; every refusal that names a limit points at it.

## Feedback and budgets

⚙ **The budget split follows the tool's classification, not only its flag.** Query tools count against `MaxReadToolCalls`; everything else counts against `MaxWriteToolCalls`. `GetCompiledRunStatus` is classified `Execute` and counts as a write even though it starts no code.

⚙ **Skills and sub-agent tools are query tools for the budget** (their names are read from `SkillAgentToolkit.ToolNames` / `SubAgentAgentToolkit.ToolNames`), and `Execute` for the permission mode. The budget and the permission gate read different things.

⚙ **`WithAutoMarkDirty` marks the tree after every non-query call.** A query-only custom tool registered with `WithQueryTools` is never marked dirty.

⚙ **`ToolCalled` fires after every wrapped call.** Use it for host-side logging or UI feedback.

## Custom tools

```csharp
scope.WithTools("<prompt text describing these tools>", myTools);
scope.WithQueryTools("<prompt text>", myReadOnlyTools);   // classified as read-only for the budgets
```

⚙ Tools added this way are always included regardless of category, and get the tracking wrapper — budget accounting, UI-thread marshalling, `ToolCalled`, auto-dirty. Only `AIFunction` instances are wrapped; anything else is added to the tool list untouched.

When mutations fail or a tool appears twice, read [references/pitfalls/wiring.md](../pitfalls/wiring.md).
