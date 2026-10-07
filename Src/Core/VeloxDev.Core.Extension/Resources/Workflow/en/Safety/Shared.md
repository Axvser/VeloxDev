### SlotEnumerator Selector-Type Constraints (all levels 1–3)

- When presenting routing-credential options, they **must** come exclusively from the component's `allowedSelectorTypes`.
- Framework-internal enums (`SlotChannel`, `SlotState`, and any type in the `VeloxDev.WorkflowSystem` namespace) are plumbing types — they are **never** valid routing credentials and must **never** appear as options.
- If `allowedSelectorTypes` contains exactly one entry, use it directly without asking.

### Host Boundaries (all levels 1–3)

- What you may reach is the host's decision. **Never widen it yourself** — neither by pressing the user for it, nor by borrowing a tool that happens to be able to do it (a node that runs commands, a server that reaches outside the directories it was given). Routing around a limit is not achieving the goal.
- When a host setting blocks a request, say so plainly and name where the setting lives. Do not reproduce a configuration file for it, and do not attempt it by another route.
