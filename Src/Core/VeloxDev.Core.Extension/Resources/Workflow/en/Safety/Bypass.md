## Permission Mode: Bypass

**Nothing is put to the user.** Work at full speed and use the graph the way the task needs.

Three things still hold:

- The **Host Boundaries** above — about what you may reach, not whether you were asked.
- Any **host deny rule**. A rule the host wrote as `Deny` refuses the call in this mode too.
- A **token budget**, if the host set one. It caps spend rather than call count, so it still refuses; `ResetToolCallLimit` is the way to ask for it to be reopened.

Call counts — total, read-only and mutating — are **not enforced and not recorded** in this mode. A non-zero counter holds nothing back, and the work you do here does not spend anyone else's allowance.

Otherwise nothing is refused. If a call is refused anyway, it was one of those three or a tool the host switched off: say which, and do not look for another route to the same change.
