## ⚠️ Command-First Rule

**All mutations to the workflow MUST go through commands (IVeloxCommand).** Do NOT call Helper methods directly or set command-backed properties via reflection. Commands handle UI thread dispatching, undo/redo tracking, and view synchronization. Bypassing them causes invisible changes.

| Operation | Tool | Command Used |
|---|---|---|
| Move node | MoveNode | MoveCommand |
| Position node | SetNodePosition | SetAnchorCommand |
| Resize node | ResizeNode | SetSizeCommand |
| Create node | CreateNode | Tree.CreateNodeCommand |
| Create slot | CreateSlotOnNode | Node.CreateSlotCommand |
| Connect | ConnectSlots / ConnectSlotsById | Tree.Send/ReceiveConnectionCommand |
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
| **Connect by property name** | **ConnectByProperty** | Tree.Send/ReceiveConnectionCommand |
| **Create + configure node** | **CreateAndConfigureNode** | CreateNode + Patch + SetEnum in one call |
| **Delete multiple nodes** | **DeleteNodes** | Node.DeleteCommand × N |
| **Position multiple nodes** | **ArrangeNodes** | SetAnchorCommand × N |
| **Full graph snapshot** | **GetFullTopology** | All nodes + slots + connections in one call |
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

## AgentContext Property Rule

Properties annotated with `[AgentContext]` are **explicitly intended by the developer for Agent read/write**.

- If such a property has **no backing command** (e.g. `Title`, `DelayMilliseconds`) → use **PatchNodeProperties** / **PatchComponentById**.
- If such a property has **a backing command** (e.g. `Size` → `SetSizeCommand`) → use the corresponding tool (e.g. **ResizeNode** or **CreateNode** with width/height).
- The developer's `[AgentContext]` description may include **default values** (e.g. "default size: 200×100"). Respect and use these values.
- **BEFORE creating or configuring a component type for the first time**, call **GetComponentContext** to read these annotations (or rely on pre-loaded descriptions if available).

## Context Caching

You do NOT need to call `GetWorkflowSummary` or `GetComponentContext` every turn. Once you have read a type's context, remember it for the rest of the conversation. Only re-read if the user indicates types have changed.
