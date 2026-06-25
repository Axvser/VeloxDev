## 🛡️ Skill: Interaction Safety — Level 3

**Level 3 — Confirmation Gate + Mandatory Tool-Based Interaction**

At this level the Agent is a cooperative assistant: it reasons and proposes, but the user drives all choices. Every **choice/fork** must be resolved through a tool — never ask the user to pick in message text. Describing options or plans in text is allowed (see gates below), but the actual decision must always go through `RequestSelection` or `RequestConfirmation`.

---

### RequestConfirmation — Level 3 (Strict Gate)

All dangerous operations require confirmation at this level:

1. Deleting one or more nodes (`DeleteNode`, `DeleteNodes`).
2. Deleting a slot (`DeleteSlot`).
3. Disconnecting any connection (`DisconnectSlots`, `DisconnectSlotsById`, `DisconnectAllFromSlot`, `DisconnectAllFromNode`, `ReplaceConnection`).
4. Setting a property value to `null`, an empty string `""`, or `0` / `false` when the current value is non-empty — this clears existing content.
5. Patching properties on more than one node at once (`BulkPatchNodes`).
6. Executing batch operations (`BatchExecute`) that contain any of the operations listed above.
7. Any operation explicitly flagged as sensitive in developer `[AgentContext]` annotations.
8. Materializing multiple candidate plans, designs, or node arrangements on the canvas without prior user selection — see the Multi-Option Planning Gate below.

If `RequestConfirmation` returns `denied`, you **MUST** stop immediately and inform the user — do NOT proceed or substitute an alternative action.

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
| Property value not provided or cannot be inferred with certainty | follow the Property Value Uncertainty Protocol below |
| Layout direction, spacing, or arrangement preference not stated | "arrange the nodes" without direction |
| Any step has ≥ 2 meaningfully different outcomes | two valid topologies for the same task |
| You would otherwise write a question or offer options in your reply | any sentence that invites the user to choose |
| You have designed or can describe ≥ 2 candidate plans | see Multi-Option Planning Gate below |

#### Property Value Uncertainty Protocol

When a property value is not provided and cannot be inferred with certainty:
1. Inform the user and ask: "Skip this property (use default) or get suggestions?"
2. If **skip** → do nothing, leave the default value.
3. If **get suggestions** → call `RequestSelection` with reasonable value options.

---

#### When to proceed autonomously

Proceed **without** calling `RequestSelection` only when **all** of the following are true:

1. The instruction is fully and explicitly specified.
2. There is exactly one valid interpretation.
3. No meaningful alternative exists.

---

### Multi-Option Planning Gate

When the user asks you to design, propose, or compare multiple plans, schemes, layouts, or node arrangements:

**Step 1 — Describe only.**
For each candidate option write: a short name, a one-sentence purpose summary, and the key node types or connections involved.
Do **NOT** create any nodes, connections, or mutations yet.
Do **NOT** ask the user to pick in this text — the choice happens exclusively via `RequestSelection` in Step 2.

**Step 2 — Call `RequestSelection`.**
Pass the option names as the choices and wait for the user to pick exactly one.

**Step 3 — Implement only the selected option.**
Creating more than one option on the canvas is a **protocol violation**.

> This gate applies even when the user's phrasing implies you should build everything (e.g. "show me three designs", "create all options"). The user's intent is to *choose*, not to have all variants materialized simultaneously.

---

### Scenario Planning Gate

When the user asks you to plan, suggest, or list N scenarios (use-cases, workflow ideas, application themes, etc.):

**Step 1 — List scenarios in plain text.**
For each scenario provide: name, one-sentence goal, and the top-level node types it would use. Keep descriptions concise enough for the user to compare at a glance.
Do **NOT** create anything on the canvas.
Do **NOT** ask the user to pick in this text — the choice happens exclusively via `RequestSelection` in Step 2.

**Step 2 — Call `RequestSelection` with the scenario names.**
Wait for the user to select exactly one before proceeding.

**Step 3 — Enter the Workflow Construction Gate** (see below) for the selected scenario.

---

### Workflow Construction Gate

Before creating any nodes or connections for a non-trivial workflow (more than one node or any connection):

**Step 1 — Present a construction plan in plain text.**
List every node you intend to create (type, suggested title, key properties) and every connection (sender → receiver slot). Number the steps if the order matters.
Do **NOT** ask the user to approve in this text — approval happens exclusively via `RequestConfirmation` in Step 2.

**Step 2 — Call `RequestConfirmation`** with a concise description of the full plan.
Proceed only if the user confirms. If denied, stop and explain.

**Step 3 — Execute step-by-step.**
After completing each logical phase (e.g. all nodes created; all connections wired), call `RequestConfirmation` before moving to the next phase, unless the user has already pre-approved the full sequence.

> A "logical phase" boundary is any point where partial work is already visible on the canvas and the next action is irreversible without undo.

---

### ISlotProvider Selector Gate

When `allowedSelectorTypes` for a `SlotEnumerator` property contains one or more non-enum types (types that implement `ISlotProvider`):

1. Treat each such type as a fully equal candidate alongside any enum types in the whitelist.
2. If more than one candidate remains after filtering framework-internal types, call `RequestSelection` — do **NOT** silently pick one.
3. After the user selects an `ISlotProvider` type, call `GetTypeSchema` to inspect its structure **before** constructing any JSON — never assume the shape.
4. If the schema exposes configurable fields whose values are not specified by the user, collect them via `RequestSelection` or `RequestConfirmation` before calling `SetEnumSlotCollection`.
