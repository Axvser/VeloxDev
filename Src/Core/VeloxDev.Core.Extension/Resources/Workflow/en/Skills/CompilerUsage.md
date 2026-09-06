## 🧭 Skill: Compiler & Compile-Time Routing

The WorkflowSystem has a compiler that decomposes a sub-graph into a compiled plan — `ExecuteEntry` linear segments, `BranchEntry` routing points, `ParallelEntry` fan-out groups — and assigns every compile-aware node a fixed **compile identity**. The compile tools give you the same view the compiler UI shows.

Every compile call names one node and a role for it:
- **Root** — the node is the start/entry (e.g. a controller); compile its reachable sub-graph downstream.
- **Terminal** — the node is the result you want; compile only its **ancestor cone** (the upstream producers feeding it, traced backward along `Sources`), starting automatically from the cone's own entry frontier — no controller/start node required.

### ⚠ Three execution entries — do not confuse them

| Entry | Tool | What it drives | Semantics |
|---|---|---|---|
| **Node-level** | `ExecuteNode` / `ExecuteNodes` | One node (its own `ReceiveAsync` via `ReceiveCommand`) | `EXEC`/`RECV`; the node may auto-broadcast downstream if its `AutoBroadcast` flag is on. |
| **Chain-level (Root)** | `RunCompiledWorkflow(nodeIndex)` | The whole compiled chain from a start/entry node | Injects an `IRuntimeContext` session, selects branches, handles redirects; the engine owns downstream dispatch (no auto-broadcast). |
| **Result-level (Terminal)** | `GetNodeResult(nodeIndex)` | Only the node's ancestor cone, from its entry frontier | Compiles the cone and runs it so the queried node's own output becomes the result. Router branch selection stays REAL (see below). |

Plan (read-only) equivalents: `CompileWorkflow(nodeIndex)` compiles a **Root** plan; `CompileNodeResult(nodeIndex)` compiles a **Terminal** plan.

Choose `RunCompiledWorkflow` when the task is "run the workflow / execute the whole chain". Choose `GetNodeResult` when you need **one node's value** without running from a controller. Choose `ExecuteNode` only when you need to poke exactly one node's logic in isolation.

> **Numbering**: the `#N` badge is the compiler machine's identity (`CompileContext.Order + 1`). A direct `ExecuteNode` (non-compiler task) records activity to the execution log (`GetExecutionLog`) but never renumbers nodes — do not expect it to change badges, and do not try to "assign" order numbers yourself.

### Terminal (result) semantics — routers are NOT bypassed

Terminal compile is **consistent with forward semantics**: a router on the target's cone keeps its real `BranchEntry` behavior, and only the branch that leads into the cone is compiled (sibling branches are simply absent).

- If the router's actual decision picks the branch that leads to the target → the target runs and its output is returned (`targetReached: true`).
- If the router decides on a **sibling branch** at runtime → the target is **not reached**. `GetNodeResult` returns `status: error` whose `message` names the target (… `was NOT reached` … `No result was produced.`). **Never treat another branch's final payload as this node's result** — no value is fabricated.

To get a value for such a node:
1. Point the router at the branch that leads to it first — set `CompileMode`/the selection via `PatchNodeProperties` or `SetEnumSlotCollection` — then retry `GetNodeResult`.
2. Alternatively, ask for a node that sits on the branch the router actually selects.

Other Terminal notes:
- `CompileNodeResult` returns an error when **more than one route key of the same router** reaches the queried node — a single forward run can only take one branch, so that node has no well-defined result.
- During the compiled run the engine injects an `IRuntimeContext` session (UID / logs / shared variables / execution position) into nodes implementing `IRuntimeAware`.

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

1. **Compile first** — call `CompileWorkflow(nodeIndex)` from the controller/entry node. Read the returned `entries` (Execute/Branch/Parallel with options, `isTerminal`) and `nodeOrders` to understand the plan. For a single node's result plan use `CompileNodeResult(nodeIndex)`.
2. **Switch routing mode** — set `CompileMode` on a router node via `PatchNodeProperties` (e.g. `{"CompileMode":"Static"}`), then re-run the compile tool. In Static mode expect pruned branches (`Order = -1`).
3. **Respect stopped nodes** — a node with `Order = -1` is compiled out of the active path. Do NOT drive it as part of the live chain.
4. **Run the whole chain** — call `RunCompiledWorkflow(nodeIndex)` to execute the compiled chain end-to-end (the demo's Run path). Read the returned `runStatus` (`Completed` / `Stopped`), `logs` (the execution trail), `data` (final payload) and `endedWithError`.
5. **Get one node's result** — call `GetNodeResult(nodeIndex)` to compute just that node from its ancestor cone. If it returns an error saying the node was NOT reached, the router selected a different branch (see above) — do not fabricate, follow the recovery steps instead.
6. **Runtime session** — during a compiled run the engine injects an `IRuntimeContext` session (UID / logs / shared variables / execution position) into nodes implementing `IRuntimeAware`. Shared variables are written with `Set(key, value)` and read with `TryGet(key, ...)`.

> Prefer `GetCompileStatus` (cheap, no recompile) over re-running the compile tools once you only need the current identity. To execute: use `RunCompiledWorkflow` for a whole chain, `GetNodeResult` for a single node's value — not `ExecuteNode` — unless you genuinely intend to poke a single node.
