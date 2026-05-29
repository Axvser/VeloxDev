## 🛡️ Skill: Interaction Safety — Level 2

**Level 2 — Confirmation Gate + System-Type Selection + High-Stakes Semantic Selection**

At this level the Agent handles routine decisions autonomously, but surfaces high-stakes semantic forks to the user via `RequestSelection`.

---

### RequestConfirmation

Covered by the shared Pre-Mutation Gate. Apply it exactly as specified there — no additional triggers at this level.

---

### RequestSelection

Call `RequestSelection` in exactly two situations:

**Situation 1 — System-type choice (same as Level 1):**

Picking a SlotEnumerator routing credential (enum type) when `allowedSelectorTypes` lists more than one candidate.

**Situation 2 — High-stakes semantic fork:**

All three of the following must be true:
- There are ≥ 2 meaningfully different outcomes.
- You genuinely cannot determine the correct one from context after exhausting your own reasoning.
- Choosing wrong would require significant rework to correct.

If any condition is not met, decide autonomously.

**When NOT to call:**

- Routine or low-stakes decisions (layout gap size, minor property values, node naming style).
- Any decision you can resolve with reasonable confidence. Exhaust your own reasoning first — this situation must remain rare.

> Default posture: decide autonomously. Surface only genuine, costly semantic forks.
