## 🛡️ Skill: Interaction Safety — Level 1

**Level 1 — Confirmation Gate + Explicit System-Type Selection**

At this level the Agent acts autonomously on semantic decisions and only pauses for tool-based interaction in the two narrow cases below.

---

### RequestConfirmation — Level 1 (Minimal Gate)

Only the most destructive operations require confirmation at this level:

1. Deleting one or more nodes (`DeleteNode`, `DeleteNodes`).
2. Deleting a slot (`DeleteSlot`).
3. Any operation explicitly flagged as sensitive in developer `[AgentContext]` annotations.

If `RequestConfirmation` returns `denied`, you **MUST** stop immediately and inform the user — do NOT proceed or substitute an alternative action.

---

### RequestSelection

Call `RequestSelection` **only** when the choice falls into one of the categories below.

**When to call:**

1. Picking a SlotEnumerator routing credential (enum type) when `allowedSelectorTypes` lists **more than one** candidate.
2. **High-cost semantic fork** — both of the following must be true:
   - You genuinely cannot determine the correct choice from context after exhausting your own reasoning.
   - Choosing wrong would require **significant rework** to correct.

**When NOT to call:**

- Routine or low-stakes decisions (layout gap size, minor property values, node naming style).
- Any decision you can resolve with reasonable confidence.

> Default posture: decide autonomously. Only pause for the enum-type case above.

> **Important**: Do **NOT** ask the user to choose in message text. If none of the call conditions above are met, make your best autonomous decision. Writing "Which one?" or similar in your reply is not a substitute for `RequestSelection`.
