---
name: veloxdev-use-workflow-cli
description: Obtain, launch and operate VeloxDev.Cli — the headless workflow host and stdio MCP server — load a workflow archive with its component assemblies, inspect and edit the graph through the native tools, compile and run it in an isolated worker, manage background jobs, redirects, checkpoints and logs, and save the result. Use when an MCP client should drive a VeloxDev workflow, when installing or configuring VeloxDev.Cli in a client, or when running a graph from a terminal or CI.
---

## Responsibility

Drive a **running workflow** through `VeloxDev.Cli`'s tools: read the graph, change it, compile it, run it, and save it — without a GUI, and without editing the JSON document by hand.

This is not the .NET API skill. Writing a host, a custom tool or a component library is covered by `veloxdev-drive-workflow-with-ai` and `veloxdev-create-workflow`; this skill starts after those pieces exist and only covers the shipped CLI and its MCP surface.

## What it is

⚙ **One process, one loaded archive, one session.** `veloxdev mcp` loads a workflow document at startup. In isolated execution mode `LoadArchive` can replace it without restarting; under `--in-process-execution` it refuses. Unsaved edits are discarded on a switch.

⚙ **The tools are the library's own commands.** `ListNodes`, `CreateNode`, `ConnectByProperty`, `CompileWorkflow`, `RunCompiledWorkflow` and the rest are the same `AIFunction`s an embedded Agent uses. Editing through them keeps the undo stack and the component-command path intact.

⚙ **Execution runs in a child process by default.** A node that hangs, calls `Environment.Exit`, or crashes a native library kills the worker, not the MCP server. `--in-process-execution` trades that isolation for the toolkit's pause/resume and handle-based runs. It is opt-in and unsafe for untrusted graphs.

⚙ **Execution is opt-in.** Without `--allow-execution` (or `"allowExecution": true` in the archive manifest), the execution tools are not on the surface at all. Editing and inspection still work.

## Obtain

Do this on the command line; the agent runs the checks and the installs.

1. **Check .NET.** Run `dotnet --info` and `dotnet --list-runtimes`. `VeloxDev.Cli` is a
   framework-dependent `net8.0` tool with `RollForward=LatestMajor`: a .NET 8 or newer runtime runs it, and
   `dotnet tool` needs the SDK. If `dotnet` is missing or older than 8, install the .NET 8+ SDK/runtime for
   the platform and re-check.
2. **Find the version.** `dotnet tool search VeloxDev.Cli --detail` lists the published versions (add
   `--prerelease` to include prereleases). Take the newest stable; do not chase the components' Core major.
3. **Install or update.** `dotnet tool install --global VeloxDev.Cli --version <x.y.z>` for a first
   install, `dotnet tool update --global VeloxDev.Cli --version <x.y.z>` when it is already present. Omit
   `--version` for the newest stable; `--tool-path ./.veloxdev` installs project-locally. Verify with
   `veloxdev --help` (Windows: `veloxdev.exe`); on Unix, `~/.dotnet/tools` may not be on `PATH`, so use the
   absolute path or add it.
4. **Locate the runtime.** The shim is `~/.dotnet/tools/veloxdev` (`veloxdev.exe` on Windows). The DLL it
   launches is `~/.dotnet/tools/.store/veloxdev.cli/<version>/veloxdev.cli/<version>/tools/net8.0/any/veloxdev.dll`.
   `dotnet tool list --global` reports the installed version. Use the shim unless a checkout or
   `dotnet <dll>` launch is needed.

The package is a **tool**, not a library: do not add it with `PackageReference`.

**Judge drift from the components' references.**

Compatibility is decided at the view-model layer, not by matching majors.

1. Read the components' references — `dotnet list <proj> package --include-transitive`. A component
   project's package list is the pre-install shortcut; `ArchiveInfo.compatibility` is the authoritative
   reading once the CLI is running (`wanted` = component, `loaded` = CLI).
2. Judge each entry in `ArchiveInfo.compatibility.mismatches[]` (`wanted` = component, `loaded` = CLI):
   - `VeloxDev.Core` — `wanted >= loaded` is fine. `wanted < loaded` is fine only down to `10.0.1`, the
     first release with the completed AOT-friendly serializer. Below that, stop and tell the user.
   - `VeloxDev.Core.Extension` — **any** difference is a risk to report to the user; Extension is not a
     unified model layer and changes often.
3. `incompatible` only labels a major difference. Confirm compatibility by loading and exercising the
   graph: `ListCreatableTypes`, `ValidateWorkflow`, then a run.

Commands and the compatibility table: [references/practices/launch.md](references/practices/launch.md#choose-the-cli-version).

## The archive and its manifest

The archive is the library's serialized workflow document. Dependencies are declared in a sidecar next to it, `<archive>.manifest.json`:

```json
{
  "assemblies": [ "lib/MyNodes.dll", "lib/MyNodes.Helpers.dll" ],
  "rootType": "MyCompany.ViewModels.MyTreeViewModel",
  "allowExecution": false
}
```

⚙ **Load every component assembly before the document is read.** Type resolution goes through the library's registry, not `Type.GetType`; an unloaded assembly means the document cannot be deserialized. List it in `assemblies`, pass `--assembly`, or call `LoadAssemblies` for a type discovered later. The loader does **not** scan a directory.

⚙ **Loading an assembly is code execution.** Its module initializers run and register types. Treat an archive and its manifest as executable input, and use `--workspace` when serving one project.

⚙ **Name the root type when the document cannot.** Serializing through a concrete tree type writes no root `$type`; set `rootType` in the manifest or `--root-type`, or the CLI falls back to `TreeDefaultViewModel` and the derived tree's behavior is lost.

## Launch

Point the MCP client at the stdio server. The installed shim is the command:

```powershell
veloxdev mcp --archive C:/work/graph.json --assembly C:/work/lib/MyNodes.dll --workspace C:/work --allow-execution
```

Use the absolute shim path when `~/.dotnet/tools` is not on `PATH`. A checkout or local build is
`dotnet <checkout>/Src/Cli/VeloxDev.Cli/bin/Debug/net8.0/veloxdev.dll` followed by the same `mcp` args.
Translate that command and args into the client's own MCP configuration syntax.

| Flag | Effect |
|---|---|
| `--archive <path>` | Required. The document loaded at startup. |
| `--manifest <path>` | Sidecar path; defaults to `<archive>.manifest.json` when it exists. |
| `--assembly <path>` | Extra dependency; repeatable. Also `--assemblies a.dll;b.dll`. |
| `--root-type <name>` | Concrete tree type when the document has no root `$type`. |
| `--allow-execution` | Puts `RunCompiledWorkflow` / `GetNodeResult` and the job tools on the surface. |
| `--in-process-execution` | Runs node code in the server; enables pause/resume and run handles; refuses `LoadArchive`; unsafe for untrusted graphs. |
| `--redirect-on-error <index>` | Session-level host fallback for a node without `IRedirectable`. |
| `--checkpoint-file <path>` | File-backed checkpoints instead of memory. |
| `--log-file <path>` | Full run log on disk; the result names it as `logFile`. |
| `--workspace <dir>` | Sandbox every read/write path to this existing directory. |
| `--max-concurrent-runs <n>` | Worker concurrency (default 2; `0` = unlimited). |
| `--worker-kill-grace-ms <n>` | Extra wait after a run timeout before killing the worker (default 5000). |

For a human or CI, use the one-shot command — the same executable the MCP server uses as its worker:

```powershell
veloxdev run --archive graph.json --start 0 --log-file run.log --output-tree post-run.json
veloxdev run --archive graph.json --start 0 --node-result        # terminal role: compute that node's result
```

⚙ `run` always executes; `--allow-execution` is an MCP-surface switch, not a `run` switch.

## The operating loop

1. **Orient.** `ArchiveInfo` (or the `veloxdev://archive/info` resource) says which archive, root type, counts, execution mode, compatibility and warnings are in force. `GetCapabilities` lists every exposed tool and the result cap in one call; `GetAuditLog` shows what recent calls did. `GetWorkflowSummary` and `ListNodes` describe the graph; `ListCreatableTypes` says what can be created.
2. **Understand before editing.** `GetNodeDetail` / `GetNodeDetailById` for one node, `GetComponentContext` / `GetTypeSchema` for a component's API, `ListComponentCommands` for its commands. Use indices from the latest `ListNodes`; structural edits shift them.
3. **Edit through component commands.** `CreateNode`, `CreateSlotOnNode`, `ConnectByProperty`, `DisconnectSlots`, `PatchNodeProperties`, `SetEnumSlotCollection`, `ArrangeLayout`. `Undo` / `Redo` / `ClearHistory` are real; `TakeSnapshot` / `GetChangesSinceSnapshot` show what changed. For a multi-step edit, `ApplyBatch` runs up to 100 Mutation/Layout calls with an optional snapshot rollback (`stopOnError`).
4. **Validate and compile.** `ValidateWorkflow` for structural lint; `CompileWorkflow` (root) or `CompileNodeResult` (terminal) to see the plan and `GetCompileStatus` to read compile identity without recompiling.
5. **Save.** Editing changes memory only. `SaveArchive` writes the document atomically; with no path it overwrites the loaded archive. It writes compact JSON by default, keeps the previous file as `<path>.bak`, and `dryRun: true` reports what would be written without touching the disk.
6. **Run.** `RunCompiledWorkflow(startNodeIndex)` blocks and returns outcome/data/failures/log; `GetNodeResult(nodeIndex)` computes one node's result. Every run also writes `tasks/<taskId>_<timestamp>/runs/<timestamp>.csv` and names it as `csvFile` (logs + failures, one row each). For long runs use `StartWorkflow` → `GetWorkflowJob` → `CancelWorkflowJob`; `ListWorkflowJobs` / `ClearFinishedJobs` manage the registry.
7. **Recover.** `SetRedirectOnError(nodeIndex)` installs a session-wide fallback for nodes that report an error without implementing `IRedirectable`; `ClearRedirectOnError` removes it. A node's own contract always wins. Then re-run, or fix the graph and run again.
8. **Adopt post-run state.** An isolated run is a snapshot; its node VM state is not merged back. The result names a `stateFile`; `LoadArchive(stateFile)` adopts it explicitly. This is deliberately not automatic, so edits made while the run was in flight are not overwritten.

The full tool catalogue and the exact execution/job semantics are in [references/practices/tools.md](references/practices/tools.md).

## Headless authoring loop

An archive must exist before the CLI starts; it loads one, it does not create one. When the starting point is
an empty directory, the path is in [references/practices/headless.md](references/practices/headless.md). The shape:

1. **Build a component library.** A plain `net8.0` class library referencing `VeloxDev.Core` (the NuGet
   package brings the generator); `partial` node VMs with `[WorkflowBuilder.Node<THelper>]` and slot
   properties, `InitializeWorkflow()` from the constructor, helpers overriding `ReceiveAsync`. No adapter and
   no views.
2. **Bootstrap the first archive.** A console app builds a `TreeDefaultViewModel`, adds one node with
   `tree.GetHelper().CreateNode(...)`, and writes `tree.Serialize()`. Put `<archive>.manifest.json` beside it
   naming the DLL under `assemblies`, and copy the DLL into a folder under the manifest.
3. **Edit through the tools.** `ListCreatableTypes` must list the DLL's types; then `CreateNode`,
   `ConnectByProperty`, `ValidateWorkflow`, `CompileWorkflow`, `RunCompiledWorkflow`, `SaveArchive`.
4. **Verify headlessly.** `veloxdev run --archive ... --start 0` must exit `0` with `outcome: Completed` and
   the expected `data`; that replaces the GUI's drag-and-drop check.

⚙ **`$type` carries the assembly name.** Keep the component assembly name stable once archives exist.

## Rules that decide success

⚙ **Do not edit the archive file while the server runs.** The server owns the live graph; write through the tools and call `SaveArchive`. A direct file edit is invisible to the session and will be overwritten by the next save.

⚙ **A tool that is absent cannot be called.** Execution tools exist only with `--allow-execution` or the manifest flag. `StartCompiledWorkflow` and pause/resume exist only under `--in-process-execution`; in isolated mode use `StartWorkflow` / `GetWorkflowJob` / `CancelWorkflowJob`.

⚙ **Isolated means snapshot.** The worker runs the graph as it was when the job or call started. It does not see later edits, and its in-memory node changes do not come back. Only external side effects (files, databases) persist.

⚙ **A redirect target is validated per failure.** It must be an upstream node of the failing one in the compiled graph. A node's invalid target is ignored; a host target that cannot be a fallback ends the run. A node implementing `IRedirectable` is never overridden, including when it declines. Redirects are capped at 50.

⚙ **`LoadArchive` is isolated-only.** Under `--in-process-execution` the run handles belong to the old session, so the call is refused; restart instead. In isolated mode the tool list stays valid and every tool resolves the new session.

⚙ **Node code must not write to stdout.** In a worker it corrupts the JSON result; in-process it corrupts the MCP protocol stream. Use `IRuntimeContext.Log` or stderr.

⚙ **Results are capped at 64 KiB.** A long `ListNodes`, `SerializeWorkflow` or run payload can become a truncation envelope. `--log-file` keeps full run logs on disk.

⚙ **Plugins are not unloaded.** The default load context unifies by simple name. `ArchiveInfo.pluginWarnings` names direct `VeloxDev.Core` / `VeloxDev.Core.Extension` version mismatches, and `compatibility.status` says whether they are `unified` (minor/patch) or `incompatible` (major); the plugin still runs against the host's version.

⚙ **The CLI is stdio-only.** One client, no HTTP/SSE, no authentication, no multi-tenant sessions. Approval is the MCP client's own tool-approval UI; the server has no permission mode of its own.

## Troubleshooting

Symptom-first entries, with the rule each one violates and the fix, are in [references/pitfalls/runtime.md](references/pitfalls/runtime.md). The common ones:

| Symptom | Fix |
|---|---|
| `The workflow archive was not found: ...` | The CLI loads an archive; it never creates one. Bootstrap it first — [headless.md](references/practices/headless.md). |
| `has no registered JSON reader for the type name ...` | Load the assembly that declares it (`--assembly` or the manifest `assemblies`) before the document. |
| `The worker exited with code N` | A node died or crashed. Read the worker's stderr in the result; fix the node, or use `run` to reproduce. |
| `outcome: Cancelled` | The timeout or a cancel killed the worker. Check `timeoutMs` and `--worker-kill-grace-ms`; inspect `cancelled` / `timedOut`. |
| `LoadArchive is unavailable with --in-process-execution` | Restart with isolated execution to switch archives. |
| `... is outside the workspace` | Move the file under `--workspace`, or launch without the sandbox. |
| `Tool 'X' is not available in the current session` | The tool was not registered for this launch (execution off, or in-process split). Relaunch with the right flag. |
| Graph looks unchanged after a run | Isolated runs do not merge state. Use the result's `stateFile` and `LoadArchive` if you want the post-run tree. |

## References

- [references/practices/launch.md](references/practices/launch.md) — runtime checks, version selection, archive manifest, and every launch flag.
- [references/practices/tools.md](references/practices/tools.md) — the complete tool catalogue, execution/job modes, redirects, checkpoints and logs.
- [references/practices/headless.md](references/practices/headless.md) — component DLL, first archive, stdio driver, headless verification.
- [references/pitfalls/runtime.md](references/pitfalls/runtime.md) — observed failures: unloaded types, worker crashes/hangs, state not merging, reload refusals, workspace escapes.
- The project README: `Src/Cli/VeloxDev.Cli/README.md`.
