## ⚡ Operation Ordering Protocol (CRITICAL)

You MUST follow this lifecycle ordering — the same order a human developer uses. Violating it causes silent data loss, unregistered slots, or broken connections.

### Mandatory Sequence

```
1. CreateNode             — node must exist in the tree before any further operation on it
2. PatchNodeProperties    — configure scalar properties (Title, DelayMs…)
   SetEnumSlotCollection  — set selector type on SlotEnumerator properties
3. CreateSlotOnNode /     — create or configure slots (only AFTER the node is in the tree)
   AddSlotToCollection
4. ConnectSlots /         — connect slots (BOTH endpoints must already exist)
   ConnectByProperty
5. ExecuteWork /          — run workflow logic (only after topology is complete)
   BroadcastNode
```

These are **single-step calls** — there is no composite tool. Perform each step as its own `CreateNode` / `PatchNodeProperties` / `SetEnumSlotCollection` / `CreateSlotOnNode` / `ConnectByProperty` command, in exactly this order.

### Why Order Matters

| Wrong order | What breaks |
|---|---|
| PatchNodeProperties before CreateNode | Node has no Parent; slot lifecycle hooks do not fire |
| ConnectSlots before slots exist | Slot ID lookup fails or connects wrong slot |
| SetEnumSlotCollection before CreateNode | OutputSlots are created but OnWorkflowSlotAdded cannot register them |
| ExecuteWork before connections | Work produces no downstream effects |

### ⚠ Silent Skip Warning

If you violate the ordering above, the framework's extension methods will **silently return without executing anything and without reporting any error**. You will see `status: "ok"` but the operation had **no effect**.

| Violation | Actual consequence |
|---|---|
| Operate on a node that has NOT been added to the Tree (`Parent == null`) | `DeleteNode` / `SetSlotChannel` → **silent no-op**. `CreateSlotOnNode` → adds slot **without undo registration** |
| Operate on a slot whose `Parent == null` | `DeleteSlot` → **silent no-op** |
| Operate on a link whose `Sender?.Parent?.Parent == null` | `DeleteCommand` → **silent no-op** |

**Always** obtain a valid `nodeIndex` or `runtimeId` from `ListNodes` / `CreateNode` before operating on a node's internals. Never assume a node exists unless you just created it or queried it.
