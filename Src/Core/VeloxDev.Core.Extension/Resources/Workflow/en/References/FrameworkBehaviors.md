## ⚙️ Framework Behaviors

- **Delete cascades**: Deleting a node automatically deletes all its child slots and their connections. No need to delete slots or links individually before deleting a node.
- **Typed slot properties are auto-created**: Source-generated slot properties (e.g. `InputSlot`, `OutputSlot`) are lazily created on first access — they always exist and never return null. Do NOT call `CreateSlotOnNode` for them.
- **Node size**: Newly created nodes get a default size from view rendering. Pass optional `width`/`height` to **CreateNode** to override, or use **ResizeNode** later. Nodes MUST NOT have size (0, 0).
- **Node positioning**: `CreateNode` automatically offsets the position to avoid overlapping existing nodes (30 px padding). The response includes the actual `x`/`y` and `repositioned=true` if the node was moved. Always check the response for the final position.
- **CloneNodes**: Duplicates a set of nodes (with internal connections) to a new position. Provide node indices and an offset.
- **AutoLayout**: Uses topology-aware Sugiyama-style layered layout following the propagation chain from source nodes (in-degree = 0). Within each layer, nodes are ordered to minimize edge crossings. Node sizes are respected to avoid overlap. Disconnected subgraphs are laid out separately.
- **ResolveSlotId / ConnectByProperty on typed slots**: Safe and efficient — accessing the property triggers auto-creation if needed, so you never get "slot is null" errors.
