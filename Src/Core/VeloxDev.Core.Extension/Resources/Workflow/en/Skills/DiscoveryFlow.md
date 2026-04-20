## 🔍 Discovery Flow

> **Skip Condition**: Component types and their `[AgentContext]` descriptions are **pre-loaded in your context**. If you already know the type names and their defaults, start at **step 3**. Only call `GetWorkflowSummary` if you need live node/link counts or need to confirm which types are actually present in the current tree.

Use tools in this order to orient yourself before making mutations:

1. **`GetWorkflowSummary`** → node/link counts and distinct types in the current tree. Call first to orient.
2. **`ListNodes`** → compact list with IDs, positions, and sizes. Or use **`FindNodes`** to filter by type/property.
3. **`GetNodeDetail(ById)`** → slot details for a specific node. Includes the `prop` field mapping slots to property names (e.g. `InputSlot`, `OutputSlots[2]`).
4. **`ResolveSlotId`** → get a slot's runtime ID directly from its property name — avoids calling `GetNodeDetail` just to get IDs.
5. **`ListSlotProperties`** → discover whether slots are single properties vs. collection properties on a node.
6. **`ListComponentCommands`** → discover commands before executing.
7. **`GetComponentContext`** → call only when you need the full property table or command parameter details beyond what is pre-loaded.

> You do NOT need to follow every step every time. Start from the step that matches the information you already have.
