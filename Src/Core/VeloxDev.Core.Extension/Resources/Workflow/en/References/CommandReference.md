## ⚠️ Command-First Rule

**All mutations to the workflow MUST go through commands (IVeloxCommand).** Do NOT call Helper methods directly or set command-backed properties via reflection. Commands handle UI thread dispatching, undo/redo tracking, and view synchronization. Bypassing them causes invisible changes.

| Operation | Tool | Command Used |
|---|---|---|
| Move node | MoveNode | MoveCommand |
| Position node | SetNodePosition | SetAnchorCommand |
| Resize node | ResizeNode | SetSizeCommand |
| Create node | CreateNode | Tree.CreateNodeCommand |
| Create slot | CreateSlotOnNode | Node.CreateSlotCommand |
| Connect (preferred) | **ConnectByProperty** | Tree.Send/ReceiveConnectionCommand |
| Connect (by index) | ConnectSlots | Tree.Send/ReceiveConnectionCommand (⚠️ indices unstable — see below) |
| Connect (by ID) | ConnectSlotsById | Tree.Send/ReceiveConnectionCommand (⚠️ IDs stale after SlotEnumerator reconfig) |
| Disconnect | DisconnectSlots | Link.DeleteCommand |
| Delete node | DeleteNode | Node.DeleteCommand |
| Delete slot | DeleteSlot | Slot.DeleteCommand |
| Broadcast | BroadcastNode | Node.BroadcastCommand |
| Any other | ExecuteCommandOnNode / ExecuteCommandById | Resolved by name |
| Patch custom props | PatchNodeProperties / PatchComponentById | Direct (non-command props only) |
| Add slot to collection | AddSlotToCollection | Collection lifecycle (OnWorkflowSlotAdded) |
| Remove slot from collection | RemoveSlotFromCollection | Collection lifecycle (OnWorkflowSlotRemoved) |
| Set enum on slot collection | SetEnumSlotCollection | Clears + rebuilds enum-driven collection |
| Find nodes by filter | FindNodes | Introspection (no mutation) |
| Resolve slot ID by property | ResolveSlotId | Introspection (no mutation) |
| **Create + configure node** | **CreateAndConfigureNode** | CreateNode + Patch + SetEnum in one call |
| **Delete multiple nodes** | **DeleteNodes** | Node.DeleteCommand × N |
| **Position multiple nodes** | **ArrangeNodes** | SetAnchorCommand × N |
| **Full graph snapshot** | **GetFullTopology** | All nodes + slots + connections in one call |
| **Mark workflow dirty** | **MarkDirty** | TreeHelper.MarkDirty |
| **Reverse broadcast** | **ReverseBroadcastNode** | Node.ReverseBroadcastCommand |
| **Search downstream** | **SearchForward** | BFS via SearchForwardNodes extension |
| **Search upstream** | **SearchReverse** | BFS via SearchReverseNodes extension |
| **Search both directions** | **SearchAllRelative** | BFS via SearchAllRelativeNodes extension |
| **Check connectivity** | **IsConnected** | Transitive reachability check |
| **Find path** | **FindPath** | Shortest forward path between two nodes |
| Disconnect by IDs | DisconnectSlotsById | Link.DeleteCommand |
| Disconnect all from slot | DisconnectAllFromSlot | Bulk Link.DeleteCommand |
| Disconnect all from node | DisconnectAllFromNode | Bulk Link.DeleteCommand |
| Replace connection | ReplaceConnection | Atomic disconnect + reconnect |
| Set slot channel | SetSlotChannel | Slot.SetChannelCommand |
| Inspect link | GetLinkDetail | Introspection (no mutation) |
| Execute work on many | ExecuteWorkOnNodes | WorkCommand × N |
| Patch many nodes | BulkPatchNodes | Same patch applied to N nodes |
| Align nodes | AlignNodes | SetAnchorCommand × N (left/right/top/bottom/center) |
| Distribute nodes | DistributeNodes | Equalize spacing along axis |
| Auto topology layout | AutoLayout | Sugiyama-style layered layout |
| Node statistics | GetNodeStatistics | In/out degree, connected nodes |
| List creatable types | ListCreatableTypes | Discover available node/slot types |
| Validate workflow | ValidateWorkflow | Check for issues (zero size, isolated nodes) |

## Slot Connection Safety Rules

> `SlotEnumerator<T>` members inside Node components are created by the **source generator** during `InitializeWorkflow()`. Reconfiguring via `SetEnumSlotCollection` rebuilds all enum-driven slots, invalidating all **slot indices and IDs**.

### Priority

1. **Always prefer `ConnectByProperty`** — connects by stable property name, valid throughout the component lifetime.
2. **For SlotEnumerator slots use `ConnectEnumSlot`** — routes by condition value (enum member name or True/False).
3. **Avoid `ConnectSlots` (index-based)** — use only after confirming stable indices via `ListSlotProperties`. On success the response includes `senderProperty`/`receiverProperty` — switch to `ConnectByProperty` next time.
4. **Use `ConnectSlotsById` carefully** — IDs go stale after `SetEnumSlotCollection`; safe only if IDs were obtained from `GetFullTopology` in the same task without an intervening reconfig.

### Handling Connection Failure

- When the response contains `"status":"rejected"`, **do NOT retry with the same arguments**.
- Inspect the `reasons` field to diagnose channel incompatibility or `ValidateConnection` constraints.
- If the response includes `senderProperty` / `receiverProperty`, switch to `ConnectByProperty` immediately.

## AgentContext Property Rule

Properties annotated with `[AgentContext]` are **explicitly intended by the developer for Agent read/write**.

- If such a property has **no backing command** (e.g. `Title`, `DelayMilliseconds`) → use **PatchNodeProperties** / **PatchComponentById**.
- If such a property has **a backing command** (e.g. `Size` → `SetSizeCommand`) → use the corresponding tool (e.g. **ResizeNode** or **CreateNode** with width/height).
- The developer's `[AgentContext]` description may include **default values** (e.g. "default size: 200×100"). Respect and use these values.
- **BEFORE creating or configuring a component type for the first time**, call **GetComponentContext** to read these annotations (or rely on pre-loaded descriptions if available).

## Context Caching

You do NOT need to call `GetWorkflowSummary` or `GetComponentContext` every turn. Once you have read a type's context, remember it for the rest of the conversation. Only re-read if the user indicates types have changed.

## Dirty Marking Rule

Dirty-marking behavior is controlled by `WorkflowAgentScope.WithAutoMarkDirty(bool)`:

| Mode | Configuration | Agent Behavior |
|---|---|---|
| **Manual (default)** | `WithAutoMarkDirty(false)` or omitted | Call **`MarkDirty` exactly once** at the end of a mutation task |
| **Automatic** | `WithAutoMarkDirty(true)` | Framework marks dirty after every mutation tool call — **no need to call `MarkDirty`** |

- Pure query tools (`ListNodes`, `FindNodes`, `GetFullTopology`, etc.) **never trigger** auto dirty marking.
- In automatic mode the `MarkDirty` tool can still be called explicitly without side effects.
