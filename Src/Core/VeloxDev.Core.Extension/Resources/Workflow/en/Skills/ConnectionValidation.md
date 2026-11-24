---
name: connection-validation
description: Recover from a rejected ConnectSlots, ConnectByProperty or ConnectEnumSlot, not a retry — read the status rejected reasons array to tell apart channel incompatibility (SetSlotChannel), same-node endpoints and developer ValidateConnection rules, and know that same-direction dedup replaces a connection rather than rejecting it
---

## ⚠️ Connection Validation

The framework may reject a connection attempt. All Connect tools verify and report the outcome; on `status: "rejected"`, **do not retry the same connection** — rejection is deterministic.

### Rejection Reasons

1. **Channel incompatibility** — the sender's channel cannot send, or the receiver's cannot receive. Check `ch` in `GetNodeDetail` / `ListSlotProperties`.
2. **Same-node connection** — sender and receiver belong to the same node; always rejected.
3. **Developer `ValidateConnection` rule** — the tree helper's custom validation refused it. Treat the rule as opaque: tell the user, do not retry.
4. **The fallback reason** — when the reply only says `"developer ValidateConnection rule or channel capacity limit"`, it is the developer rule above. Channels that allow one connection per direction evict the previous connection automatically, so capacity cannot be the cause.

### Not a rejection: same-direction dedup

Connecting two nodes that already share a same-direction connection silently removes the old connection first — the result is a replacement, not a parallel connection. No `DisconnectSlots` call is needed before `ConnectSlots` for the same node pair.

### Recovery

1. Read the `reasons` array.
2. **Channel issue** → inspect `ch` and change it with `SetSlotChannel` when the channel is wrong for the intended role.
3. **`ValidateConnection`** → tell the user; do not retry.
4. After a successful `ConnectSlots`, its reply names `senderProperty` / `receiverProperty` — use `ConnectByProperty` next time.

### Refresh policy for connection tools

- If the immediately previous steps did **not** change topology, cached slot IDs may be reused.
- After changing slot collections, deleting components, cloning nodes, or calling `SetEnumSlotCollection`, refresh slot identity first.
- Do not loop on `Connect*` after a rejection unless you changed a precondition: channel, endpoint, selector type, or the existing connection.
