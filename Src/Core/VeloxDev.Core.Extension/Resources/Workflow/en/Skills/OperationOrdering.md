## ⚡ Operation Ordering Protocol (CRITICAL)

You MUST follow this lifecycle ordering — the same order a human developer uses. Violating it causes silent data loss, unregistered slots, or broken connections.

### Mandatory Sequence

```
1. CreateNode             — node must exist in the tree before any further operation on it
   (CreateAndConfigureNode combines steps 1–3 into one call)
2. PatchNodeProperties    — configure scalar properties (Title, DelayMs…)
   SetEnumSlotCollection  — set selector type on SlotEnumerator properties
3. CreateSlotOnNode /     — create or configure slots (only AFTER the node is in the tree)
   AddSlotToCollection
4. ConnectSlots /         — connect slots (BOTH endpoints must already exist)
   ConnectByProperty
5. ExecuteWork /          — run workflow logic (only after topology is complete)
   BroadcastNode
```

### Why Order Matters

| Wrong order | What breaks |
|---|---|
| PatchNodeProperties before CreateNode | Node has no Parent; slot lifecycle hooks do not fire |
| ConnectSlots before slots exist | Slot ID lookup fails or connects wrong slot |
| SetEnumSlotCollection before CreateNode | OutputSlots are created but OnWorkflowSlotAdded cannot register them |
| ExecuteWork before connections | Work produces no downstream effects |

### BatchExecute Ordering

Operations inside a **BatchExecute** call are executed **sequentially in array order**. You MUST list them in the correct lifecycle order: CreateNode → Patch → Slot → Connect → Execute.

### Common BatchExecute Patterns

**Pattern A — Create, configure, and connect two nodes in one call:**
```json
BatchExecute([
  { "tool": "CreateNode",           "type": "MyNodeType", "x": 100, "y": 100, "width": 200, "height": 100 },
  { "tool": "CreateNode",           "type": "MyNodeType", "x": 400, "y": 100, "width": 200, "height": 100 },
  { "tool": "PatchNodeProperties",  "nodeId": "$0.id", "properties": { "Title": "Source" } },
  { "tool": "PatchNodeProperties",  "nodeId": "$1.id", "properties": { "Title": "Target" } },
  { "tool": "ConnectByProperty",    "sourceNodeId": "$0.id", "sourceProperty": "OutputSlot",
                                    "targetNodeId": "$1.id", "targetProperty": "InputSlot" }
])
```

**Pattern B — Bulk-reconfigure existing nodes (no creation):**
```json
BatchExecute([
  { "tool": "PatchNodeProperties", "nodeId": "id-A", "properties": { "DelayMs": 500 } },
  { "tool": "PatchNodeProperties", "nodeId": "id-B", "properties": { "DelayMs": 500 } },
  { "tool": "ConnectByProperty",   "sourceNodeId": "id-A", "sourceProperty": "OutputSlot",
                                   "targetNodeId": "id-B", "targetProperty": "InputSlot" }
])
```

> Use `CreateAndConfigureNode` instead of `CreateNode + PatchNodeProperties + SetEnumSlotCollection` whenever possible — it is already a 3-in-1 composite and further reduces BatchExecute array length.
