## ⚙️ Framework Behaviors

- **Delete cascades**: Deleting a node automatically deletes all its child slots and their connections. No need to delete slots or links individually before deleting a node.
- **Node size**: `CreateNode` with `width`/`height` of 0 uses the type's `[DefaultSize]`, else 300×260. Pass explicit values to override, or use **ResizeNode** later. A node must not have size (0, 0).
- **Node positioning**: `CreateNode` auto-offsets the position to avoid overlapping existing nodes (30 px padding). The response carries the final `x`/`y` and `repositioned=true` when it moved — read the response for the final position.
- **Topology-changing operations invalidate stale handles**: After creating, deleting, cloning, rebuilding enum slots, or mutating slot collections, previously cached node indices, slot indices, and some slot runtime IDs may no longer be valid. Refresh before the next topology-sensitive step.
