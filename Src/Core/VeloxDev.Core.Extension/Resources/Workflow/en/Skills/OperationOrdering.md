---
name: operation-ordering
description: Follow the mutation lifecycle CreateNode, PatchNodeProperties/SetEnumSlotCollection, CreateSlotOnNode/AddSlotToCollection, ConnectSlots/ConnectByProperty, ExecuteNode/BroadcastNode — why each inversion breaks slots; command paths no-op silently on an unmounted component while the patcher rejects
---

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
5. ExecuteNode /          — run node logic via the receive path (only after topology is complete)
   BroadcastNode
```

These are **single-step calls** — there is no composite tool. Perform each step as its own `CreateNode` / `PatchNodeProperties` / `SetEnumSlotCollection` / `CreateSlotOnNode` / `ConnectByProperty` command, in exactly this order.

### Why Order Matters

| Wrong order | What breaks |
|---|---|
| PatchNodeProperties before CreateNode | The patch is rejected: the target has no parent chain, so it is not mounted in a tree |
| ConnectSlots before slots exist | Slot ID lookup fails or connects wrong slot |
| SetEnumSlotCollection before CreateNode | Selector state is applied detached — no tree registration and no undo entry |
| ExecuteNode before connections | Receive produces no downstream effects |

### ⚠ Silent Skip Warning

In release builds, the command-path extension methods below **return without executing anything and without reporting an error** — the tool still answers `status: "ok"`, but nothing happened. `PatchNodeProperties` is the exception: it rejects an unmounted target with `status: "error"`.

| Violation | Actual consequence |
|---|---|
| Operate on a node that has NOT been added to the Tree (`Parent == null`) | `DeleteNode` / `SetSlotChannel` → **silent no-op**. `CreateSlotOnNode` → adds slot **without undo registration** |
| Operate on a slot whose `Parent == null` | `DeleteSlot` → **silent no-op** |
| Operate on a link whose `Sender?.Parent?.Parent == null` | `DeleteCommand` → **silent no-op** |

**Always** obtain a valid `nodeIndex` or `runtimeId` from `ListNodes` / `CreateNode` before operating on a node's internals. Never assume a node exists unless you just created it or queried it.
