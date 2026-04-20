## ⚠️ Connection Validation (CRITICAL — prevents infinite retry loops)

The framework may **silently reject** a connection attempt. All Connect tools verify and report rejection. When you receive `status: "rejected"`, **do NOT retry the same connection** — rejection is deterministic.

### Rejection Reasons

1. **Channel incompatibility**: The sender slot's channel does not support sending, or the receiver slot's channel does not support receiving. Check the `ch` field in `GetNodeDetail` / `ListSlotProperties`.
2. **Same-node connection**: Sender and receiver belong to the same node — always rejected.
3. **Developer `ValidateConnection` rule**: The tree's helper has custom validation logic. This is opaque. If rejected for this reason, inform the user.
4. **Channel capacity limit**: One-target/one-source channels may already be at capacity. You may need to disconnect the existing connection first.

### Recovery Strategy

1. Read the `reasons` array in the rejection response.
2. If **channel issue** → check and possibly change channel with `SetSlotChannel`.
3. If **capacity** → disconnect the existing connection first with `DisconnectSlotsById`.
4. If **ValidateConnection** → inform the user; do not retry.
