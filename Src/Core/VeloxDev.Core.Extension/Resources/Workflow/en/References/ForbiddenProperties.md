## 🚫 Forbidden Properties and Operations

**Node creation MUST go through `CreateNode` or `CreateAndConfigureNode`.** NEVER add nodes by directly modifying the Nodes collection or any other means. The Tree's `CreateNodeCommand` performs essential initialization; bypassing it causes broken state.

The following properties are **framework-managed** and must NEVER be set or patched directly:

| Property | Reason | Correct Approach |
|---|---|---|
| `Parent` | Auto-set when node/slot is added to tree/node | Use CreateNode / CreateSlotOnNode |
| `Nodes`, `Links`, `LinksMap` | Tree collections managed by framework | Use CreateNode, ConnectSlots, DeleteNode |
| `Slots` | Node slot collection managed by framework | Use CreateSlotOnNode, DeleteSlot |
| `Targets`, `Sources` | Slot connection collections managed by framework | Use ConnectSlots, DisconnectSlots |
| `State` (on slots) | Managed by connection lifecycle | Automatic |
| `VirtualLink` | Tree internal for connection preview | Never touch |
| `RuntimeId` | Immutable identity | Read-only |
| `Helper` | Internal framework plumbing | Never touch |
| `Anchor` (on Slot) | Computed by view layout | Never set on slots |

### Source-Generator Managed Slot Properties

Node types may declare **typed slot properties** (e.g. `InputSlot`, `OutputSlot`) using `[VeloxProperty]`. These slots are **auto-created by the source generator** via lazy initialization + `CreateSlotCommand`.

- Do NOT assign, replace, or create these slots manually — they are fully lifecycle-managed.
- Only create slots dynamically via **CreateSlotOnNode** when the node type does NOT define them as typed properties.
- Accessing a typed slot property triggers auto-creation if needed — you never get "slot is null" errors.

### Slot Collection Properties

Node types may declare **slot collection properties** (e.g. `ObservableCollection<SlotViewModel> OutputSlots`). These are backed by source-generated `INotifyCollectionChanged` lifecycle hooks:

- Adding a slot triggers `OnWorkflowSlotAdded` → auto-registers with the node via `CreateSlotCommand`.
- Removing a slot triggers `OnWorkflowSlotRemoved` → auto-deletes the slot and its connections.
- **Use `AddSlotToCollection` / `RemoveSlotFromCollection`** — do NOT use `CreateSlotOnNode` for collection-managed slots.
- Use **`ListSlotProperties`** to discover whether slots are single properties or collection properties.
- **`GetNodeDetail`** output includes a `prop` field on each slot (e.g. `InputSlot`, `OutputSlots[2]`).
