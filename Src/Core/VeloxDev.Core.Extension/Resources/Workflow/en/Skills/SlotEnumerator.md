---
name: slot-enumerator
description: Wire SlotEnumerator (IConditionalSlotProvider) — CreateNode then SetEnumSlotCollection for enum/bool selectors, GetTypeSchema plus JSON for ISlotProvider types, reach branches by value with GetEnumSlotByValue, SetEnumSlotChannel and ConnectEnumSlot; never set the selector with PatchNodeProperties
---

## Skill: SlotEnumerator

Node types may declare **SlotEnumerator properties** — any property whose type implements `IConditionalSlotProvider<TSlot>` (e.g. `SlotEnumerator<SlotViewModel> OutputSlots`). When the property type is the raw `IConditionalSlotProvider<TSlot>` interface, the generator uses `SlotEnumerator<TSlot>` as the concrete default. These properties auto-generate one output slot per value of the configured selector type (enum or bool).

### Creation

Create the node first, then configure its selector:

```
CreateNode(fullTypeName, left, top, width, height)
SetEnumSlotCollection(nodeIndex, "OutputSlots", "Demo.ViewModels.MyEnum")
```

A selector-driven node is created with **no ports**: its slots exist only once a selector is set. `ListSlotProperties` reporting `count: 0` on a SlotEnumerator property means exactly that, not that the property is missing.

- Read `allowedSelectorTypes` before choosing: `ListSlotProperties` reports it reliably, and `GetComponentContext` shows it when the property has no description of its own. The one-line type summary does not carry member detail.

### Changing Selector on an Existing Node

Call `SetEnumSlotCollection` with the property name and selector. Do **not** delete and recreate the node.

**Enum / bool selector** (most common): pass the fully-qualified type name, leave `nonEnumTypeName` empty.

**ISlotProvider (instance-driven) selector** — when `[SlotSelectors]` lists a non-enum type (one that implements `ISlotProvider`):

1. **Inspect first** — call `GetTypeSchema(nonEnumTypeName)` to read the type's property structure, nested types, and developer instructions. Never assume the JSON shape.
2. **Construct and apply** — build the JSON from the schema, then call `SetEnumSlotCollection` with that JSON and the fully-qualified type name in `nonEnumTypeName`.

```
// Step 1 — always inspect before constructing
GetTypeSchema("Demo.ViewModels.CustomRouteSelector")

// Step 2 — construct JSON based on schema, then apply
SetEnumSlotCollection(nodeIndex, "OutputSlots",
    "{\"Routes\":[{\"Key\":\"A\",\"Label\":\"Path A\"},{\"Key\":\"B\",\"Label\":\"Path B\"}]}",
    "Demo.ViewModels.CustomRouteSelector")
```

> ⚠️ Switching the selector type rebuilds the branches. The framework auto re-wires new branches onto the previous branches' downstream (by position), and a previously-used type's connections are restored — do **not** manually rewire. If the new type has fewer members, the downstream of dropped positions is lost.

### Accessing Internal Slots by Condition Value

A condition value is an enum member name, `True`/`False` for a bool selector, or the port name for an ISlotProvider-driven enumerator.

- **`GetEnumSlotByValue`** — returns the slot's runtime ID (`slotId`) and channel.
- **`SetEnumSlotChannel`** — changes the channel of one enumerator slot.
- **`ConnectEnumSlot`** — connects an enumerator slot (by condition value) to another slot and verifies the result. The receiver may be a plain slot, a numeric slot index, or another enumerator slot (which then needs its own condition value).

### Mandatory Connection Protocol (STRICT)

Before connecting **any** slot inside a `SlotEnumerator`, complete Phase 1 in full. Skipping it can wire one source slot to multiple unintended branches.

**Phase 1 — enumerate the condition values**

- The reply to `SetEnumSlotCollection` lists every slot with its `label` and `value`; those values are the condition values.
- If that reply is no longer in context, call `ListSlotProperties(nodeIndex)` to read `currentSelectorType`, then `GetTypeSchema(currentSelectorType)` and read its `values`.
- For an ISlotProvider-driven enumerator, the port names are the condition values.

Do not proceed to Phase 2 until you have the complete list.

**Phase 2 — connect each slot individually**

Call `ConnectEnumSlot` exactly once per condition value, with the matching `senderCondition`. Never use a condition value that Phase 1 did not return.

**Example (correct)**
```
// Phase 1
ListSlotProperties(2)  → currentSelectorType: "Demo.NetworkRequestMethod", count: 3
GetTypeSchema("Demo.NetworkRequestMethod") → values: GET, POST, DELETE

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

- Do not add or remove slots manually on `SlotEnumerator` properties.
- Do not use `PatchNodeProperties` to set the selector type — it is rejected.
- **`[SlotSelectors]` is the authoritative whitelist for user-defined types.** Framework enums (`SlotChannel`, `SlotState`) are accepted regardless of the whitelist.
- `ListSlotProperties` shows `slotEnumerator: true`, `currentSelectorType`, `allowedSelectorTypes`, and the current slot IDs — use it for discovery.
