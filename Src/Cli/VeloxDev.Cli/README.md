# VeloxDev.Cli

A headless host for a VeloxDev workflow archive, and a stdio MCP server over the library's own tool surface.

Give it a workflow document and the assemblies its component types live in, and an MCP client can inspect the
graph, change it through the same component commands the editor uses, compile it, run it, and read the run's
result — without a GUI and without reading or rewriting JSON by hand.

```text
veloxdev mcp --archive graph.json --assembly lib/MyNodes.dll --allow-execution
```

## Install

The CLI is a framework-dependent .NET tool. Check the environment first:

```powershell
dotnet --info
dotnet --list-runtimes   # .NET 8 or newer; the tool rolls forward to the latest major
```

Install from NuGet:

```powershell
dotnet tool install --global VeloxDev.Cli --version 11.0.0
veloxdev --help
```

`dotnet tool install` creates the cross-platform `veloxdev` shim (Windows: `veloxdev.exe`) and places
the actual `veloxdev.dll` in the tool store. The shim is the supported entry; direct
`dotnet <store>/.../tools/net8.0/any/veloxdev.dll` is possible but depends on the store layout. On
Unix, add `~/.dotnet/tools` to `PATH` or use `--tool-path`.

Contributors can build the working tree instead:

```powershell
dotnet build Src/Cli/VeloxDev.Cli
dotnet build -c Release -p:VeloxDevLocalCore=true Src/Cli/VeloxDev.Cli
dotnet pack -c Release -p:VeloxDevLocalCore=true Src/Cli/VeloxDev.Cli
```

## Why a CLI and not another adapter

The graph model, the commands, the compiler and the runtime live in `VeloxDev.Core`. `VeloxDev.Core.Extension`
already exposes them as `AIFunction`s with descriptions, argument schemas and a permission gate. The CLI does not
reimplement any of that: it loads the archive, lets the extension build its toolkit, and publishes those same
tools over MCP. A fix to a tool is a fix in both the embedded Agent and this server.

## Commands

### `mcp`

```text
veloxdev mcp --archive <graph.json> [--manifest <path>] [--assembly <path>]...
             [--allow-execution] [--in-process-execution] [--strict-plugin-versions]
             [--single-archive] [--no-assembly-copy] [--no-run-csv]
             [--redirect-on-error <nodeIndex>] [--checkpoint-file <path>] [--log-file <path>]
             [--max-concurrent-runs <n>] [--worker-kill-grace-ms <n>] [--workspace <dir>]
```

Starts the stdio MCP server. Standard output is the JSON-RPC stream; all logging goes to standard error.
The startup archive can be replaced at runtime with `LoadArchive` (isolated execution only); the tool list stays
valid and every tool resolves the new session.

### `run`

```text
veloxdev run --archive <graph.json> --start <nodeIndex> [--node-result]
             [--seed <text>] [--timeout-ms <n>] [--redirect-on-error <nodeIndex>]
             [--checkpoint-file <path>] [--log-file <path>] [--output-tree <path>] [options]
```

One-shot execution for a human or CI. Prints the run result (outcome, data, failures and the log) as JSON and
exits 0 when `status` is `ok`. `--node-result` uses the terminal role and computes the node at `--start` from its
ancestor cone instead of running the chain from it. `--output-tree` writes the post-run graph and names it in the
result as `stateFile`; a client can adopt it with `LoadArchive`. This is also the process the MCP server uses as
its worker.

## The archive and its manifest

The archive is exactly what the library's serializer produced. Dependencies are declared in a sidecar,
`<archive>.manifest.json`:

```json
{
  "assemblies": [ "lib/MyNodes.dll", "lib/MyNodes.Helpers.dll" ],
  "rootType": "MyCompany.ViewModels.MyTreeViewModel",
  "allowExecution": false
}
```

- `rootType` is needed when the document was serialized through a derived tree's own type. The serializer
  writes `$type` only when the runtime type differs from the declared one, so `treeViewModel.Serialize()` leaves
  no root marker; without this field the CLI reads the graph into `TreeDefaultViewModel`. `--root-type` overrides
  the manifest. A derived tree's constructor still runs — a tree that expects a host service (an agent, a UI) is
  not automatically headless-ready.
- Assembly paths are resolved relative to the manifest and may not escape its directory.
- `--assembly` adds a dependency without editing the manifest; it resolves against the working directory.
- Dependency assemblies are copied to `tasks/<taskId>_<timestamp>/` beside the executable (falling back to
  LocalApplicationData when that directory is not writable) and loaded from the copy. A per-process
  `tasks/.lock` covers directory creation. The source build output therefore stays unlocked and can be
  rebuilt while the server runs. `ArchiveInfo.assemblyStaging` names the directory; `--no-assembly-copy`
  restores direct loading for troubleshooting.
- Every execution result is also exported to `tasks/<taskId>_<timestamp>/runs/<timestamp>.csv` (logs and
  failures, one row each; UTF-8 with BOM). The run result carries `csvFile`, and
  `ArchiveInfo.runLogDirectory` / `runCsv` describe the destination. `--no-run-csv` disables the export.
- Loading an assembly is **code execution**: its module initializers run and register the component types and
  serializer readers. Load only files you trust.
- A loaded plugin whose `VeloxDev.Core` / `VeloxDev.Core.Extension` reference differs is reported as
  `ArchiveInfo.compatibility` (`exact`, `unified`, or `incompatible`) and in `pluginWarnings`.
  `--strict-plugin-versions` refuses a major-version mismatch; the default is to load it and warn.
- `--single-archive` serves one document for the life of the process and refuses `LoadArchive`. Runtime IDs
  are only stable while the loaded graph stays alive; use this flag, or start a new process per archive, when
  a client caches IDs. `ArchiveInfo.runtimeIdScope` says which contract is in force.
- `allowExecution` is an opt-in request; `--allow-execution` does the same from the command line. Without either,
  the execution tools are not on the MCP surface at all.

If a document names a component type whose assembly was not loaded, the process fails before deserialization
with the type name and the remedy (`--assembly` or a manifest entry).

## Tools exposed

- **Inspection** — `GetWorkflowSummary`, `ListNodes`, `GetNodeDetail`, `GetFullTopology`, `GetTypeSchema`,
  `GetComponentContext`, `FindNodes`, `ListCreatableTypes`, `ValidateWorkflow`, `CompileWorkflow`,
  `CompileNodeResult`, `GetCompileStatus`, graph traversal and layout preview.
- **Editing** — `CreateNode`, `DeleteNode`, `CreateSlotOnNode`, `ConnectSlots` / `ConnectByProperty`,
  `DisconnectSlots`, `PatchNodeProperties`, `SetEnumSlotCollection`, slot collection edits, `Undo` / `Redo`.
- **Execution** (only with `--allow-execution` or the manifest flag) — by default the blocking
  `RunCompiledWorkflow` / `GetNodeResult` plus background `StartWorkflow`, `GetWorkflowJob`,
  `CancelWorkflowJob`, `ListWorkflowJobs`, `ClearFinishedJobs`, each in a child process. With
  `--in-process-execution`, the toolkit's full execution surface is exposed instead: `StartCompiledWorkflow`,
  `ContinueCompiledWorkflow`, `GetCompiledRunStatus`, `PauseCompiledRun`, `ResumeCompiledRun`,
  `StopCompiledRun`, `ListCompiledRuns`, and node-level `ExecuteNode` / `BroadcastNode`.
- **Session** — `ArchiveInfo`, `LoadArchive`, `SaveArchive`, `LoadAssemblies`, `GetCapabilities`,
  `GetAuditLog`, `ApplyBatch`, `SetRedirectOnError`, `ClearRedirectOnError`.
- **Context** — resources `veloxdev://archive/info` and `veloxdev://archive/manifest`, plus the
  `workflow-operator` prompt carrying the same operating rules as the server instructions.

`--workspace <dir>` is an optional path sandbox: every startup path, `LoadArchive`, `SaveArchive`,
`LoadAssemblies`, checkpoint, log and `--output-tree` must stay below it. Without it, paths are unrestricted.

Editing tools change the in-memory graph. Call `SaveArchive` when the change should outlive the process; it
writes compact JSON by default, copies the previous document to `<path>.bak` before replacing it, and accepts
`dryRun` to report the byte count, format and node/link counts without touching the disk.
`LoadArchive` switches documents while the server keeps running; unsaved edits to the old graph are discarded.
It is refused under `--in-process-execution`, where run handles belong to the session being replaced.

### Execution isolation

The default execution tools serialize the live graph to a temporary archive and run a `veloxdev run` child with
the current dependency assemblies, redirect setting, checkpoint file and log file. An edit made a moment ago is
what executes, without saving. A node that hangs, calls `Environment.Exit`, or crashes a native library kills
only the worker; the server returns the failure and keeps serving. Calls block until the worker ends; `timeoutMs`
is enforced by the child and, as a backstop, the server kills the tree after `--worker-kill-grace-ms` (default
5 s) and answers with `outcome: Cancelled`. `--max-concurrent-runs` (default 2, `0` = unlimited) bounds how many
workers run at once.

`StartWorkflow` returns a job id immediately; `GetWorkflowJob` reports `Queued`/`Running`/`Completed`/`Cancelled`/
`Failed` and the run result when done, `CancelWorkflowJob` kills the worker, and `ListWorkflowJobs` /
`ClearFinishedJobs` manage the registry. A job captures the session when it starts, so a later `LoadArchive` does
not change what a queued job will run.

After a successful run the worker writes its post-run tree and the result names it as `stateFile`. State is not
merged into the server's tree automatically: `LoadArchive(stateFile)` adopts it explicitly, so concurrent edits
are never overwritten behind the client's back.

`--in-process-execution` keeps the toolkit's handle-based tools (background runs, pause/resume, run status) at
the cost of that isolation: node code then runs in the server process.

Loaded plugins whose direct `VeloxDev.Core` / `VeloxDev.Core.Extension` reference has a different version are
reported in `ArchiveInfo.pluginWarnings` — the default load context unifies by simple name, so the plugin will run
against the host's version regardless.

## Client configuration

Claude Code (`.mcp.json`) or any client that uses the `mcpServers` shape:

```json
{
  "mcpServers": {
    "veloxdev": {
      "command": "dotnet",
      "args": [
        "C:/tools/VeloxDev.Cli/veloxdev.dll",
        "mcp",
        "--archive", "C:/work/graph.json",
        "--assembly", "C:/work/lib/MyNodes.dll",
        "--allow-execution"
      ]
    }
  }
}
```

For development, `dotnet run --project Src/Cli/VeloxDev.Cli -- mcp --archive ...` works the same way.

## Execution and redirects

A run is compile-then-drive: `CompilerViewModel.CompileAsync(entry, CompileRole.Root)` produces a `CompiledGraph`,
and `RuntimeEngine.RunAsync` drives it. `RunCompiledWorkflow` returns `runStatus`, `outcome`, `attempts`,
`failures`, the log and the final `data`; `GetNodeResult` computes a single node's result through the terminal
(ancestor-cone) role.

**Redirect is a node contract, not a tool argument.** A node that implements `IRedirectable` is asked for an
upstream target when its drive reports an error or throws; the engine maps that to the node's compile order,
ends the pass, and re-runs from there while keeping the outputs produced before it. A target that is not an
upstream compiled node is ignored with one log line, and redirects are capped at 50.

**The CLI also exposes the host-level fallback.** A node that reports an error without implementing
`IRedirectable` ends the run by default. `SetRedirectOnError(nodeIndex)` — or `--redirect-on-error` at launch —
installs `RuntimeContext.HostRedirect`, so subsequent runs fall back to that upstream node instead. The engine
validates the answer against every failure: it must be an upstream node of the failing one, and an invalid host
answer ends the run rather than being ignored (a node's invalid target is still ignored). A node that implements
`IRedirectable` is unaffected, including its right to decline. The setting is session-wide, not a parameter on a
run call, which is what lets it survive in a worker process and stay one decision rather than one per retry.

What an MCP client can do:

- see how a run ended, including `attempts` and the failure records;
- set or clear a host-level fallback with `SetRedirectOnError` / `ClearRedirectOnError`;
- re-run from an upstream node by calling `RunCompiledWorkflow` with that node's index (a fresh run, not a
  redirect — the preserved-prefix semantics do not apply);
- resume from a checkpoint with `ContinueCompiledWorkflow` in `--in-process-execution` mode after fixing the
  graph;
- edit the graph and run again.

It cannot override a node's own redirect contract, and it cannot pass a different target on each call. Both are
deliberate: the node that knows why it failed owns that decision, and one session setting is what the engine and
the worker can agree on.

## Known limits

- **Archive switching is isolated-only.** `LoadArchive` swaps the session when node code runs in a worker. Under
  `--in-process-execution` it is refused, because the toolkit's run handles belong to the old session; restart
  the server instead.
- **In-process execution is the unsafe mode.** The default runs node code in a child worker; with
  `--in-process-execution`, a node that hangs, calls `Environment.Exit`, or crashes a native library takes the
  server down with it.
- **No GUI semantics.** `LocateNode` / `LocateSlot` / `LocateLink`, `MarkDirty`, selection and confirmation
  tools are not exposed; they need a surface and a human.
- **Checkpoints are in memory unless told otherwise.** `--checkpoint-file` writes them through
  `FileCheckpointStore`, so `ContinueCompiledWorkflow` (in-process mode) can resume after a restart. The isolated
  surface does not expose a continuation handle; its workers write the file for a host or a later in-process run.
- **The isolated surface has no pause/resume.** `StartWorkflow` / `GetWorkflowJob` / `CancelWorkflowJob` give
  background start, status and stop, but pausing between nodes exists only in `--in-process-execution` mode.
- **Run state is not merged automatically.** The worker's post-run tree is written and returned as `stateFile`;
  adopting it is an explicit `LoadArchive`, so edits made while the run was in flight are not silently discarded.
- **Results are capped at 64 KiB.** A long `ListNodes`, `SerializeWorkflow` or run payload can be replaced by a
  truncation envelope. `--log-file` keeps the full run log on disk.
- **Node code must not write to stdout.** In a worker, stray output makes the result non-JSON and the call is
  reported as a worker failure; in-process, it corrupts the MCP protocol stream. Log through `IRuntimeContext.Log`
  (or stderr), never `Console.WriteLine`.
- **stdio only.** No HTTP/SSE transport, authentication, multi-tenant sessions or per-tool approval inside the
  server; the MCP client's own approval UI is the gate.
- **The workspace sandbox is opt-in.** Without `--workspace`, `SaveArchive`, `LoadAssemblies`, checkpoints and
  logs accept any path the process can reach. Set it when the server serves one project.
- **The tool package is local.** `dotnet pack` builds `VeloxDev.Cli`; it reaches NuGet only through the release
  step. Until then, install it from the package folder.
- **Plugins are never unloaded.** The default load context unifies by simple name; version mismatches are
  reported as warnings, not isolated. There is no per-plugin `AssemblyLoadContext` and no signature check.
- **A derived tree brings its constructor.** If the root type builds services in its constructor — the demo
  `TreeViewModel` installs an `AgentHelper` — those services and their assemblies must be in the archive's
  dependency closure, and they must be able to run headless. A plain `TreeDefaultViewModel` archive does not
  have this constraint.
