# The tool surface

60 built-in tools, grouped by the flags of `WorkflowToolCategory` — **62 once the two interaction tools register**, which takes both handlers configured *and* `WithInteractionSafety > 0`. Names are the C# method names and are passed to the model verbatim.

**R** = read-only: never dirties the tree, counts against `MaxReadToolCalls`. **M** = mutating: counts against `MaxWriteToolCalls`. **\*** = mutating and **not undoable**.

## Query — always registered, all read-only

| Tool | Effect |
|---|---|
| `GetWorkflowSummary` | tree id, node and link counts, distinct node types. The intended first call. |
| `GetFullTopology` | the whole graph — nodes, slots and links — in one call |
| `ListNodes` | compact JSON per node: `{i, id, t, x, y, l, w, h, slots, …scalars}` |
| `GetNodeDetail(nodeIndex)` | one node with its slots and connections |
| `GetNodeDetailById(runtimeId)` | the same, addressed by a stable id |
| `FindNodes(typeName, propertyName, propertyValue)` | filtered node list, cheaper than listing everything |
| `ListConnections` | visible links as `{id, sid, rid}` |
| `GetLinkDetail(linkId)` | a link's endpoint slots and their parent nodes |
| `ResolveSlotId(nodeIndex, propertyName, collectionIndex)` | a slot's `RuntimeId` without fetching the whole node |
| `ListSlotProperties(nodeIndex)` | single slots, slot collections and enumerators, with `allowedSelectorTypes` |
| `GetEnumSlotByValue(nodeIndex, propertyName, conditionValue)` | the slot inside an enumerator for a given enum or bool value |
| `GetTypeSchema(fullTypeName)` | a .NET type as a JSON schema — properties, types, current values |
| `GetComponentContext(fullTypeName, language)` | the runtime `[AgentContext]` documentation for a type |
| `ListComponentCommands(nodeIndex)` | command names and parameter types |
| `ListCreatableTypes` | concrete node and slot types discovered by the auto-discovery registrations |
| `ValidateWorkflow` | warnings for zero-size nodes, isolated nodes, slot-less nodes and duplicate links |
| `CompileWorkflow(startNodeIndex)` | the Root-role plan: segments, branches, `Order` / `ChainIndex` / `Offset` |
| `CompileNodeResult(nodeIndex)` | the Terminal-role reverse compile of that node's ancestor cone |
| `GetCompileStatus` | the current compile identity without recompiling |
| `GetExecutionLog` | the tree's execution log — direct node executions only |

## Analytics

A category of its own with exactly one member — its flag is neither `Query` nor `Mutation`:

| Tool | Effect |
|---|---|
| `GetNodeStatistics(nodeIndex)` | in/out degree, connected node ids, slot count |

⚙ **It is read-only** (it counts against `MaxReadToolCalls`), so it behaves like a Query tool — but it is **not registered by the `Query` flag**. A host that passes `WorkflowToolCategory.Query` alone does not get it. If you filter categories at all, name `Analytics` explicitly alongside `Query`.

## Graph traversal — read-only

`SearchForward`, `SearchReverse`, `SearchAllRelative` (BFS with an optional type filter and depth bound), `IsConnected(source, target, direction)`, `FindPath(source, target)`.

## State

`TakeSnapshot` (R), `GetChangesSinceSnapshot` (R), `MarkDirty` (M) — the only writing member.

## Mutation — all undoable unless marked

| Tool | Effect |
|---|---|
| `CreateNode` | creates a node of a discovered type; offsets by 30 px and sizes from the type's default, else 300 × 260 |
| `DeleteNode` | an atomic four-phase cascade — connections, slots, node, index |
| `CreateSlotOnNode` / `AddSlotToCollection` / `RemoveSlotFromCollection` | slot lifecycle |
| `DeleteSlot` | removes a slot and its connections |
| `ConnectSlots` / `ConnectSlotsById` | connect two slots |
| `ConnectByProperty` | connect by node + property name — the preferred form |
| `DisconnectSlots` / `DisconnectSlotsById` | remove a connection |
| `SetSlotChannel` | change a slot's `SlotChannel` |
| `SetEnumSlotCollection` | replace a slot enumerator's selector set. The only way to change a `[SlotSelectors]` property. |
| `ConnectEnumSlot` / `SetEnumSlotChannel` | the enumerator equivalents of the two above |
| `Undo` / `Redo` / `ClearHistory` | history. `ClearHistory` drops the trail; the canvas is untouched. |
| `MoveNode`* / `SetNodePosition`* / `ResizeNode`* | mirror the GUI drag: direct writes, **no undo entry** |
| `PatchNodeProperties`* / `PatchComponentById`* | direct property writes, **not undoable by design** |

⚙ **A rejected connection is silent at the framework level**, so the connect tools re-verify against `LinksMap` and return `status:"rejected"` with `reasons`, a `hint` and often a `preferredAlternative`. A model that ignores the status will believe it connected two slots when it did not.

⚙ **A same-direction link silently replaces the previous one** when the channel is `One*`. That is not a rejection and comes back as success.

⚙ **`PatchNodeProperties` refuses `[SlotSelectors]` properties** with an explicit pointer at `SetEnumSlotCollection`.

## Execution — gated by `WithAllowNodeExecution(true)`

Each of these re-checks the policy at call time and returns `disabled by host policy` when it is off.

| Tool | Effect |
|---|---|
| `ExecuteNode(nodeIndex, parameter?)` | node-level EXEC/RECV; **waits for completion** |
| `ExecuteNodes(nodeIndicesJson, parameter?)` | the same over a set |
| `BroadcastNode` / `ReverseBroadcastNode` | the node's broadcast channels |
| `RunCompiledWorkflow(startNodeIndex, seed?)` | chain-level: compile as `Root` and drive the whole reachable sub-graph |
| `GetNodeResult(nodeIndex, seed?)` | result-level: compile the ancestor cone as `Terminal` and return that node's output |

⚙ **`GetNodeResult` reports `TargetReached` explicitly.** Read it — `Data` can hold the last driven node's value even when the requested target was never reached, because a router took a sibling branch. That is the reverse-compilation contract, not a failure to retry blindly.

⚙ `ExecuteNode` **blocks until the node completes**, unlike the GUI's fire-and-forget `ReceiveCommand`.

## Command — gated by `WithAllowedGenericCommands(...)`

`ExecuteCommandOnNode(nodeIndex, commandName, jsonParameter?)`, `ExecuteCommandById(runtimeId, commandName, jsonParameter?)`.

⚙ Calling `WithAllowedGenericCommands("Receive", "Delete")` **restricts** these two tools to that set; it does not merely permit those two on top of everything. The `"Command"` suffix is appended automatically and matching is case-insensitive.

## Layout and Composite — reserved, no tools

They exist as categories with zero members. Layout is done node by node through `MoveNode` / `SetNodePosition`; there are deliberately no bundled layout gestures, so that Core's undo stack is never bypassed.

## Interaction — registered conditionally

`RequestSelection` only when `WithSelectionHandler` was set; `RequestConfirmation` only when `WithConfirmationHandler` was set; both only when `IsInteractionAllowed` (`WithInteractionSafety > 0`).

## Adding your own tools

```csharp
scope.WithTools("<prompt text describing these tools>", myTools);
scope.WithQueryTools("<prompt text>", myReadOnlyTools);   // classified as read-only for the budgets
```

⚙ Tools added this way are always included regardless of category, and get the tracking wrapper — budget accounting, UI-thread marshalling, `ToolCalled`, auto-dirty. **MCP server tools do not**; see [mcp.md](mcp.md).

⚙ Only `AIFunction` instances are wrapped. Anything else is added to the tool list untouched.
