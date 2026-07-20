# Compilation Mechanism

## Overview

`WorkflowCompiler` is a graph topology-based compilation execution engine. It compiles workflow node graphs into ordered executable plans, drives node execution in BFS/DFS order chain, and includes built-in mechanisms such as routing branches, error redirection, and lifecycle hooks.

---

## Quick Start

```csharp
// 1. Create the compiler
var compiler = new WorkflowCompiler();

// 2. Compile (FromNode default: traverse forward from Controller along Slot connections)
var results = compiler.Compile(controllerNode, CompileMode.BFS);
var plan = results[0]; // FromNode always returns a single result

// 3. Execute
var context = NetworkFlowContext.Create("seed-payload");
await plan.ExecuteAsync(context);
```

---

## Four-Dimensional Compilation Configuration

The compiler provides four orthogonal configuration dimensions, combining to form **24 compilation strategies**:

```
CompileMode     Direction       Scope           CycleHandling
─────────────────────────────────────────────────────────────
BFS             Forward         FromNode        Throw
DFS             Reverse         Omni            Trim
                                                Allow
```

### CompileMode — Traversal Algorithm

| Value   | Behavior                                                                                              | Applicable Scenario                                                   |
| ------- | ----------------------------------------------------------------------------------------------------- | --------------------------------------------------------------------- |
| `BFS` | Breadth-first, level expansion. Siblings at the same level are sorted in ascending order by `ICompileTimePriority`. Child nodes of route nodes are inserted at the head of the queue. | Scenarios requiring execution by topological hierarchy (default)      |
| `DFS` | Depth-first pre-order, parent node → child node recursively. Siblings are sorted by priority.         | Scenarios requiring completion of a single chain before processing other branches |

### CompileDirection — Direction of edges

| 值          | 行为                           | 适用场景             |
| ----------- | ------------------------------ | -------------------- |
| `Forward` | Forward traversal along `Slots[].Targets` | From entry to exit (default) |
| `Reverse` | Reverse traversal along `Slots[].Sources` | Trace backward from exit to entry |

### CompileScope — Starting Point Selection

| Value        | Behavior                                                        | Applicable Scenario                                               |
| ------------ | --------------------------------------------------------------- | ------------------------------------------------------------------ |
| `FromNode` | Starting from the specified node, radiate outward                | Starting from Controller (default, intuitive)                      |
| `Omni`     | Automatically discover all boundary points in the graph         | Full graph scan, automatically discover all isolated subgraphs      |
|              | Forward+Omni: Find nodes with in-degree 0 as starting points     |                                                                    |
|              | Reverse+Omni: Find nodes with out-degree 0 as starting points    |                                                                    |

**Note**: `Omni` mode returns `IReadOnlyList<CompilationResult>` (a separate result for each entry), `FromNode` returns a single-element list.

### CycleHandling — Cycle Strategy

| Value     | Behavior                                                                                          | Scenario                                    |
| --------- | ------------------------------------------------------------------------------------------------- | ------------------------------------------- |
| `Throw` | Throws exception when cycle is detected                                                           | Strict acyclic graph (default)              |
| `Trim`  | BFS/DFS naturally skips visited nodes, does not retain cycle metadata                             | Tolerates cycles but no circuit breaker needed |
| `Allow` | Retains cycle metadata (`IsLoopEntry`/`LoopTailId`), each node executed only once during execution | External tools that require cycle information |

---

## Depth and Execution Order

### `CompiledItem.Depth` (Depth Exemption)

Routing nodes (`ICompileTimeRouter`, e.g., BoolSelector, EnumSelector) **do not increment depth**:

```
Controller    LoadSeed    BoolSelector(router)    joinHot
Depth: 0      1           1                       1 ← 未递增!
```

The Depth of a child node is equal to the Depth of the route node itself, and is not affected by routing "branches".

### `CompiledItem.Order` (Execution Order)

Order is the 0-based sequential position in the compilation result. **The child nodes of the routing node jump to the front of the BFS queue**:

```
BFS queue: [Aggregate, joinHot(enqueue), joinCold(enqueue)]
→ Dequeue order: joinHot → joinCold → Aggregate
```

---

## Calculation Result

### `CompilationResult`

```csharp
public sealed class CompilationResult
{
    public IReadOnlyList<CompiledItem> Items { get; }  // Ordered execution plan
    public CompileMode Mode { get; }
    public CompileDirection Direction { get; }
    public CompileScope Scope { get; }
    public bool HasCycle { get; }
    public CycleHandling CycleHandling { get; }

    // Execute sequentially, result chain automatically passed
    public Task<object?> ExecuteAsync(object? parameter = null, CancellationToken ct = default);
}
```

### `CompiledItem`

```csharp
public sealed class CompiledItem
{
    public int Id { get; }            // Unique identifier
    public int Order { get; }         // Execution order
    public int Depth { get; }         // BFS/DFS depth (route exemption)
    public IWorkflowNodeViewModel Node { get; }  // Actual node
  
    // Error redirection
    public int? ErrorRedirectId { get; set; }
    public int MaxRetries { get; set; }
  
    // Route metadata
    public bool IsLoopEntry { get; }          // Mark loop entry when CycleHandling.Allow
    public int? LoopTailId { get; }           // Loop tail node ID
    public IReadOnlyDictionary<object, IWorkflowNodeViewModel>? RouteTable { get; }
    public Dictionary<object, HashSet<int>>? BranchExclusiveItems { get; }
}
```

---

## Execution Mechanism

### Result Chain Passing

After the node executes, `item.Node.WorkResult` is automatically passed to the next node as `parameter`:

```csharp
Controller → LoadSeed → BoolSelector → joinHot → Aggregate
  WorkResult  WorkResult  WorkResult   WorkResult  WorkResult
  "seed"      "loaded"    "routed"     "hot#1"     "aggregated"
```

### Route Branch Skip

The `ICompileTimeRouter` node calls `GetCurrentRouteKey()` at runtime, and the compiler precomputes the "exclusive downstream node" for each branch. The exclusive node of the unselected branch is skipped in `ExecuteAsync` (the `WorkCommand` is not executed, but lifecycle notifications are still sent).

### Error Redirection

```csharp
plan.Items[0].ErrorRedirectId = 3; // Redirect to node 3 when node 0 fails

// Error handler receives ErrorContext
// Redirect target executes asynchronously, result is chained into currentParam
// Redirect target is added to skippedItems, will not be re-executed in the main loop
```

### Lifecycle Hooks

Node implementing `ICompileTimeSink` receives:

| Event              | Timing                              |
| ----------------- | ----------------------------------- |
| `BeforeExecute` | Before node execution               |
| `AfterExecute`  | After node execution (including skipped) |
| `OnError`       | When node fails                     |
| `OnCompleted`   | When the entire chain is completed  |

### Cancel

`ExecuteAsync` accepts `CancellationToken`. After cancellation:

- `OperationCanceledException` **will not** be caught, immediately aborting the entire chain
- All nodes after the cancelled node **will not be executed**
- The current node's `UnsubscribeError` is executed before rethrow

---

## Extension Interface

### `ICompileTimeRouter` — Compile-time Routing

```csharp
public interface ICompileTimeRouter
{
    // Compile-time: returns mapping table of enum values to downstream nodes.
    IReadOnlyDictionary<object, IWorkflowNodeViewModel> GetRouteTable();
  
    // Runtime: returns the currently selected route key.
    object? GetCurrentRouteKey();
}
```

### `ICompileTimePriority` — Compile-Time Priority

```csharp
public interface ICompileTimePriority
{
    int CompilePriority { get; set; }  // Smaller value has higher priority, default 0
}
```

### `ICompileTimeSink` — execution lifecycle

```csharp
public interface ICompileTimeSink
{
    void OnExecutionEvent(ExecutionContext context);
}
```