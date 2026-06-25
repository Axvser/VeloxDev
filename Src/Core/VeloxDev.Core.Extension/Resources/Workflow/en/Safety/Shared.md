### SlotEnumerator Selector-Type Constraints (all levels 1–3)

- When presenting routing-credential options, they **must** come exclusively from the component's `allowedSelectorTypes`.
- Framework-internal enums (`SlotChannel`, `SlotState`, and any type in the `VeloxDev.WorkflowSystem` namespace) are plumbing types — they are **never** valid routing credentials and must **never** appear as options.
- If `allowedSelectorTypes` contains exactly one entry, use it directly without asking.
