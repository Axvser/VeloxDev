## 🔍 Discovery Flow

> **Skip Condition**: Component types and their `[AgentContext]` descriptions are **pre-loaded in your context**. If you already know the type names and their defaults, start at the step that matches the information you are missing. Only call `GetWorkflowSummary` when you need live node/link counts, need to confirm which types are currently present, or your cached topology may be stale.

Choose a refresh strategy before making mutations:

### Strategy A — Safety-first

- Refresh live state before most mutations.
- Best for weaker models, long tool chains, or when another actor may have modified the tree.
- Typical sequence:
  1. **`GetWorkflowSummary`**
  2. **`ListNodes`** or **`FindNodes`**
  3. **`GetNodeDetail(ById)`** / **`ListSlotProperties`**
  4. mutate

### Strategy B — Speed-first

- Reuse cached state aggressively.
- Best when the Agent is confident that no one else changed the tree and the next step only depends on data it already owns.
- Refresh only when a prior operation invalidates old handles or when a tool response indicates rejection / mismatch.

### Recommended discovery tools

1. **`GetWorkflowSummary`** → node/link counts and distinct types in the current tree.
2. **`ListNodes`** → compact list with IDs, positions, and sizes. Or use **`FindNodes`** to filter by type/property.
3. **`GetNodeDetail(ById)`** → slot details for a specific node. Includes the `prop` field mapping slots to property names (e.g. `InputSlot`, `OutputSlots[2]`).
4. **`ResolveSlotId`** → get a slot's runtime ID directly from its property name — avoids calling `GetNodeDetail` just to get IDs.
5. **`ListSlotProperties`** → discover whether slots are single properties vs. collection properties on a node.
6. **`ListComponentCommands`** → discover commands before executing.
7. **`GetComponentContext`** → call only when you need the full property table or command parameter details beyond what is pre-loaded.

### Mandatory refresh points

Even in speed-first mode, refresh live state after operations that may invalidate cached indices or slot IDs:

- `CreateNode`, `DeleteNode`, `DeleteSlot`, `CloneNodes`
- `CreateSlotOnNode`, `AddSlotToCollection`, `RemoveSlotFromCollection`
- `SetEnumSlotCollection` (old enum-slot IDs and connections become obsolete)
- any user action or external process that may have changed the tree outside the current tool chain

> You do NOT need to follow every step every time. Preserve the Agent's freedom to trade safety for speed, but never reuse stale indices or stale slot IDs after structure-changing operations.
