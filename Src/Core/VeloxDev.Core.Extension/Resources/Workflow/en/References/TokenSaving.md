## 💡 Token-Saving Tips

> **Decision Rule**: Before making any tool call, ask: *"Am I about to make 2 or more sequential calls that share the same target node or the same set of nodes?"* If yes, check the table below first — a composite or BatchExecute almost always applies.

Prefer composite tools over multi-step sequences to reduce round-trips:

| Instead of… | Use… | Saves |
|---|---|---|
| CreateNode + PatchNodeProperties + SetEnumSlotCollection | **CreateAndConfigureNode** | 2 calls |
| ResolveSlotId × 2 + ConnectSlots | **ConnectByProperty** | 2 calls |
| DeleteNode × N | **DeleteNodes** | N−1 calls |
| SetNodePosition × N | **ArrangeNodes** | N−1 calls |
| ListNodes + GetNodeDetail × N + ListConnections | **GetFullTopology** | N+1 calls |

**Other efficiency tools:**

- **BatchExecute** — combine any operations not covered by composites into one call.
- **TakeSnapshot** / **GetChangesSinceSnapshot** — diff only, avoids re-reading everything.
- **FindNodes** — filter by type name or property value, avoids manual filtering of ListNodes.
- **SearchForward / SearchReverse / SearchAllRelative** — graph traversal without walking connections manually.
- **IsConnected** — check reachability in one call.
- **FindPath** — shortest route between two nodes.
- **DisconnectAllFromNode** — clear all connections in one call.
- **AlignNodes / DistributeNodes / AutoLayout** — layout in one call instead of N SetNodePositions.
- **ExecuteWorkOnNodes** — trigger work on multiple nodes at once.
- **BulkPatchNodes** — same property change across multiple nodes.
- **ValidateWorkflow** — check for issues before prompting the user.
- **ListCreatableTypes** — discover available node/slot types.
- **ResolveSlotId** — get slot ID by property name without full GetNodeDetail.
- Prefer **RuntimeId** over indices for multi-step operations (stable across add/remove).
- Use **ConnectSlotsById** when you already have slot IDs, or **ConnectByProperty** when you know property names.
