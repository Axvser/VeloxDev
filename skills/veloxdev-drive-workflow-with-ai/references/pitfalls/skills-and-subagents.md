# Pitfalls — skills and sub-agents

Failures the repository has actually hit. Each entry names the symptom, the rule it violates, and the fix. The correct rules are in [skills.md](../practices/skills.md) and [subagents.md](../practices/subagents.md).

## A disk skill is not discovered

**Symptom:** the skill is absent from `ListSkills`; no error in the host.

**Cause:** the folder layout is wrong, the frontmatter is missing, or the directory name does not equal the frontmatter `name`.

**Fix:** use `<root>/<name>/SKILL.md`, with `---` frontmatter carrying `name` and `description`. Keep the directory name lowercase kebab-case and identical to `name`; keep `name` ≤ 64 characters and `description` ≤ 1024.

## A malformed skill disappears from the list

**Symptom:** the folder exists but the skill is not listed.

**Cause:** a malformed skill is reported, not silently dropped — but only if the root was scanned.

**Fix:** call `Refresh()` after adding the root or changing its contents, then inspect `scope.Skills.Status.ErrorCount` and the row's `State == Error` / `Error` message.

## A skill stays active after the host turned it off

**Symptom:** the model still receives a skill's text.

**Cause:** the model's tools and the host's UI share the same rows, but the toggle is applied per turn; a cached prompt was built before the change.

**Fix:** read the change through the provider on the next turn. Do not rebuild the agent; the provider re-renders when the skill set changes.

## The corpus is injected twice

**Symptom:** the model sees every skill document twice.

**Cause:** skills were attached after `ProvideProgressiveContextPrompt()` was frozen, or the host also injected the corpus.

**Fix:** attach skills before building the skeleton; let the provider own the corpus. The static prompt omits it when skills are attached.

## `Refresh()` makes the prompt re-render every turn

**Symptom:** token usage or prompt build cost rises unexpectedly.

**Cause:** `Refresh()` always advances `Skills.Version`, invalidating the provider's render.

**Fix:** call `Refresh()` when a root's contents change, not per turn.

## A skill tool is refused in `Plan`

**Symptom:** `ListSkills` / `load_skill` are denied in Plan mode.

**Cause:** skill tools carry `AgentActionCategory.Execute` for permissions even though they are query tools for the budget.

**Fix:** use a mode that allows `Execute` (or an `Allow` rule) when the session needs to load skills. The budget classification does not imply a permission grant.

## A sub-agent cannot reach a skill or MCP server

**Symptom:** the child's `ListSkills` / `ListMcpServers` is empty, or a request comes back in `dropped`.

**Cause:** the grant omitted the capability. `allowedSkills` / `allowedMcpServers` default to the parent's currently-enabled set; an empty array grants none. Skills and servers are handed down as narrowed views.

**Fix:** pass the capability in the spawn request, or omit the grant to inherit the parent's set. Read `dropped` for the wall that refused it.

## A sub-agent tree never terminates

**Symptom:** children keep spawning.

**Cause:** the budget alone terminates the tree slowly, and an unlimited root is bounded only by the fallback `SpawnBudget`.

**Fix:** set `WithSubAgentDepth(n)` in addition to the budget. Every grant is strictly smaller than what remains.

## A stuck child cannot reopen its budget

**Symptom:** the child keeps refusing calls.

**Cause:** a parent's `ResetToolCallLimit` does not reach down into a child; only the child's own reset travels up.

**Fix:** let the child call `ResetToolCallLimit`, which reaches the user through the tree; or spawn it with a larger share.

## Two skill providers reject agent construction

**Symptom:** agent construction throws because two providers share a session-state key.

**Cause:** one scope produced two providers.

**Fix:** build one provider per scope and share the scope.
