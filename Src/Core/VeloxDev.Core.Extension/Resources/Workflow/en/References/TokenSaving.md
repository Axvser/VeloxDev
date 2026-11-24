## 💡 Token-Saving Tips

> **Reads can be batched; mutations cannot.** There is no composite tool — every mutation is one command per call (see OperationOrdering). Savings come from **read-side** batching (e.g. `GetFullTopology` for the whole graph) and **stable-handle reuse** (`RuntimeId`s), never from bundling edits.

Prefer read-side batching and stable handles over unnecessary re-reading:

| Instead of… | Use… | Saves |
|---|---|---|
| ResolveSlotId × 2 + ConnectSlots | **ConnectByProperty** | 2 calls |
| ListNodes + GetNodeDetail × N + ListConnections | **GetFullTopology** | N+1 calls |

> **Large results are capped, not delivered whole.** `GetFullTopology` takes `maxNodes`, `GetExecutionLog` takes `maxEntries`, and `ListNodes`/`FindNodes` take `maxNodes`/`maxMatches`; a capped reply reports `truncated` and the total. Every tool result is also replaced by a truncation envelope past the host's character limit. When a reply says it was truncated, narrow the query — do not repeat the same call.

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
| Type shape + runtime-only values | **GetTypeSchema** |
| Developer docs of a type | **GetComponentContext** |

If a structure-changing call happened since the cache was read, refresh first (the list is in the Discovery Flow skill). Otherwise reuse cached IDs and topology.
