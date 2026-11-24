---
name: node-creation
description: Create a node per the shipped protocol — use the pre-loaded type list when the host enabled discovery (otherwise call ListCreatableTypes), read defaults from [AgentContext] and use GetComponentContext for the full property/command table, prefer developer instructions over GetTypeSchema runtime-only zero values, keep a 30 px gap, then CreateNode and check repositioned=true
---

## 🔧 Node Creation Protocol

When the user asks to create a node, follow these steps **in order**:

1. **Choose the most appropriate node type.** If the pre-loaded customer component types are visible in your context above, choose from that list — the host enabled discovery and it is complete. If that list is absent or empty, call **`ListCreatableTypes`** to get the available types; do not invent a type name. If only one type exists, use it. If multiple exist, pick the best fit or ask the user.
2. **Read the `[AgentContext]` description** for that type (pre-loaded when the list above was). Extract the default size and any other defaults. When you need a property or command the one-line description does not cover, call **`GetComponentContext`** once for that type and use its full table; do not guess the shape from the name.
3. **Apply the `[AgentContext]` defaults.** If the description says "default size: 200×100", use `width=200, height=100` in `CreateNode`. Resolve "default" values in the order below; user-specified values override defaults.
4. **Choose a non-overlapping position**: check existing node positions (via `ListNodes` or cached knowledge) and pick a position with at least 30 px gap. The tool auto-offsets on overlap, but proactively choosing good positions produces better layouts.
   - **Empty canvas**: start the first node near the origin (e.g. `(20, 20)`) unless the user specifies a different region.
   - **Non-empty canvas**: default to placing new nodes to the right of or below the existing bounding box. If the user requests a specific quadrant or region, translate that intent using the **Coordinate System** reference.
5. **Call `CreateNode`** with the resolved type, position, and size. If the response includes `repositioned=true`, the node was auto-moved — note the actual `x`/`y`.
6. **Set `[AgentContext]`-described properties** via `PatchNodeProperties` if the user wants values different from defaults. If the type has SlotEnumerator properties, set their selector type afterwards via `SetEnumSlotCollection`.

### 📌 Default Value Resolution (CRITICAL)

1. **`[AgentContext]` developer instructions** (pre-loaded or from `GetComponentContext`). Example: "默认大小为 200*100" means width=200, height=100. **Always authoritative.**
2. **`GetComponentContext` full property table** — when the pre-loaded description does not cover the specific property.
3. **Never** use runtime values from other nodes, `GetTypeSchema` `defaultJson_runtimeOnly`, or guesswork as "defaults".

> ⚠️ `GetTypeSchema` reports `defaultJson_runtimeOnly` — runtime zero-initialized values (e.g. `Size={0,0}`). These are NOT the intended defaults.

User instructions override these defaults; never ask the user for information `[AgentContext]` already provides.
