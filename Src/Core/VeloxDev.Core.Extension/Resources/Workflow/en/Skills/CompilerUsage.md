## 🧭 Skill: Compiler & Compile-Time Routing

The WorkflowSystem has a compiler that decomposes the controller-reachable sub-graph into a compiled plan — `ExecuteEntry` linear segments, `BranchEntry` routing points, `ParallelEntry` fan-out groups — and assigns every compile-aware node a fixed **compile identity**. The `CompileWorkflow` and `GetCompileStatus` tools give you the same view the compiler UI shows.

### ⚠ Two execution entries — do not confuse them

| Entry | Tool | What it drives | Semantics |
|---|---|---|---|
| **Node-level** | `ExecuteNode` / `ExecuteNodes` | One node (its own `ReceiveAsync` via `ReceiveCommand`) | `EXEC`/`RECV`; the node may auto-broadcast downstream if its `AutoBroadcast` flag is on. |
| **Chain-level** | `RunCompiledWorkflow(startNodeIndex)` | The whole compiled chain via the execution engine | Injects an `IRuntimeContext` session, selects branches, handles redirects; the engine owns downstream dispatch (no auto-broadcast). |

Choose `RunCompiledWorkflow` when the task is "run the workflow / execute the chain". Choose `ExecuteNode` only when you need to poke exactly one node's logic in isolation.

> **Numbering**: the `#N` badge is the compiler machine's identity (`CompileContext.Order + 1`). A direct `ExecuteNode` (non-compiler task) records activity to the execution log (`GetExecutionLog`) but never renumbers nodes — do not expect it to change badges, and do not try to "assign" order numbers yourself.

### Compile identity (ICompileContext)

Every node implementing `ICompileTimeAware` receives an `ICompileContext` after compilation:

| Member | Meaning |
|---|---|
| `Order` | Fixed execution sequence number. **`-1` = absolute stop** — the node is on a pruned static branch and must not run. |
| `ChainIndex` | Index within a linear segment. |
| `Offset` | Sub-graph entry offset. |

Query it with `GetCompileStatus` (returns `{i, id, t, order, chainIndex, offset, isStopped}` per node).

### Routing modes (RouterCompileMode)

| Mode | Compile-time behavior | Runtime behavior |
|---|---|---|
| **Static** | The branch key is locked to the selector's current value. Unselected branches are pruned; their downstream nodes get `Order = -1` (stopped). | Executes exactly the locked branch. |
| **Dynamic** | Cannot decide — `ResolveRouteKey(null)` returns null; **all** branches stay alive (`isDynamic = true`). | Re-resolves the key from the payload each run. |

### Reading compile state on a node

- `ICompileContext` — read-only; the node's `Order` / `ChainIndex` / `Offset`.
- `IsCompileStopped` — `true` when `Order == -1` (pruned static branch).
- `CompileMode` — the router's compile mode (`Static` / `Dynamic`), **writable** via `PatchNodeProperties`.

### How to operate

1. **Compile first** — call `CompileWorkflow(startNodeIndex)` from the controller/entry node. Read the returned `entries` (Execute/Branch/Parallel with options, `isTerminal`) and `nodeOrders` to understand the plan.
2. **Switch routing mode** — set `CompileMode` on a router node via `PatchNodeProperties` (e.g. `{"CompileMode":"Static"}`), then re-run `CompileWorkflow`. In Static mode expect pruned branches (`Order = -1`).
3. **Respect stopped nodes** — a node with `Order = -1` is compiled out of the active path. Do NOT drive it as part of the live chain.
4. **Run the chain** — call `RunCompiledWorkflow(startNodeIndex)` to execute the compiled chain end-to-end (the demo's Run path). Read the returned `runStatus` (`Completed` / `Stopped`), `logs` (the execution trail), `data` (final payload) and `endedWithError`.
5. **Runtime session** — during a compiled run the engine injects an `IRuntimeContext` session (UID / logs / shared variables / execution position) into nodes implementing `IRuntimeAware`. Shared variables are written with `Set(key, value)` and read with `TryGet(key, ...)`.

> Prefer `GetCompileStatus` (cheap, no recompile) over re-running `CompileWorkflow` once you only need the current identity. To execute, ALWAYS use `RunCompiledWorkflow` — not `ExecuteNode` — unless you genuinely intend to poke a single node.
