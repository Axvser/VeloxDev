## Skill: SlotEnumerator

Node types may declare **SlotEnumerator properties** — any property whose type implements `IConditionalSlotProvider<TSlot>` (e.g. `SlotEnumerator<SlotViewModel> OutputSlots`). When the property type is the raw `IConditionalSlotProvider<TSlot>` interface, the generator automatically uses `SlotEnumerator<TSlot>` as the concrete default. These properties auto-generate one output slot per value of the configured selector type (enum or bool).

### Creation

Use **`CreateAndConfigureNode`** with `enumSlotProperty` + `enumTypeName` — this is the single correct call for creation + selector setup:

```
CreateAndConfigureNode(fullTypeName, ..., enumSlotProperty="OutputSlots", enumTypeName="Demo.ViewModels.MyEnum")
```

- The node's `[AgentContext]` description lists the `enumSlotProperty` name and the `allowedSelectorTypes`. **Read it before calling.**
- Do NOT call `ListSlotProperties` as a prerequisite — allowed types are already in `[AgentContext]`.

### Changing Selector on an Existing Node

Call `SetEnumSlotCollection(nodeIndex, propertyName, fullEnumTypeName)`. Do **NOT** delete and recreate the node.

- `fullEnumTypeName` is a **fully-qualified type name string** (e.g. `"Demo.ViewModels.MyEnum"`). This is the same value used in `CreateAndConfigureNode`.

> ⚠️ Switching enum type destroys ALL existing connections on old output slots — you must rewire them after calling.

### Accessing Internal Slots by Condition Value

SlotEnumerator slots can now be accessed directly by their **condition value** (enum name or `True`/`False`):

- **`GetEnumSlotByValue(nodeIndex, propertyName, conditionValue)`** – Returns runtime ID and detail of a slot inside the enumerator.
- **`SetEnumSlotChannel(nodeIndex, propertyName, conditionValue, channel)`** – Changes the `SlotChannel` of a specific slot.
- **`ConnectEnumSlot(senderNodeIndex, senderProperty, senderCondition, receiverNodeIndex, receiverSlot)`** – Connects an enumerator slot (by value) to another slot. Auto-verifies success.

### Mandatory Connection Protocol (STRICT — no exceptions)

Before connecting **any** slot inside a `SlotEnumerator`, you **MUST** complete Phase 1 in full. Skipping Phase 1 causes one source slot to be wired to multiple unintended branches.

**Phase 1 — Enumerate all internal slots first**

Call `ListSlotProperties(nodeIndex)` on the node that owns the SlotEnumerator. Locate the entry whose `slotEnumerator` is `true` and read its `currentSelectorType` and every condition value listed there. Do **not** proceed to Phase 2 until you have a complete list of all condition values.

**Phase 2 — Connect each slot individually**

For each condition value in the list, call `ConnectEnumSlot` exactly once with the correct `senderCondition`. Never call `ConnectEnumSlot` with a condition value that was not returned in Phase 1.

**Example (correct)**
```
// Phase 1
ListSlotProperties(2)  →  OutputSlots: ["GET", "POST", "DELETE"]

// Phase 2 — one call per condition, no more
ConnectEnumSlot(2, "OutputSlots", "GET",    5, "InputSlot")
ConnectEnumSlot(2, "OutputSlots", "POST",   6, "InputSlot")
ConnectEnumSlot(2, "OutputSlots", "DELETE", 7, "InputSlot")
```

**Example (wrong — never do this)**
```
// ✗ Connecting without enumerating first — may wire the wrong condition
ConnectEnumSlot(2, "OutputSlots", "GET", 5, "InputSlot")
ConnectEnumSlot(2, "OutputSlots", "GET", 6, "InputSlot")  // duplicate → one slot, two targets
```

### Rules

- **`enumTypeName` / `fullEnumTypeName`** always accepts a **fully-qualified type name string**. Passing a resolved `Type` object directly is also supported at the C# API level (`SetSelector(object? selector)` accepts both forms).
- Do NOT add/remove slots manually on `SlotEnumerator` properties.
- Do NOT use `PatchNodeProperties` to set the selector type — it is rejected.
- **`[SlotSelectors]` is the authoritative whitelist for user-defined types.** Framework enums (`SlotChannel`, `SlotState`) are **always valid** regardless of the whitelist.
- `ListSlotProperties` shows `slotEnumerator: true`, `currentSelectorType`, and `allowedSelectorTypes` — for discovery only, not as a prerequisite.
