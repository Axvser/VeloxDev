# Pitfalls — wiring

Failures the repository has actually hit. Each entry names the symptom, the rule it violates, and the fix. The correct rules are in [wiring-and-policy.md](../practices/wiring-and-policy.md).

## Tool calls touch the graph from a background thread

**Symptom:** an `InvalidOperationException` about the wrong thread, a dispatcher exception, or an intermittent graph corruption.

**Cause:** `WithSynchronizationContext` was not called, or it was called off the UI thread.

**Fix:** call `WithSynchronizationContext(SynchronizationContext.Current)` on the UI thread before the agent runs. Keep `ConfigureAwait(false)` out of tool bodies; the wrapper resumes on the context after each await.

## A tool appears twice in the prompt

**Symptom:** the model calls one tool and the host sees two identical entries, or token usage is higher than expected.

**Cause:** the tool was offered through both `ChatOptions.Tools` and a context provider (the framework unions lists without deduplicating), or a subsystem's management tools were registered manually in addition to the provider.

**Fix:** let the provider be the single source. Remove the tool from `ChatOptions.Tools`; remove the manual registration of MCP / skill / sub-agent management tools.

## The agent cannot reach later registrations

**Symptom:** a tool added after the agent was built never appears, or a skill switched off still contributes.

**Cause:** the host used `ProvideTools()` (a one-shot snapshot) or built the agent with `ChatOptions.Tools` instead of `CreateContextProviders()`.

**Fix:** attach `scope.CreateContextProviders()` so the provider re-renders per turn. Keep `ProvideTools()` for a host that builds the agent by hand and accepts the snapshot.

## The prompt is stale after a configuration change

**Symptom:** the model still describes an old permission mode, an old tool list or an old skill set.

**Cause:** `scope.ProvideProgressiveContextPrompt()` was frozen into `ChatOptions.Instructions` before the change, and the host never attached the per-turn envelope provider.

**Fix:** build the agent with `CreateContextProviders()`; the provider's envelope repairs what drifted since the skeleton. If you build the agent by hand, rebuild the instructions after a configuration change.

## The prompt contains every skill twice

**Symptom:** the corpus is duplicated in the model's instructions.

**Cause:** skills were attached after the skeleton string was built, or the host also injected the corpus itself.

**Fix:** attach skills before `ProvideProgressiveContextPrompt()`, and let the provider own the corpus. Do not inject the same documents in both places.

## Two providers built from one scope are rejected

**Symptom:** agent construction throws because two providers share a session-state key.

**Cause:** `CreateContextProvider()` was called twice for one scope.

**Fix:** build one provider per scope and share the scope, not the provider.

## A structural tool call leaves no undo entry

**Symptom:** the Agent changed the graph, but `UndoCommand` skips it.

**Cause:** the tool reached a custom code path or a direct write instead of the library's command. Some tools are runtime state by design (move/resize, direct patches, slot-channel writes).

**Fix:** confirm the tool is the built-in one; for your own tools, call the generated commands or `Submit`. Check the classification in [tools.md](../practices/tools.md).

## The Agent uses a node index that now points at another node

**Symptom:** a tool acts on the wrong node after a create/delete or a selector change.

**Cause:** node indices shift; a selector change rebuilds a slot enumerator's slots.

**Fix:** prefer `GetFullTopology`, `ResolveSlotId`, `ConnectByProperty` and the `…ById` tools. Resolve an index immediately before use; do not store one across mutations.
