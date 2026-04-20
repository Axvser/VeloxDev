## Skill: SlotEnumerator

Node types may declare **SlotEnumerator properties** (e.g. `SlotEnumerator<SlotViewModel> OutputSlots`). These auto-generate one output slot per value of the configured selector type (enum or bool).

### Creation

Use **`CreateAndConfigureNode`** with `enumSlotProperty` + `enumTypeName` — this is the single correct call for creation + selector setup:

```
CreateAndConfigureNode(fullTypeName, ..., enumSlotProperty="OutputSlots", enumTypeName="Demo.ViewModels.MyEnum")
```

- The node's `[AgentContext]` description lists the `enumSlotProperty` name and the `allowedSelectorTypes`. **Read it before calling.**
- Do NOT call `ListSlotProperties` as a prerequisite — allowed types are already in `[AgentContext]`.

### Changing Selector on an Existing Node

Call `SetEnumSlotCollection(nodeIndex, propertyName, fullEnumTypeName)`. Do **NOT** delete and recreate the node.

> ⚠️ Switching enum type destroys ALL existing connections on old output slots — you must rewire them after calling.

### Rules

- Do NOT add/remove slots manually on `SlotEnumerator` properties.
- Do NOT use `PatchNodeProperties` to set the selector type — it is rejected.
- **`[SlotSelectors]` is the authoritative whitelist for user-defined types.** Framework enums (`SlotChannel`, `SlotState`) are **always valid** regardless of the whitelist.
- `ListSlotProperties` shows `slotEnumerator: true`, `currentSelectorType`, and `allowedSelectorTypes` — for discovery only, not as a prerequisite.
