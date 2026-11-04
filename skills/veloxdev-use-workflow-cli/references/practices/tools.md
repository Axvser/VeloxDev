# The tool surface

All names are the library's own. An MCP client sees them unchanged. Tools not listed as always-available depend on launch flags.

## Context and session

| Tool | Use |
|---|---|
| `ArchiveInfo` | Archive path, root type, node/link counts, execution mode, redirect target, checkpoint and log files, loaded assemblies, plugin warnings, structured compatibility status, assembly copy/staging directory, task and run-log directories, and the runtime-ID scope. Call it first. |
| `LoadArchive` | Switch documents (isolated mode only). Unsaved edits are discarded. Can load new assemblies first. |
| `SaveArchive` | Write the current graph atomically. No path = overwrite the loaded archive. Compact by default; `backup=false` skips the `.bak`, `compact=false` writes indented JSON, `dryRun=true` reports without writing. |
| `LoadAssemblies` | Load dependency assemblies at runtime; their registered types become creatable. Loading runs code. |
| `GetCapabilities` | One call: exposed tool names, execution mode, result cap, workspace, compatibility status. Prefer it over guessing limits from individual descriptions. |
| `GetAuditLog` | Recent tool calls (native and archive-level): tool, time, duration, success, result size, 400-char argument summary. `maxEntries` keeps the newest N. |
| `ApplyBatch` | Apply up to 100 Mutation/Layout calls in one request: `[{"tool":"CreateNode","arguments":{...}}]`. `stopOnError=true` snapshots first and, on failure, reloads the snapshot as the live session (`stateFile`); the archive file is untouched. Refused with stopOnError under `--in-process-execution`. |
| `SetRedirectOnError` / `ClearRedirectOnError` | Session-wide host fallback for nodes without `IRedirectable`. Registered only when execution is enabled. |

Resources: `veloxdev://archive/info`, `veloxdev://archive/manifest`.
Prompt: `workflow-operator` (the server's operating rules).

## Inspection

`GetWorkflowSummary`, `ListNodes`, `GetNodeDetail`, `GetNodeDetailById`, `ListConnections`, `GetLinkDetail`,
`GetFullTopology`, `GetTypeSchema`, `GetComponentContext`, `ListComponentCommands`, `FindNodes`,
`ResolveSlotId`, `ListSlotProperties`, `GetEnumSlotByValue`, `ListCreatableTypes`, `ValidateWorkflow`,
`CompileWorkflow`, `CompileNodeResult`, `GetCompileStatus`, `GetExecutionLog`, `SerializeWorkflow`,
graph traversal (`SearchForward`, `SearchReverse`, `SearchAllRelative`, `IsConnected`, `FindPath`),
layout (`GetLayoutInfo`, `PreviewLayout`), `GetNodeStatistics`.

⚙ Prefer the narrowest query. `GetFullTopology` and `SerializeWorkflow(mode: "full")` can be large; the result
cap replaces an oversized payload with a truncation envelope. For a big graph use `ListNodes(maxNodes)`,
`ListConnections(nodeIndex, direction, maxLinks, offset)`, and `SerializeWorkflow(mode: "summary" | "skeleton")`.
`PreviewLayout` / `ArrangeLayout` accept `summaryOnly`, `changedOnly` and `maxPlacements` so the reply stays small.
`ValidateWorkflow` also runs the domain validators registered by loaded assemblies; each record names its
`validator` and optional `code`.

## Editing

`CreateNode`, `CreateSlotOnNode`, `AddSlotToCollection`, `RemoveSlotFromCollection`, `SetEnumSlotCollection`,
`DeleteNode`, `DeleteSlot`, `ConnectSlots`, `ConnectSlotsById`, `ConnectByProperty`, `ConnectEnumSlot`,
`DisconnectSlots`, `DisconnectSlotsById`, `SetSlotChannel`, `SetEnumSlotChannel`, `PatchNodeProperties`,
`PatchComponentById`, `MoveNode`, `SetNodePosition`, `ResizeNode`, `ArrangeLayout`,
`TakeSnapshot`, `GetChangesSinceSnapshot`, `Undo`, `Redo`, `ClearHistory`.

⚙ **Prefer `ConnectByProperty`.** Slot indices shift when a `SlotEnumerator` reconfigures; property names do not.
Use `ListSlotProperties` first if you must route by index.

⚙ **`PatchNodeProperties` is not a universal setter.** It rejects command-backed properties (`Anchor`, `Size`),
framework-managed members (`Parent`, `Slots`, `RuntimeId`, `Helper`), and source-generated slot properties. Use the
dedicated tool for those.

⚙ **Indices are positional and move after structural edits.** Refresh `ListNodes` after `CreateNode`,
`DeleteNode`, `DeleteSlot`, or slot-collection changes. Runtime IDs are stable across redraws; use them for
multi-step work.

⚙ **Generic command execution is not exposed.** `ListComponentCommands` discovers commands, but
`ExecuteCommandOnNode` / `ExecuteCommandById` are not on the MCP surface. Use the dedicated tool for the operation;
node-level receive/broadcast tools exist only under `--in-process-execution`.

## Compile

| Tool | Role |
|---|---|
| `CompileWorkflow(startNodeIndex)` | Root role: compile the chain reachable from a controller/entry node. |
| `CompileNodeResult(nodeIndex)` | Terminal role: compile only the ancestor cone that computes that node's result. |
| `GetCompileStatus` | Read existing compile identity without recompiling. |

⚙ `Order = -1` means the node was pruned by a static branch. Do not drive it as part of the live chain.

## Execution — isolated default

Requires `--allow-execution` or the manifest flag.

| Tool | Behaviour |
|---|---|
| `RunCompiledWorkflow(startNodeIndex, seed?, timeoutMs?)` | Blocks until the worker finishes; returns `runStatus`, `outcome`, `attempts`, `data`, `failures`, logs, `logFile`, `stateFile`. |
| `GetNodeResult(nodeIndex, seed?, timeoutMs?)` | Blocking terminal-role run; returns `targetReached` too. |
| `StartWorkflow(startNodeIndex, seed?, timeoutMs?)` | Returns a job id immediately. |
| `GetWorkflowJob(id)` | `Queued` / `Running` / `Completed` / `Cancelled` / `Failed` + result. |
| `CancelWorkflowJob(id)` | Kills the worker. |
| `ListWorkflowJobs` / `ClearFinishedJobs` | Registry inspection and cleanup. |

⚙ Each isolated run gets a snapshot of the live graph. Edits made after the call/job starts are not in that run.

⚙ A worker crash is an error result (`The worker exited with code N`); the server survives. A timeout or cancel
kills the process tree after `timeoutMs + --worker-kill-grace-ms` and returns `outcome: Cancelled` with
`cancelled` / `timedOut`.

⚙ `stateFile` is the post-run tree. Nothing is merged back automatically; `LoadArchive(stateFile)` adopts it.
This is how you keep run-produced node state without clobbering concurrent edits.

⚙ `--max-concurrent-runs` applies to blocking and background calls. If every slot is busy, a `StartWorkflow`
job sits in `Queued` until one frees.

## Execution — `--in-process-execution`

The toolkit's full execution surface, running in the server:

`StartCompiledWorkflow`, `ContinueCompiledWorkflow`, `GetCompiledRunStatus`, `PauseCompiledRun`,
`ResumeCompiledRun`, `StopCompiledRun`, `ListCompiledRuns`, `ClearFinishedRuns`,
`ExecuteNode`, `ExecuteNodes`, `BroadcastNode`, `ReverseBroadcastNode`.

⚙ This mode is the only place with pause/resume and run handles. It is also the only mode where a node crash,
`Environment.Exit`, or a native fault takes the MCP server down. Do not use it for an archive you do not trust.

⚙ `LoadArchive` is refused in this mode: the handles belong to the session being replaced. Restart instead.

⚙ `--checkpoint-file` plus `ContinueCompiledWorkflow` is the restart-surviving continuation. The isolated
surface writes the same checkpoint file but has no continuation handle.

## Redirects

- A node implementing `IRedirectable` is asked for an upstream target when its drive reports an error or throws.
  It owns the answer, including declining by returning null (the flow carries on).
- `SetRedirectOnError(nodeIndex)` is the host fallback used only for nodes **without** that contract. The engine
  validates the target per failure: it must be an upstream node of the failing one. A host target that is not
  ends the run.
- Redirects are capped at 50. The target's compile order and the outputs preserved before it follow the engine's
  normal redirect path.
- The setting is session-wide. It is not a per-call parameter, and a node's own contract always wins.

## Save, logs and results

- Editing changes memory. `SaveArchive` is the only implicit-free way to persist; a direct JSON edit is invisible
  to the session and will be overwritten.
- `--log-file` writes every run's lines to disk and the result names it. Without it, the returned logs are the
  in-memory view and the whole result is subject to the 64 KiB cap.
- `ArchiveInfo.logFile` / `checkpointFile` / `workspace` tell you what the process is configured with.
