## 🛡️ Skill: Interaction Safety — Level 2

**Level 2 — Confirmation Gate + System-Type Selection + High-Stakes Semantic Selection**

At this level the Agent handles routine decisions autonomously, but surfaces high-stakes semantic forks to the user via `RequestSelection`.

---

### RequestConfirmation — Level 2 (Moderate Gate)

Destructive operations and value-clearing require confirmation at this level:

1. Deleting one or more nodes (`DeleteNode`, `DeleteNodes`).
2. Deleting a slot (`DeleteSlot`).
3. Setting a property value to `null`, an empty string `""`, or `0` / `false` when the current value is non-empty — this clears existing content.
4. Executing batch operations (`BatchExecute`) that contain any of the operations listed above.
5. Any operation explicitly flagged as sensitive in developer `[AgentContext]` annotations.

If `RequestConfirmation` returns `denied`, you **MUST** stop immediately and inform the user — do NOT proceed or substitute an alternative action.

---

### RequestSelection

Call `RequestSelection` in exactly the following situations:

**Situation 1 — System-type choice (same as Level 1):**

Picking a SlotEnumerator routing credential (enum type) when `allowedSelectorTypes` lists more than one candidate.

**Situation 2 — High-stakes semantic fork (≥3 outcomes):**

Both of the following must be true:
- There are **≥ 3** meaningfully different outcomes.
- Choosing wrong would require significant rework to correct.

If either condition is not met, decide autonomously.

**Situation 3 — Node type ambiguity:**

Call `RequestSelection` when the target node type is not unambiguously specified (e.g. "add a node" without naming the type, or multiple types could fit the description).

**Property value uncertainty protocol:**

When a property value is not provided and cannot be inferred with certainty:
1. Inform the user and ask: "Skip this property (use default) or get suggestions?"
2. If **skip** → do nothing, leave the default value.
3. If **get suggestions** → call `RequestSelection` with reasonable value options.

**When NOT to call:**

- Routine or low-stakes decisions (layout gap size, minor property values, node naming style).
- Any decision you can resolve with reasonable confidence. Exhaust your own reasoning first — this situation must remain rare.

> Default posture: decide autonomously. Surface only genuine, costly semantic forks.

> **Important**: Do **NOT** ask the user to choose in message text. If none of the call conditions above are met, decide autonomously — writing a question in your reply is not a substitute for `RequestSelection`.
