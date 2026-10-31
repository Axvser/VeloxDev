# Pitfalls — permissions and budget

Failures the repository has actually hit. Each entry names the symptom, the rule it violates, and the fix. The correct rules are in [wiring-and-policy.md](../practices/wiring-and-policy.md) and [tools.md](../practices/tools.md).

## A tool is refused and the reason is unclear

**Symptom:** a call comes back with a refusal string instead of a result.

**Cause:** the refusal can come from the mode matrix, a permission rule, a judge, or a budget cap. The order is: mode/rule/judge first, then the shared allowance, then this scope's own caps.

**Fix:** read the refusal text — it names the mode or rule. Check `WithPermissionMode`, `WithPermissionRule`, `WithPermissionJudge`, and the three call caps in that order.

## A `Deny` rule seems to be ignored under `Bypass`

**Symptom:** a tool runs in `Bypass` although a `Deny` rule exists.

**Cause:** the rule's pattern does not match the call. Patterns match a tool name (`DeleteNode`, `Get*`), a category (`category:Edit`), or a source (`source:mcp:*`); skill and sub-agent tools carry a null source, so a `source:` rule does not reach them.

**Fix:** match on the tool name or category, or wrap the call in a tool that carries the source you want to gate.

## An `Ask` becomes a silent denial

**Symptom:** a call is refused with no prompt.

**Cause:** the mode says `Ask` and no confirmation handler is registered. An unanswerable prompt denies.

**Fix:** register `WithConfirmationHandler`; or choose a mode that allows the category; or add an `Allow` rule for that call.

## The model believes execution is enabled but the tool refuses

**Symptom:** the model calls `ExecuteNode` and gets `disabled by host policy`.

**Cause:** `WithAllowNodeExecution` defaults to off. The tool is registered but the body gate refuses.

**Fix:** call `WithAllowNodeExecution(true)` when the host permits running node code. The refusal is the intended confirmation that the gate is wired.

## A read-only tool is charged to the write budget

**Symptom:** `MaxWriteToolCalls` is exhausted by queries.

**Cause:** the tool is not in the Query classification. `Analytics` is a separate flag; a query-only custom tool must be registered with `WithQueryTools`; `GetCompiledRunStatus` is classified `Execute`.

**Fix:** name `Analytics` alongside `Query` when filtering; register read-only customs with `WithQueryTools`; budget the handle-status calls as writes.

## The budget is spent and the model stops

**Symptom:** every call returns a limit refusal.

**Cause:** a cap was reached. The model may not realise `ResetToolCallLimit` exists.

**Fix:** the refusal text points at `ResetToolCallLimit`; the tool asks the user through the confirmation handler and reopens the budget on agreement. With no confirmation handler, the reset cannot be granted.

## A judge throws and every call is denied

**Symptom:** calls fail with a denial after a judge was registered.

**Cause:** an exception from the judge is treated as a denial.

**Fix:** make the judge total — return a decision for every invocation — and log instead of throwing.

## A tool is offered but cannot run in `Plan`

**Symptom:** the model sees a mutation tool in Plan mode but every call is refused.

**Cause:** the tools are registered regardless of mode; the mode matrix denies `Edit` / `Execute` / `Curate` in `Plan`.

**Fix:** that is the intended shape — the model can survey and propose. Use `ExitPlanMode` (with a confirmation handler) to move to a working mode.

## The session's shared allowance is exhausted before this scope's own

**Symptom:** a child scope refuses a call although its own cap has room.

**Cause:** the root ledger is shared across the tree and is asked before the per-scope cap.

**Fix:** reset the tree's budget through the user-facing `ResetToolCallLimit`, or raise the root cap. Per-scope caps cannot exceed the shared remainder.
