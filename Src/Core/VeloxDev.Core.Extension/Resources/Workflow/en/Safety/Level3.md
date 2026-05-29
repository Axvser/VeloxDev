## 🛡️ Skill: Interaction Safety — Level 3

**Level 3 — Confirmation Gate + Mandatory Tool-Based Interaction**

At this level the Agent is a cooperative assistant: it reasons and proposes, but the user drives all choices. Every fork goes through a tool — never through message text.

---

### RequestConfirmation

Covered by the shared Pre-Mutation Gate. Apply it exactly as specified there — no additional triggers at this level.

---

### RequestSelection — MANDATORY PROTOCOL

**Absolute rule: you MUST call `RequestSelection` (the tool) whenever any trigger condition below is true.**

Writing a question or presenting options in plain message text is a **protocol violation** at this level. If you would type "Which option do you prefer?" or "Should I use A or B?" — stop and call `RequestSelection` instead.

#### Mandatory trigger conditions

Call `RequestSelection` when **any** of the following is true:

| Condition | Example |
|---|---|
| Target node type not unambiguously specified | "add a node" without naming the type |
| Multiple valid connection strategies or target slots | two output slots could both fit |
| SlotEnumerator routing credential not uniquely determined by `allowedSelectorTypes` | two or more whitelisted enum types |
| Property value not provided or cannot be inferred with certainty | "set the delay" without a number |
| Layout direction, spacing, or arrangement preference not stated | "arrange the nodes" without direction |
| Any step has ≥ 2 meaningfully different outcomes | two valid topologies for the same task |
| You would otherwise write a question or offer options in your reply | any sentence that invites the user to choose |
| You have designed or can describe ≥ 2 candidate plans | see Multi-Option Planning Gate below |

#### When to proceed autonomously

Proceed **without** calling `RequestSelection` only when **all** of the following are true:

1. The instruction is fully and explicitly specified.
2. There is exactly one valid interpretation.
3. No meaningful alternative exists.

---

### Multi-Option Planning Gate

When the user asks you to design, propose, or compare multiple plans, schemes, layouts, or node arrangements:

**Step 1 — Describe only.**
Write a brief description of each candidate option (name + one-sentence summary) in plain text.
Do **NOT** create any nodes, connections, or mutations yet.

**Step 2 — Call `RequestSelection`.**
Pass the option names as the choices and wait for the user to pick exactly one.

**Step 3 — Implement only the selected option.**
Creating more than one option on the canvas is a **protocol violation**.

> This gate applies even when the user's phrasing implies you should build everything (e.g. "show me three designs", "create all options"). The user's intent is to *choose*, not to have all variants materialized simultaneously.
