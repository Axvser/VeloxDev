## 💡 Token-Saving Tips

> **Decision Rule**: There are **no composite or bundled tools** in this toolkit. Every mutation is a step-by-step single-command call that mirrors a single GUI→Command→ViewModel interaction. Before making a tool call, still ask: *"Am I about to make several sequential calls that share the same target node or the same set of nodes?"* If yes, reduce round-trips where safe — but only via **read-side** batching (e.g. `GetFullTopology` for the whole graph) and **stable-handle reuse** (e.g. reusing `RuntimeId`s). Mutations themselves always stay one command per call.

> **State Reuse Rule**: Token saving is allowed. You may deliberately reuse cached topology instead of re-reading live state before every operation. This is safe only until a structure-changing operation invalidates the cache.

Prefer read-side batching and stable handles over unnecessary re-reading:

| Instead of… | Use… | Saves |
|---|---|---|
| ResolveSlotId × 2 + ConnectSlots | **ConnectByProperty** | 2 calls |
| ListNodes + GetNodeDetail × N + ListConnections | **GetFullTopology** | N+1 calls |

**Other efficiency tools:**

- **TakeSnapshot** / **GetChangesSinceSnapshot** — diff only, avoids re-reading everything.
- **FindNodes** — filter by type name or property value, avoids manual filtering of ListNodes.
- **SearchForward / SearchReverse / SearchAllRelative** — graph traversal without walking connections manually.
- **IsConnected** — check reachability in one call.
- **FindPath** — shortest route between two nodes.
- **ExecuteNodes** — trigger the receive path on multiple nodes at once.
- **ValidateWorkflow** — check for issues before prompting the user.
- **ListCreatableTypes** — discover available node/slot types.
- **ResolveSlotId** — get slot ID by property name without full GetNodeDetail.
- Prefer **RuntimeId** over indices for multi-step operations (stable across add/remove).
- Use **ConnectSlotsById** when you already have slot IDs, or **ConnectByProperty** when you know property names.

### Query tools — pick the right one, don't call several

| Need… | Use… |
|---|---|
| Quick orient (counts + distinct types) | **GetWorkflowSummary** |
| Compact node list | **ListNodes** |
| Full graph (nodes + slots + connections, one call) | **GetFullTopology** |
| One node's full detail | **GetNodeDetail** / **GetNodeDetailById** |
| Connections only | **ListConnections** (GetFullTopology already includes them) |
| Type shape + runtime defaults | **GetTypeSchema** |
| Developer docs of a type | **GetComponentContext** |

### Cache invalidation checklist

Refresh before the next topology-sensitive step if you just called any of these:

- `CreateNode`, `DeleteNode`, `DeleteSlot`
- `CreateSlotOnNode`, `AddSlotToCollection`, `RemoveSlotFromCollection`
- `SetEnumSlotCollection`

After these operations:

- old **node indices** may point to a different node
- old **slot indices** may point to a different slot
- old **enum-slot runtime IDs** may no longer exist

If no invalidating operation occurred, speed-first reuse of cached IDs and topology is acceptable.
