# The execution engine

`VeloxDev.Core`, namespace `VeloxDev.Core.WorkflowSystem.CompilerEx`. A graph is **compiled into a plan once**, then the plan is driven. Nodes do not broadcast to each other during a compiled run — the engine owns dispatch.

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
Task RunAsync(CompiledGraph graph, IRuntimeContext context, CancellationToken ct);
```

⚙ **`RunAsync`'s `CancellationToken` has no default.** It is required.

⚙ `CompileAsync` requires a **node** (anything else throws `ArgumentException`), clears and refills `Compiler.Graphs`, and currently returns exactly one graph.

⚙ `RuntimeContext.Target` is caller-set and `TargetReached` is engine-set. Set `Target` on **any** run — forward or reverse — to have the engine report whether that node was driven.

| `CompileRole` | Compiles | Walks |
|---|---|---|
| `Root` | the node's reachable sub-graph | downstream along `Targets` |
| `Terminal` | the node's ancestor cone | backward along `Sources`, deriving the entry frontier itself |

Compilation can throw `InvalidOperationException` when a `Terminal` cone cannot be expressed; see [Reverse compilation](#reverse-compilation).

## The plan

`CompiledGraph` is a nestable `ObservableCollection<CompileSegment>`. `CompileSegment` carries only structural state (`Id`, `Depth`).

| Segment | Drives |
|---|---|
| `ChainSegment` | `Nodes` in order |
| `BranchSegment` | `Router`, `Options`, `IsDynamic`, `CompileKey` — resolves a key and picks the matching option |
| `ParallelSegment` | `Branches` — a list of sub-graphs, executed **sequentially** |

The compiler emits a `ChainSegment` at every end of a linear run: a leaf, a router boundary, a fan-out boundary or a join boundary.

`ParallelSegment` appears when a route key has more than one target, when a **non-router** node has more than one valid target, or when a cone has several entry-frontier nodes.

⚙ **`ParallelSegment` is not parallel.** Its branches run one after another, and `context.Data` is rewound to the fan-out source's payload before each one — so a branch never sees a sibling's output. The shared runtime session is intentionally not thread-safe.

⚙ Live targets' siblings under a static router are marked stopped (`Order = -1`): they stay attached to the graph but are never placed in a segment and never driven.

## Compile identity

Every reached node is handed an `ICompileContext`:

| Field | Meaning |
|---|---|
| `Order` | a global monotonic counter across the whole graph, never reset |
| `ChainIndex` | index within its own chain |
| `Offset` | the counter's value at the graph's entry — sub-graphs carry an offset instead of resetting |
| `InputNodes` | the distinct upstream nodes, when the node is a join |

⚙ **`Order == -1` means absolute stop.** Such a node still gets a compile context, so read it through `IsCompileStopped` rather than assuming a missing value. It is never driven.

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

⚙ **Consumption and production are declared by topology, not by code.** `slot.Sources[].Parent` is an upstream node, `slot.Targets[].Parent` a downstream one. The compiler walks slots to `Targets`/`Sources` to `Parent` — never links, never channels.

⚙ **`AccessAsync` is the edge gate and runs in both phases.** Returning `false` silently deletes the edge: the downstream node is never visited and never gets a compile context.

⚙ **`ReceiveAsync` returning `null` is legal and wipes `Data`** for the next node.

⚙ **A node must not broadcast downward during a compiled run.** The engine drives every node itself; a node that relies on its own broadcast does nothing.

⚙ `context.Error(...)` / `context.Warn(...)` are not logging — see [Redirects](#redirects).

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

⚙ **One `RuntimeContext` instance is injected into every node of a run.** It is the shared blackboard — `Set`/`TryGet` for variables, `Log` for output — and it is not thread-safe.

⚙ `IsCompilePhase` is `true` on an `ICompileContext` and the compile phase always sees `Data == null`. Branch on it rather than on the presence of a value.

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

⚙ **A router must return its payload unchanged** (`return ctx.Data;`). Returning something else makes the selected branch see `null`.

⚙ **A dynamic router that happens to answer at compile time becomes static.** That is a one-line difference in behaviour that is very hard to spot from the node's own code.

### Reverse compilation

Terminal compilation walks backward over valid edges — gated by the same `AccessAsync` — building the ancestor cone, then derives the cone's entry frontier and compiles only what is needed.

⚙ **On a cone, routers keep their real `BranchSegment` semantics.** Only the branches whose targets lie inside the cone are compiled; sibling branches are **absent from `Options`** entirely, and their nodes are never driven.

⚙ **The target is never fabricated.** If the router resolves a key with no matching option at runtime, the run logs one line and ends — `Status` is still `"Completed"` — with `TargetReached == false`. Exactly what forward execution would have done.

⚠ **`TargetReached` is the only reliable reverse-probe signal.** `session.Data` can be non-null when the target was never driven, because the last node that *did* run left its value there.

⚙ **More than one route key reaching the cone** → `InvalidOperationException` at compile time.

⚙ **Independent cone producers that do not funnel into one common join** → `InvalidOperationException`. The compiler refuses to guess rather than inventing a merge order.

The test suite is the specification here: `Src/Core/VeloxDev.Core.Test/WorkflowSystem/CompilerEx/CompileToReverseTests.cs` and `RuntimeEngineRunTests.cs`.

## Joins

When a node has more than one distinct upstream node, the engine hands it an `IGroupData`:

```csharp
if (context.Data is IGroupData group && group.TryGetValue(sourceNode, out var value)) { … }
```

`IGroupData` is an `IReadOnlyDictionary<IWorkflowNodeViewModel, object?>`, keyed by **source node reference identity**.

⚙ **The join node must implement `ICompileTimeAware`.** The injection is guarded on a non-null compile context; without it the node silently receives whichever branch ran last instead of a group.

⚙ **An upstream that never registered an output is simply absent** — `TryGetValue` returns `false` and the indexer throws.

⚙ **"Wait for all inputs" is realized by running the branches sequentially**, not by synchronising threads. A join downstream of a fan-out therefore sees every input, in branch order.

⚙ **A join boundary is detected from distinct source *nodes*** across all input slots. Two input slots fed by the same upstream node are not a join.

## Redirects — loops without cycles

```csharp
public interface IRedirectable
{
    Task<int?> ResolveRedirectAsync(IRuntimeContext context, CancellationToken ct);
}
```

⚙ **`context.Warn(...)` and `context.Error(...)` request a redirect — they are not logging.** On a node that does not implement `IRedirectable`, either one ends the run: `Status = "Stopped"`, `CurrentOrder = -1`. A node whose helper calls `Warn` for an empty input quietly kills the chain.

⚙ **A redirect target must be strictly earlier than the requesting node's `Order`.** Anything else is ignored with a single log line and the flow carries on as if nothing had happened.

⚙ **This is a contract, not a shipped feature.** The engine drives `IRedirectable` end to end and the behaviour is covered by contract tests, but **nothing outside the test suite implements it** — no node in `Examples/Workflow/Common/Lib` or in any demo does. Treat it as an engine capability you would be the first to exercise, not as a pattern to copy from somewhere.

⚙ The whole graph is re-run from the accepted target, with `Order < target` skipped; `Attempt` increments per pass. **50 redirects** is the cap, after which it throws. If the target is a router's own order, only the branch is re-routed and the router is not driven again.

⚙ **The compiled graph is always acyclic** — a redirect is purely a runtime contract.

## Hooks

| Interface | Fires | What you do with it |
|---|---|---|
| `ICompileTimeAware` | once per reached node, when compilation finishes | keep the `ICompileContext`; show `Order` in the UI; `context is { Order: -1 }` means stopped |
| `IRuntimeAware` | before **every** drive of that node, once per pass | keep the session for logging and shared variables |
| `IRedirectable` | when a redirect is requested | return an earlier `Order`, or `null` to decline |

⚙ `AttachCompileTimeContext` is **not** called for pruned or unreached nodes — the absence of a compile context is how a node knows it is outside the plan.

## Pitfalls

⚙ **`GetHelper()` returning `null` silently prunes every outgoing edge.** The node compiles, runs, and produces nothing.

⚙ **Nothing in the engine fires `ReceiveCommand` or `BroadcastCommand`.** Return the value from `ReceiveAsync`; do not self-dispatch during a compiled run.

⚙ **Cancellation is the one exception that propagates unchanged** — a cancelled run surfaces as `Status = "Stopped"`. Any other exception thrown from `ReceiveAsync` is recorded on the context and rethrown to the chain driver.

⚙ **`RuntimeContext` is shared and not thread-safe** by design. Anything a node stores on the blackboard is visible to every other node in the same run.

⚙ **Compile-time and runtime reuse the same gate.** `AccessAsync` is called with an `ICompileContext` during compilation and with an `ITaskContext` at runtime — write it to answer for both, or read `IsCompilePhase`.

⚙ **The plan is a snapshot.** Editing the graph after `CompileAsync` does not change the compiled graph; recompile.
