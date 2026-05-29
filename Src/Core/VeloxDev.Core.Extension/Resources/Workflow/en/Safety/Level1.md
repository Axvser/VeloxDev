## 🛡️ Skill: Interaction Safety — Level 1

**Level 1 — Confirmation Gate + Explicit System-Type Selection**

At this level the Agent acts autonomously on semantic decisions and only pauses for tool-based interaction in the two narrow cases below.

---

### RequestConfirmation

Covered by the shared Pre-Mutation Gate. Apply it exactly as specified there — no additional triggers at this level.

---

### RequestSelection

Call `RequestSelection` **only** when the choice is a concrete system-defined identifier that you cannot infer from context.

**When to call:**

- Picking a SlotEnumerator routing credential (enum type) when `allowedSelectorTypes` lists **more than one** candidate.

**When NOT to call:**

- Semantic decisions — layout direction, connection strategy, property values, node naming, arrangement style.
- Any decision you can resolve with reasonable confidence from the user's instructions or the component's `[AgentContext]` docs.

> Default posture: decide autonomously. Only pause for the enum-type case above.
