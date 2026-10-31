# Sub-agents

`WithSubAgents(scope)` attaches the third subsystem (after skills and MCP). The model gains five tools — `SpawnSubAgent`, `WaitSubAgents`, `GetSubAgentResult`, `ListSubAgents`, `CancelSubAgent` — and uses them to dispatch background children, then collect their reports.

```csharp
var subAgents = SubAgentScope.ForClient(chatClient)   // the host owns the chat client
    .WithSubAgentDepth(3);                            // bound the tree depth; the budget alone only terminates it
scope.WithSubAgents(subAgents);
```

⚙ **A spawn can pick a model when the host registers more than one.** Use `SubAgentScope.ForClientFactory(model => model switch { "cheap" => cheapClient, null => defaultClient, _ => throw new ArgumentException(...) })`; `SpawnSubAgent` takes an optional `model` name and hands it to the factory unchanged. The library never validates model names — the factory's exception becomes the child's `Failed` row. The two-parameter constructor `SubAgentScope(Func<WorkflowAgentScope, string?, AIAgent>)` is the escape hatch when the agent itself is built per model.

⚙ **The pair is dispatch-and-poll.** `SpawnSubAgent` returns at once, and the child runs on a background task while the host's UI thread stays free. The model collects the report with `WaitSubAgents` / `GetSubAgentResult`; the latter caps the report at 64 KiB by default (`maxChars`, 0 = no cap).

## The grant

`allowedTools`, `allowedSkills` and `allowedMcpServers` each default to the parent's currently-enabled set; an empty array grants none. The grant is a **whitelist**, and a child is never shown a capability it does not hold. A request it cannot satisfy is reported in `dropped` with the wall named.

⚙ **Skills and MCP servers are handed down as narrowed views, not by name lists.** The child's `ListSkills` / `ListMcpServers` shows only what it was granted.

## The budget

⚙ **One ledger for the whole tree.** Every grant is strictly smaller than what remains, which is what makes any spawn depth terminate.

⚙ **`WithSubAgentDepth(n)` makes the tree usable.** Set both the budget and the depth; a single unlimited root is bounded only by the fallback `SpawnBudget` (default 64), not by the depth you intended.

⚙ **A child holds its own share.** A reset the child itself makes (`ResetToolCallLimit`) goes up through the tree and reaches the user, so a stuck child can reopen the budget; a parent's reset does not reach down into a child.

⚙ **Every granted child holds `ResetToolCallLimit`, even under an empty whitelist.** The tool is exempt from every tool switch, and the grant report includes it for that reason — "an empty whitelist" means no workflow tools beyond the budget escape hatch.

⚙ **The interaction configuration is inherited** — the whole permission policy (mode, rules, judge), the per-mode prompt overrides and the two handlers — so `RequestSelection` / `RequestConfirmation` exist on the child rather than being listed and unreachable.

## The status model

`SubAgentScope.Children` is the bindable roster of `SubAgentStatusViewModel` rows (depth, state, result); `SubAgentScope.Snapshot` is the immutable copy republished on the roster's thread. Read `Children` from the UI and `Snapshot` from the agent invocation thread or any other thread.

When a sub-agent cannot reach a capability, read [references/pitfalls/skills-and-subagents.md](../pitfalls/skills-and-subagents.md).
