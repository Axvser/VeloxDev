# Pitfalls — declarations and paths

Failures the repository has actually hit. Each entry names the symptom, the rule it violates, and the fix. The correct rules are in [declarations.md](../practices/declarations.md).

## A cached declaration animates the first caller's value

**Symptom:** the second call to a reused declaration starts from or ends at the first call's endpoint.

**Cause:** the declaration captured a local endpoint. The value is passed by value and frozen when the declaration is built; a `static readonly` declaration shares it with every later execution.

**Fix:** build the declaration at the call site when its endpoint depends on the caller. Keep `static readonly` for declarations whose endpoints are constants.

## A cached declaration follows a later caller's index

**Symptom:** one execution's index change drives every execution of a shared declaration.

**Cause:** an index local such as `Items[i]` is captured through the closure instance. The closure is shared by the cached declaration, so a later change to `i` is read by every execution.

**Fix:** build the declaration per call when the index is per-call; use a path rooted at the target (`t.Selected`) when the index should follow the target.

## An index write does nothing

**Symptom:** the animation runs, but the indexed property never changes.

**Cause:** the index is out of range at frame time, or the leaf is declared on a value type, or the last segment is a get-only indexer. Out-of-range reads and writes map to a miss and are silent; only a miss seen by `Prepare` is reported once as `WarnStage.Unreadable`.

**Fix:** validate the index before starting; use a reference-type leaf; use a get-only indexer only as a step on the way to a writable member. Check `Warn` during development.

## An index follows a slot that changed mid-flight

**Symptom:** a property written for one item lands on another after `Selected` changes during the animation.

**Cause:** the path used the live index form; each frame re-resolves the index, but the end value was computed when the segment started.

**Fix:** wrap the index in `PathIndex.Frozen(t.Selected)` when the end value must land where it was read. Leave it live when following the target is the intent.

## The animation fires and returns before anything has moved

**Symptom:** code immediately after `Execute` reads the old value.

**Cause:** `Execute` returns as soon as the pipeline accepts the run; the pipeline is `async void`.

**Fix:** read the target after the run finishes, or sequence on the completion signal (`AwaitThen` in a chain, or the effect's `Completed` event). A blocking wait is not part of the contract.

## Starting a second animation makes the first look like a no-op

**Symptom:** two animations are started on the same object; only the later one is visible.

**Cause:** `Execute(target)` is mutual by default; a second mutual run supersedes the first.

**Fix:** give each animation its own target object, or pass `CanMutualTask: false` when they must run together. Concurrent runs are tracked separately and are not cancelled by a mutual `Execute`.

## The timeline overload is called with a wrong argument

**Symptom:** the animation runs on its own clock instead of the shared one, or does not compile.

**Cause:** the shared-timeline parameter is positional and named `timeline`; it is not a property on the effect.

**Fix:** pass the `ITimeSourceControl` as the second argument to `Execute(target, timeline)`.
