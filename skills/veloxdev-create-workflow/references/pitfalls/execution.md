# Pitfalls — compiled execution

Failures the repository has actually hit. Each entry names the symptom, the rule it violates, and the fix. The correct rules are in [execution.md](../practices/execution.md).

## A node produces nothing during a compiled run

**Symptom:** the run completes, but a node's output is `null` or the downstream node never runs.

**Cause:** one of four things — `GetHelper()` returned `null`, `ReceiveAsync` was not overridden (the base returns `null`), `AccessAsync` returned `false` and pruned the edge, or the node was compiled out because its slot topology did not reach it. The engine drives nodes itself; a node's own `BroadcastAsync` does nothing during a compiled run.

**Fix:** check `node.GetHelper()`; return the value from `ReceiveAsync`; make `AccessAsync` answer `true` for a live edge; inspect the compiled plan (`CompileWorkflow`) for the node.

## A fan-out branch sees another branch's data, or a sibling overwrites it

**Symptom:** two branches write the same payload, or one branch reads a value it did not produce.

**Cause:** treating the run as one shared `Data`. A fan-out gives each branch its own `BranchRuntimeContext`: `Data`, redirect state and report level are branch-private. The shared parts are identity, logs, the blackboard and the output table.

**Fix:** write branch output to the branch's `Data` and read inputs from `IGroupData` at a join. Use the blackboard (`Set` / `TryGet`) only for genuinely shared values, and remember the engine interleaves branches at await points.

## A branch's redirect state leaks into a sibling

**Symptom:** one branch's `Error` or redirect request changes another branch's outcome.

**Cause:** the code reached the shared `RuntimeContext` instead of the branch facade, or it cached the context across an await in a way that crossed branches.

**Fix:** use the `IRuntimeContext` handed to `ReceiveAsync` / `AccessAsync` for that drive. The branch facade forwards identity/logs/blackboard and keeps redirect state private.

## Assuming fan-out branches run to completion in source order

**Symptom:** a branch reads state written by a later branch, or a test that passed sequentially now races.

**Cause:** fan-out is concurrent. `RuntimeEngine.RunParallelAsync` starts branches with `Task.WhenAll`; they interleave at await points. It is not thread-parallel (no `Task.Run`), but it is not sequential either.

**Fix:** do not depend on cross-branch ordering. For a join, wait for the group; for determinism, rely on the documented policies — logs in real time, redirect winner by branch order, final `Data` from the last branch. `RuntimeContext.MaxParallelBranches` caps a group.

## A fan-out saturates the UI thread

**Symptom:** many branches make the host unresponsive.

**Cause:** asynchronous concurrency does not create threads. CPU-bound branches time-share the caller's synchronization context; a large group can monopolize it.

**Fix:** keep branch bodies I/O-shaped and await; cap the group with `MaxParallelBranches`; move CPU work out of the branch body when the host cannot afford it.

## A join receives a single value instead of a group

**Symptom:** `context.Data` is one branch's output, and `IGroupData` is absent.

**Cause:** the join node does not implement `ICompileTimeAware`, so the engine has no compile context to guard the group injection; or the join was detected from slots fed by the same upstream node (not a join).

**Fix:** implement `ICompileTimeAware` on the join node. Confirm the inputs come from distinct source nodes.

## A reverse query reports a value but `TargetReached` is false

**Symptom:** `session.Data` is non-null, but the target was never driven.

**Cause:** the last node that did run left its value in `Data`; a router on the cone selected a sibling branch.

**Fix:** treat `TargetReached` as the only reliable reverse-probe signal. Point the router at the branch that leads to the target and recompile.

## A redirect never happens

**Symptom:** `ResolveRedirectAsync` returns a target, but the run carries on as if nothing happened.

**Cause:** the target is not strictly earlier than the requesting node's `Order`; or the node does not implement `IRedirectable`; or the 50-redirect cap was hit and the run threw.

**Fix:** return an earlier compile order; implement `IRedirectable`; inspect the log line for the ignored target.

## A warning stops the run, or an error does not

**Symptom:** the run ends on a `Warn`, or continues after an `Error`.

**Cause:** `Warn` is a note and continues; `Error` is a stop unless the node implements `IRedirectable`, which then places the run. An uncaught exception takes the `Error` path.

**Fix:** use `Warn` for "this branch has nothing to do"; use `Error` only for a failure no downstream can bypass; implement `IRedirectable` when the node can route around it.

## A run does not see a graph edit

**Symptom:** a node added after compilation never runs.

**Cause:** the plan is a snapshot. `CompileAsync` produced it once; the engine drives the plan.

**Fix:** recompile after structural edits. For a live editor, recompile at the same point the GUI would.

## A terminal compile throws `InvalidOperationException`

**Symptom:** `CompileAsync(node, CompileRole.Terminal)` throws before the run.

**Cause:** the ancestor cone cannot be expressed — more than one route key reaches it, or independent cone producers do not funnel into one common join.

**Fix:** simplify the cone or select a node with a single upstream path. The compiler refuses to guess rather than inventing a merge order.
