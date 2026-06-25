## ⚠️ Connection Validation (CRITICAL — prevents infinite retry loops)

The framework may **silently reject** a connection attempt. All Connect tools verify and report rejection. When you receive `status: "rejected"`, **do NOT retry the same connection** — rejection is deterministic.

### Rejection Reasons

1. **Channel incompatibility**: The sender slot's channel does not support sending, or the receiver slot's channel does not support receiving. Check the `ch` field in `GetNodeDetail` / `ListSlotProperties`.
2. **Same-node connection**: Sender and receiver belong to the same node — always rejected.
3. **Developer `ValidateConnection` rule**: The tree's helper has custom validation logic. This is opaque. If rejected for this reason, inform the user.
4. **Channel capacity limit**: One-target/one-source channels may already be at capacity. You may need to disconnect the existing connection first.
5. **Same-direction connection dedup**: When connecting two nodes that already have an existing connection in the same direction, the framework **silently deletes the old connection first**. The result is a replacement, not a parallel connection. This happens automatically — you do NOT need to call `DisconnectSlots` before `ConnectSlots` for the same node pair.

### Recovery Strategy

1. Read the `reasons` array in the rejection response.
2. If **channel issue** → check and possibly change channel with `SetSlotChannel`.
3. If **capacity** → disconnect the existing connection first with `DisconnectSlotsById`.
4. If **ValidateConnection** → inform the user; do not retry.

### Refresh policy for connection tools

- If the immediately previous steps did **not** change topology, you may reuse cached slot IDs.
- If you recently changed slot collections, deleted components, cloned nodes, or called `SetEnumSlotCollection`, refresh slot identity first.
- Do not loop on `Connect*` after a rejection unless you changed one of the preconditions: channel, endpoint, selector type, or existing capacity.
