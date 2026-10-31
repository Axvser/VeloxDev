# The tool surface

**76 built-in tools** in the unconditional groups of `WorkflowToolCategory` (`Composite` is reserved and empty; `Layout` carries the three solution tools; the `Execution` / `Command` gates are checked inside each tool body). On top of them: `ResetToolCallLimit`, always; `RequestSelection` / `RequestConfirmation`, each when its handler exists; `EnterPlanMode` / `ExitPlanMode`, both when a confirmation handler exists. With both handlers configured that is **81**. The mode does not decide whether they exist: asking, and the Agent's own move into and out of Plan, are `Interact`, allowed in every mode.

`ResetToolCallLimit` is registered in every configuration, whatever the categories — it is the only way out of a spent budget. It asks the user through the confirmation handler and reopens the tree's budget on agreement.

**R** = read-only: never dirties the tree, counts against `MaxReadToolCalls`. **M** = mutating: counts against `MaxWriteToolCalls`. **†** = mutating and **not undoable**.

## Query — always registered, all read-only

| Tool | Effect |
|---|---|
| `GetWorkflowSummary` | tree id, node and link counts, distinct node types. The intended first call. |
| `GetFullTopology(maxNodes?)` | the whole graph — nodes, slots and links — in one call. `maxNodes` caps the node list and keeps only links whose endpoints are included; the reply reports `truncated` |
| `ListNodes(maxNodes?)` | compact JSON per node: `{i, id, t, x, y, l, w, h, slots, …scalars}`. Uncapped it is a bare array; with `maxNodes` it becomes `{status, totalNodes, returnedNodes, truncated, nodes}` |
| `GetNodeDetail(nodeIndex)` | one node with its slots and connections |
| `GetNodeDetailById(runtimeId)` | the same, addressed by a stable id |
| `FindNodes(typeName, propertyName, propertyValue, maxMatches?)` | filtered node list, cheaper than listing everything. With `maxMatches`, `matchCount` still counts every match |
| `SerializeWorkflow()` | the tree's library JSON document — save it with a host file tool, or pass it back to `LoadWorkflow` |
| `LocateNode(nodeIndex)` / `LocateSlot(nodeIndex, slotIndex)` / `LocateLink(linkId)` | scroll the host canvas so the thing is centred; read-only, zoom preserved. Fails when the host attached no surface navigator |
| `ListCompiledRuns()` | every run handle this scope still holds, with running state and outcome |
| `GetExecutionLog(maxEntries?)` | the tree's execution log; `maxEntries` keeps the newest N and reports `truncated` |
| `ListConnections` | visible links as `{id, sid, rid}` |
| `GetLinkDetail(linkId)` | a link's endpoint slots and their parent nodes |
| `ResolveSlotId(nodeIndex, propertyName, collectionIndex)` | a slot's `RuntimeId` without fetching the whole node |
| `ListSlotProperties(nodeIndex)` | single slots, slot collections and enumerators, with `allowedSelectorTypes` |
| `GetEnumSlotByValue(nodeIndex, propertyName, conditionValue)` | the slot inside an enumerator for a given enum or bool value |
| `GetTypeSchema(fullTypeName, maxChars?)` | a .NET type as a JSON schema — properties, types, current values. Over `maxChars` (default 32 KiB) the reply is a truncation envelope with a preview |
| `GetComponentContext(fullTypeName, language, maxChars?)` | the runtime `[AgentContext]` documentation for a type. The tool maps a language name containing `Chinese` to Chinese and every other value to English. Same `maxChars` envelope |
| `ListComponentCommands(nodeIndex)` | command names and parameter types |
| `ListCreatableTypes` | concrete node and slot types the compiled context tree carries (framework and customer roots) that have a public parameterless constructor |
| `ValidateWorkflow` | structural lint: zero-size nodes, isolated nodes, slot-less nodes, duplicate links, cycles (one `error` per back edge), and SlotEnumerators with no selector |
| `CompileWorkflow(startNodeIndex)` | the Root-role plan: segments, branches, `Order` / `ChainIndex` / `Offset` |
| `CompileNodeResult(nodeIndex)` | the Terminal-role reverse compile of that node's ancestor cone |
| `GetCompileStatus` | the current compile identity without recompiling |

## Analytics

A category of its own with exactly one member — its flag is neither `Query` nor `Mutation`:

| Tool | Effect |
|---|---|
| `GetNodeStatistics(nodeIndex)` | in/out degree, connected node ids, slot count |

⚙ **It is read-only** (it counts against `MaxReadToolCalls`), so it behaves like a Query tool — but it is **not registered by the `Query` flag**. A host that passes `WorkflowToolCategory.Query` alone does not get it. Name `Analytics` explicitly alongside `Query` when filtering.

## Graph traversal — read-only

`SearchForward`, `SearchReverse`, `SearchAllRelative` (BFS with an optional type filter and depth bound), `IsConnected(source, target, direction)`, `FindPath(source, target)`.

## State

`TakeSnapshot` (R), `GetChangesSinceSnapshot` (R), `MarkDirty` (M) — the only writing member.

⚙ `WorkflowStateTracker` builds its snapshots and diffs with `VeloxJsonObject` / `VeloxJsonFormat` from `VeloxDev.Serialization`, not Newtonsoft `JObject`. `TakeSnapshot` / `GetChangesSinceLastSnapshot` return indented JSON.

## Mutation — undoable unless marked

| Tool | Effect |
|---|---|
| `CreateNode` | creates a node of a discovered type; lands exactly at `(left, top)` when there is no overlap, otherwise shifts right past each overlapping node with a 30 px gap in a retry loop. Sizes from the type's default, else 300 × 260 |
| `DeleteNode` | an atomic four-phase cascade — connections, slots, node, index |
| `CreateSlotOnNode` / `AddSlotToCollection` / `RemoveSlotFromCollection` | slot lifecycle |
| `DeleteSlot` | removes a slot and its connections |
| `ConnectSlots` / `ConnectSlotsById` | connect two slots |
| `ConnectByProperty` | connect by node + property name — the preferred form |
| `DisconnectSlots` / `DisconnectSlotsById` | remove a connection |
| `SetSlotChannel`† | change a slot's `SlotChannel` — a direct channel write, no undo entry |
| `SetEnumSlotCollection` | replace a slot enumerator's selector set. The only way to change a `[SlotSelectors]` property. |
| `ConnectEnumSlot` | connect one slot of a sender `SlotEnumerator` to a receiver |
| `SetEnumSlotChannel`† | change one enumerator slot's `SlotChannel` — a direct write, no undo entry |
| `Undo` / `Redo` / `ClearHistory` | history. `ClearHistory` drops the trail; the canvas is untouched. |
| `MoveNode`† / `SetNodePosition`† / `ResizeNode`† | mirror the GUI drag: direct writes, **no undo entry** |
| `PatchNodeProperties`† / `PatchComponentById`† | direct property writes, **not undoable by design** |
| `LoadWorkflow(json)`† | replaces the scope's tree with the graph a `SerializeWorkflow` document restores. The host must rebind its canvas (`WithWorkflowLoader` / `TreeChanged`); the previous tree is left for the host |
| `ClearFinishedRuns()` | releases finished compiled-run handles; running ones are untouched |

⚙ **A rejected connection is silent at the framework level**, so the connect tools re-verify against `LinksMap` and return `status:"rejected"` with `reasons`, a `hint` and often a `preferredAlternative`. A model that reads only the status field knows the truth.

⚙ **A same-direction link replaces the previous one** when the channel is `One*`. That is the channel's contract and comes back as success.

⚙ **`PatchNodeProperties` refuses `[SlotSelectors]` properties** with an explicit pointer at `SetEnumSlotCollection`.

## Execution — eight gated by `WithAllowNodeExecution(true)`, four handle operations not gated

The eight that start or drive node code re-check the policy at call time and return `disabled by host policy` when it is off. The four handle operations act on a handle an earlier `StartCompiledWorkflow` / `ContinueCompiledWorkflow` produced and do **not** consult the gate.

| Tool | Effect |
|---|---|
| `ExecuteNode(nodeIndex, parameter?, timeoutMs?)` | node-level EXEC/RECV; **waits for completion**, or returns an error after `timeoutMs` |
| `ExecuteNodes(nodeIndicesJson, parameter?, timeoutMs?)` | the same over a set; the timeout is per node |
| `BroadcastNode` / `ReverseBroadcastNode(..., timeoutMs?)` | the node's broadcast channels |
| `RunCompiledWorkflow(startNodeIndex, seed?, timeoutMs?)` | chain-level: compile as `Root` and drive the whole reachable sub-graph; returns when the run ends |
| `GetNodeResult(nodeIndex, seed?, timeoutMs?)` | result-level: compile the ancestor cone as `Terminal` and return that node's output |
| `StartCompiledWorkflow(startNodeIndex, seed?)` | the same chain-level path as `RunCompiledWorkflow`, but **returns a handle at once** while the run keeps going |
| `ContinueCompiledWorkflow(startNodeIndex, seed?)` | starts a run that carries on from the scope's checkpoint store instead of starting over |
| `GetCompiledRunStatus(handle, maxDataChars?)` | a run's `isRunning` / `outcome` / `isPaused` / `failures` / log tail / `logFile`; reporting a finished run retires its handle. `data` over `maxDataChars` (default 16 KiB) becomes a truncation envelope. **Not gated.** |
| `PauseCompiledRun(handle)` / `ResumeCompiledRun(handle)` | hold a run at its next node boundary, and release it. **Not gated.** |
| `StopCompiledRun(handle)` | ends the run at the next boundary with `outcome = Cancelled`, leaving its checkpoint behind for `ContinueCompiledWorkflow`. **Not gated.** |

⚙ **`GetNodeResult` reports `targetReached` explicitly.** If a router on the cone selects a sibling branch, the target is not reached: the tool returns `status:"error"` naming it, with no data. Point the router at the branch that leads to the node, then retry.

⚙ `ExecuteNode` **blocks until the node completes**, unlike the GUI's fire-and-forget `ReceiveCommand`.

⚙ **`RunCompiledWorkflow` waits for the whole run to end**, which is why the handle form exists: use `StartCompiledWorkflow` when the run may be long or need holding, poll `GetCompiledRunStatus` for its outcome, and drive it with the pause / resume / stop tools. `GetCompiledRunStatus` starts no code and only resolves a handle the start tools produced, but it is classified `Execute` and counts against `MaxWriteToolCalls`. `WithCheckpointStore` and `WithLogWriter` are the host's hooks behind `ContinueCompiledWorkflow` and `logFile`.

## Command — gated by `WithAllowedGenericCommands(...)`

`ExecuteCommandOnNode(nodeIndex, commandName, jsonParameter?)`, `ExecuteCommandById(runtimeId, commandName, jsonParameter?)`.

⚙ Calling `WithAllowedGenericCommands("Receive", "Delete")` **restricts** these two tools to that set. The `"Command"` suffix is appended automatically and matching is case-insensitive.

## Layout — the tree's registered solution

Registered under `WorkflowToolCategory.Layout`. `PreviewLayout` and `ArrangeLayout` go through `Tree.Arrange`, so they use the `IWorkflowLayoutSolver` registered on the tree's helper (or the default solver when the helper is not a host); `GetLayoutInfo` reads that registered solver. A custom solution is what the Agent sees. See [layout-agent.md](layout-agent.md).

| Tool | Effect |
|---|---|
| `GetLayoutInfo()` **R** | solver type, whether the helper hosts a custom solver, node count, and the default option values |
| `PreviewLayout(...)` **R** | solves with `DryRun` and returns every placement, cost and diagnostic without writing anchors |
| `ArrangeLayout(...)` **M** | solves and applies in one undoable `WorkflowActionPair`; returns the same shape plus applied/skipped counts |

Shared parameters: `nodeIndex?` (omit for the whole tree; when set, only the reachable subgraph moves and the start node is pinned by default), `traversal` (`Downstream` / `Upstream` / `Both`), `maxDepth` (0 = unlimited), `pinStartNode`, `positionTolerance` (0 = strict canonical), `columnGap`, `rowGap`, `maxSweeps`, `tileSize`, `measureCost`, and `pinnedNodeIndicesJson` (a JSON array such as `"[0,2]"`). The result is `{status, mode, applied, dryRun, appliedCount, skipped, cost, bounds, placements, diagnostics}`.

⚙ **Omit `nodeIndex` for a pure hierarchy.** The default pipeline treats every link as a hierarchy edge, so a graph that also carries a sequence/timeline chain interleaves that chain with the fan-out columns on a whole-tree run. Every demo menu passes a start node.

## Composite — reserved, no tools

`Composite` is a reserved category with no members: each operation stays a single component-command step, so Core's undo stack is never bypassed.

## Interaction — registered conditionally

`RequestSelection` only when `WithSelectionHandler` was set; `RequestConfirmation` only when `WithConfirmationHandler` was set. Registering one requires the handler, because the tool is useless without something to ask.

`EnterPlanMode` / `ExitPlanMode` are added outside the `Interaction` flag whenever a confirmation handler exists — both ask through it (`mode:plan` / `mode:build`) before moving the mode. `ResetToolCallLimit` is always registered and asks under `extend-tool-call-budget`.

## Adding your own tools

```csharp
scope.WithTools("<prompt text describing these tools>", myTools);
scope.WithQueryTools("<prompt text>", myReadOnlyTools);   // classified as read-only for the budgets
```

⚙ Tools added this way are always included regardless of category, and get the tracking wrapper — budget accounting, UI-thread marshalling, `ToolCalled`, auto-dirty. A connected MCP server's tools get the same wrapper when they reach the model through a provider (`WithMcps`); merging `mcp.LoadedTools` into `ChatOptions.Tools` by hand gets none of it.

⚙ Only `AIFunction` instances are wrapped. Anything else is added to the tool list untouched.
