# The execution engine

`VeloxDev.Core`, namespace `VeloxDev.Core.WorkflowSystem.CompilerEx`. A graph is **compiled into a plan once**, then the plan is driven. The engine owns dispatch during a compiled run; a node returns its output from `ReceiveAsync`.

## The two calls

```csharp
var compiler = new CompilerViewModel();

// Forward — run the whole chain from a controller
var graph = (await compiler.CompileAsync(controller, CompileRole.Root))[0];
var session = new RuntimeContext { Data = seed };
await new RuntimeEngine().RunAsync(graph, session, CancellationToken.None);
// session.Status is "Completed" or "Stopped"; session.Data is the chain's final payload

// Reverse — compute one node's value, no start node needed
var cone = (await compiler.CompileAsync(someNode, CompileRole.Terminal))[0];
var probe = new RuntimeContext { Data = seed, Target = someNode };
await new RuntimeEngine().RunAsync(cone, probe, CancellationToken.None);

if (probe.TargetReached) Console.WriteLine(probe.Data);
else Console.WriteLine("target NOT reached — no value fabricated");
```

```csharp
Task<IReadOnlyList<CompiledGraph>> CompileAsync<T>(T component, CompileRole role, CancellationToken ct = default)
    where T : IWorkflowViewModel;
Task RunAsync(CompiledGraph graph, IRuntimeContext context, CancellationToken ct, ExecutionCheckpoint? resumeFrom = null);
```

⚙ **`RunAsync`'s `CancellationToken` has no default.** It is required.

⚙ **`resumeFrom` resumes instead of restarting.** The checkpoint records the nodes already done, and those are not driven again; its `Shape` must match the graph or the call throws `InvalidOperationException` before the session is touched.

## Run controls

The run controls are capabilities configured on the `RuntimeContext`; every one is optional, and with none configured the run behaves exactly as it did before they existed.

| Capability | Member | Effect |
|---|---|---|
| Resume point | `CheckpointStore` (`IExecutionCheckpointStore`) | Saves the run's current place after each successful node; the host loads it and passes it as `resumeFrom`. |
| Retry | `RetryPolicy` (`INodeRetryPolicy`) | Retries thrown exceptions; an `Error`/`Warn` a node asked for is not an exception. |
| Pause | `ExecutionGate` (`IExecutionGate`) | Awaited before each node, so a host can pause between nodes and resume. |
| Observation | `Observer` (`IExecutionObserver`) | A structural observation stream at run / node / branch boundaries. |
| Structured errors | `ErrorSink` (`IExecutionErrorSink`) | Every failure as an `ExecutionError` record. |
| Compensation | `Compensator` (`IExecutionCompensation`) | Per-node callbacks in reverse order on failure or cancellation. |
| Fan-out cap | `MaxParallelBranches` (`int?`) | Caps a fan-out group; `null` means uncapped. |

⚙ **The engine resolves these through the concrete `RuntimeContext`.** They are deliberately not members of `IRuntimeContext`: adding them would break every external implementation. A host that supplies its own `IRuntimeContext` gets none of them; a branch gets them through its `BranchRuntimeContext.Session`.

## Compilation

`CompileAsync` requires a **node** (anything else throws `ArgumentException`), clears and refills `Compiler.Graphs`, and currently returns exactly one graph.

`RuntimeContext.Target` is caller-set and `TargetReached` is engine-set. Set `Target` on **any** run — forward or reverse — to have the engine report whether that node was driven.

| `CompileRole` | Compiles | Walks |
|---|---|---|
| `Root` | the node's reachable sub-graph | downstream along `Targets` |
| `Terminal` | the node's ancestor cone | backward along `Sources`, deriving the entry frontier itself |

Compilation can throw `InvalidOperationException` when a `Terminal` cone cannot be expressed; see [Reverse compilation](#reverse-compilation).

## The plan

`CompiledGraph` is a class holding a `[VeloxProperty] ObservableCollection<CompileSegment> _entries`, not an `ObservableCollection` itself. It is nestable: `BranchSegment` / `ParallelSegment` each hold a child `CompiledGraph`. `CompileSegment` carries only structural state (`Id`, `Depth`).

| Segment | Drives |
|---|---|
| `ChainSegment` | `Nodes` in order |
| `BranchSegment` | `Router`, `Options`, `IsDynamic`, `CompileKey` — resolves a key and picks the matching option |
| `ParallelSegment` | `Branches` — a list of sub-graphs, driven concurrently |

The compiler emits a `ChainSegment` at every end of a linear run: a leaf, a router boundary, a fan-out boundary or a join boundary.

`ParallelSegment` appears when a route key has more than one target, when a **non-router** node has more than one valid target, or when a cone has several entry-frontier nodes.

## Fan-out

⚙ **A fan-out is concurrent: branches interleave at their await points.** `RuntimeEngine.RunParallelAsync` starts one asynchronous drive per branch and awaits them together with `Task.WhenAll`. Branches interleave at their `await` points on the caller's `SynchronizationContext`; the engine never uses `Task.Run`, so a CPU-bound branch still shares the thread and a host's UI context is not left.

⚙ **Each branch receives its own `BranchRuntimeContext` facade.**

| Branch-private | Forwarded to the shared session |
|---|---|
| `Data` | `Uid`, `Sequence`, `Status`, `IsRunning`, `Attempt` |
| `RedirectRequested` | `CurrentEntry` / `NodeIndex` / `BranchKey` / `CurrentOrder` |
| `PendingRedirectTarget` | `Target` / `TargetReached` / `EndedWithError` / `ActiveRedirectTarget` |
| `ReportedLevel` | `Set` / `TryGet` (the blackboard), `Logs`, `RegisterOutput` / `ResetOutputs` / `CollectGroupedInputs` |

A branch therefore sees its own payload and its own redirect/report state, while the tree identity, the log stream, the blackboard and the output table stay single. A branch never sees a sibling's `Data`.

⚙ **`MaxParallelBranches` limits a group.** The cap is read from the concrete `RuntimeContext` (`(context as RuntimeContext)?.MaxParallelBranches`); `null` is uncapped. A semaphore enforces it per group.

⚙ **Determinism follows branch order.** Logs are written in real time. When several branches request a redirect in the same group, the earliest branch in compile order wins; the others log a line and are ignored. After the group, `context.Data` is the last branch's `Data`, which is the value a sequential loop would have left.

⚙ **A join still sees every input.** `Task.WhenAll` completes the group before the join runs, and each branch registered its output in the shared output table. The join receives an `IGroupData` keyed by source node.

## Compile identity

Every reached node is handed an `ICompileContext`:

| Field | Meaning |
|---|---|
| `Order` | a global monotonic counter across the whole graph, never reset |
| `ChainIndex` | index within its own chain |
| `Offset` | the counter's value at the graph's entry — sub-graphs carry an offset instead of resetting |
| `InputNodes` | the distinct upstream nodes, when the node is a join |

⚙ **`Order == -1` means absolute stop.** Such a node still gets a compile context, so read it through `IsCompileStopped`. It is never driven.

## Node author contract

```csharp
public interface IWorkflowNodeViewModelHelper : IWorkflowHelper
{
    Task<object?> ReceiveAsync(ITaskContext context, CancellationToken ct);    // the single execution entry
    Task<bool>    AccessAsync(IAccessContext context, CancellationToken ct);   // the edge gate
    Task BroadcastAsync(object? parameter, CancellationToken ct);
    Task ReverseBroadcastAsync(object? parameter, CancellationToken ct);
    void Install(IWorkflowNodeViewModel node);
    void Uninstall(IWorkflowNodeViewModel node);
    void CreateSlot(IWorkflowSlotViewModel slot);
    void Move(Offset offset);
    void SetAnchor(Anchor anchor);
    void SetSize(Size size);
    void Delete();
}
```

A node computes in `ReceiveAsync` and **returns its output**; the engine writes that value to `context.Data` and registers it against the node.

⚙ **Consumption and production are declared by topology.** `slot.Sources[].Parent` is an upstream node, `slot.Targets[].Parent` a downstream one. The compiler walks slots to `Targets`/`Sources` to `Parent` — never links, never channels.

⚙ **`AccessAsync` is the edge gate and runs in both phases.** Returning `false` silently deletes the edge: the downstream node is never visited and never gets a compile context.

⚙ **`ReceiveAsync` returning `null` is legal and wipes `Data`** for the next node.

⚙ **A node returns its value from `ReceiveAsync`.** The engine drives every node itself; a node that relies on its own broadcast does nothing during a compiled run.

⚙ `context.Error(...)` / `context.Warn(...)` are not logging — see [Redirects](#redirects--loops-without-cycles).

## The context hierarchy

```text
IContext                    Data (the payload; always null at compile time)
├── IAccessContext          + IsCompilePhase, Sender, Receiver
│   ├── ITaskContext        what ReceiveAsync receives
│   └── ICompileContext     + Order, ChainIndex, Offset, InputNodes
└── IRuntimeContext : ITaskContext
                            + Uid, Logs, Status, Attempt, blackboard, Target, TargetReached, writable Data
```

The single discriminator a node uses inside `ReceiveAsync` is `ctx is IRuntimeContext`.

⚙ **A run injects a runtime context into every node.** In a chain, nodes receive the host's `RuntimeContext`; inside a fan-out they receive a `BranchRuntimeContext` facade over it. The facade forwards the shared blackboard (`Set`/`TryGet`), logs and outputs, and keeps `Data`, redirect state and report level private to the branch.

⚙ `IsCompilePhase` is `true` on an `ICompileContext` and the compile phase always sees `Data == null`. Use `IsCompilePhase` as the discriminator.

## Routers

```csharp
public interface ICompileTimeRouter
{
    Task<IReadOnlyDictionary<object, IReadOnlyList<IWorkflowNodeViewModel>>> GetRouteTable();
    Task<object?> ResolveRouteKey(object? payload);   // the runtime context, or null at compile time
}
```

`RouterCompileMode` is `Static` or `Dynamic`, and the compiler decides between them **solely** from `await ResolveRouteKey(null) is null`:

- a non-null key → **Static**: `CompileKey` is locked, and at runtime the engine uses that key and never calls `ResolveRouteKey(context)` again
- `null` → **Dynamic**: every branch is kept and the key is resolved per run

⚙ **A router returns its payload unchanged** (`return ctx.Data;`). The selected branch receives that payload.

⚙ **A dynamic router that answers at compile time becomes static.** That is a one-line difference in behaviour that is hard to spot from the node's own code.

### Reverse compilation

Terminal compilation walks backward over valid edges — gated by the same `AccessAsync` — building the ancestor cone, then derives the cone's entry frontier and compiles only what is needed.

⚙ **On a cone, routers keep their real `BranchSegment` semantics.** Only the branches whose targets lie inside the cone are compiled; sibling branches are **absent from `Options`** entirely, and their nodes are never driven.

⚙ **The target is never fabricated.** If the router resolves a key with no matching option at runtime, the run logs one line and ends — `Status` is still `"Completed"` — with `TargetReached == false`. That is what forward execution would have done.

⚠ **`TargetReached` is the only reliable reverse-probe signal.** `session.Data` can be non-null when the target was never driven, because the last node that *did* run left its value there.

⚙ **More than one route key reaching the cone** → `InvalidOperationException` at compile time.

⚙ **Independent cone producers that do not funnel into one common join** → `InvalidOperationException`. The compiler throws instead of inventing a merge order.

The test suite is the specification here: `CompileToReverseTests.cs` and `RuntimeEngineRunTests.cs` in `Src/Core/VeloxDev.Core.Test/WorkflowSystem/CompilerEx/`.

## Joins

When a node has more than one distinct upstream node, the engine hands it an `IGroupData`:

```csharp
if (context.Data is IGroupData group && group.TryGetValue(sourceNode, out var value)) { … }
```

`IGroupData` is an `IReadOnlyDictionary<IWorkflowNodeViewModel, object?>`, keyed by **source node reference identity**.

⚙ **The join node implements `ICompileTimeAware`.** The injection is guarded on a non-null compile context; with the interface, the node receives the group. The join is how "wait for all inputs" is expressed: the fan-out group completes before the join runs.

⚙ **An upstream that never registered an output is simply absent** — `TryGetValue` returns `false` and the indexer throws.

⚙ **A join boundary is detected from distinct source *nodes*** across all input slots. Two input slots fed by the same upstream node are not a join.

## Redirects — loops without cycles

```csharp
public interface IRedirectable
{
    Task<int?> ResolveRedirectAsync(IRuntimeContext context, CancellationToken ct);
}
```

⚙ **`context.Warn(...)` is a note.** The line is logged with a `[Warning]` marker and the run carries on with whatever the node returned: the value is not discarded, and no redirect is asked for. The engine skips redirect handling for warnings, so a node whose helper calls `Warn` for an empty input keeps going.

⚙ **`context.Error(...)` is a stop, and also not logging.** On a node that does not implement `IRedirectable`, the run ends: `Status = "Stopped"`, `CurrentOrder = -1`. An uncaught exception takes the same path. With `IRedirectable`, its answer decides where the run goes instead.

⚙ **A redirect target is strictly earlier than the requesting node's `Order`.** Anything else is ignored with a single log line and the flow carries on.

⚙ **The redirect route is shipped and exercised.** `PythonScriptNodeViewModel` implements `IRedirectable` (`ResolveRedirectAsync` resolves a named node back to its compile order), and `PythonHelper` drives it: a script's `redirect` field becomes the node's `RedirectTo`, and an `error` in the same output is what makes the engine ask `ResolveRedirectAsync` for that target order.

⚙ The whole graph is re-run from the accepted target, with `Order < target` skipped; `Attempt` increments per pass. **50 redirects** is the cap, after which it throws. When the target is a router's own order, only the branch is re-routed and the router is not driven again.

⚙ **The compiled graph is always acyclic** — a redirect is purely a runtime contract.

## Hooks

| Interface | Fires | What you do with it |
|---|---|---|
| `ICompileTimeAware` | once per reached node, when compilation finishes | keep the `ICompileContext`; show `Order` in the UI; `context is { Order: -1 }` means stopped |
| `IRuntimeAware` | before **every** drive of that node, once per pass | keep the session for logging and shared variables |
| `IRedirectable` | when a redirect is requested | return an earlier `Order`, or `null` to decline |

⚙ `AttachCompileTimeContext` is called for reached nodes. A node without a compile context is outside the plan.

## Checking a run

⚙ **Recompile after editing the graph.** The plan is a snapshot; `CompileAsync` is what refreshes it.

⚙ **Return the value from `ReceiveAsync` and let the engine dispatch.** `ReceiveCommand` and `BroadcastCommand` belong to the GUI path.

⚙ **Cancellation propagates unchanged** — a cancelled run surfaces as `Status = "Stopped"`. Any other exception thrown from `ReceiveAsync` is recorded on the context and rethrown to the chain driver.

When a run ends early or a node seems to be skipped, read [references/pitfalls/execution.md](../pitfalls/execution.md).
