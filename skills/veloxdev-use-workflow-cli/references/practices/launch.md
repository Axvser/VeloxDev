# Launching the CLI

## Obtain and select a version

Do this on the command line; the agent runs the checks and the installs.

### Check .NET

```powershell
dotnet --info
dotnet --list-runtimes
```

The target is `net8.0` with `RollForward=LatestMajor`: .NET 8 or a newer major runtime runs it; `dotnet tool`
needs the SDK. If `dotnet` is missing or older than 8, install the .NET 8+ SDK/runtime and re-check.

### Choose the CLI version

The CLI compares the **assembly version** each component assembly references for `VeloxDev.Core` /
`VeloxDev.Core.Extension` against its own bundled copy. The component↔host coupling lives in the view-model
layer, and the generated, reflection-free serializer has kept that layer compatible across Core versions
since `10.0.1` — a matching major is not the test.

**1. Read what the components reference.**

```powershell
dotnet list MyNodes.csproj package --include-transitive
```

Once the CLI is running, `ArchiveInfo.compatibility` is the authoritative reading: it reports `wanted`
(component) and `loaded` (CLI) for every assembly it checked (step 3). The package list above is the
pre-install shortcut.

**2. Find and install the newest stable CLI.**

```powershell
dotnet tool search VeloxDev.Cli --detail          # published versions; add --prerelease for pre-releases
dotnet tool install --global VeloxDev.Cli --version <x.y.z>
dotnet tool update  --global VeloxDev.Cli --version <x.y.z>   # if already installed
veloxdev --help
```

Omit `--version` for the newest stable. `--tool-path ./.veloxdev` installs project-locally; on Unix,
`~/.dotnet/tools` may not be on `PATH`, so use the absolute shim or add it. Do not chase the components'
Core major; a component built against a newer Core than the CLI bundles is fine. Pin `--version <x.y.z>`
only when you need a reproducible build. The package is a **tool**, not a library: do not add it with
`PackageReference`.

**3. Judge each mismatch in `ArchiveInfo.compatibility.mismatches[]`.**

`wanted` is what the component references; `loaded` is what the CLI bundled.

| Reference | Condition | Action |
|---|---|---|
| `VeloxDev.Core` | `wanted >= loaded` | Fine. |
| `VeloxDev.Core` | `wanted < loaded` and `wanted >= 10.0.1` | Fine. |
| `VeloxDev.Core` | `wanted < loaded` and `wanted < 10.0.1` | Risk: the component predates the AOT-friendly serializer. Rebuild it against `10.0.1`+ and tell the user. |
| `VeloxDev.Core.Extension` | `wanted == loaded` | Fine. |
| `VeloxDev.Core.Extension` | any difference | Report the risk to the user before running. Extension is not a unified model layer and changes often. |

⚙ `exact` / `unified` / `incompatible` are reporting labels (no drift / minor-patch drift / major drift).
`incompatible` is **not** a hard stop and does not decide the table above.

**4. Verify by exercising the graph.**

Load the archive, call `ListCreatableTypes`, run `ValidateWorkflow`, then a small run. A pairing that passes
those is the real answer.

⚙ `--strict-plugin-versions` refuses a major mismatch at load. It is an opt-in policy, not the compatibility
rule: it ignores minor/patch drift, the `10.0.1` Core floor, and Extension risk.

⚙ **Coverage limit.** Only assemblies passed to `--assembly`, listed in the manifest, or loaded through
`LoadAssemblies` are checked; a transitive dependency the resolver finds on its own is not.

⚙ The package bundles `VeloxDev.Core`, `VeloxDev.Core.Extension` and the MCP SDK in
`tools/net8.0/any/`; a consumer does not need those references.

## Configure an MCP client

The server speaks stdio. Output is the JSON-RPC stream; all logging goes to stderr. The command is the
installed shim (or `dotnet` + the DLL for a checkout):

```powershell
veloxdev mcp --archive C:/work/graph.json --assembly C:/work/lib/MyNodes.dll --workspace C:/work --allow-execution
```

For a checkout or local build:

```powershell
dotnet <checkout>/Src/Cli/VeloxDev.Cli/bin/Debug/net8.0/veloxdev.dll mcp --archive C:/work/graph.json --allow-execution
```

Use the absolute shim path when `~/.dotnet/tools` is not on `PATH`, and translate the command and args into
the client's own MCP configuration syntax.

⚙ Use absolute paths in client configuration. The client's working directory is not the CLI's.

## The archive

The document is the library's serializer output. Dependencies and options live in a sidecar, `<archive>.manifest.json`:

```json
{
  "assemblies": [ "lib/MyNodes.dll" ],
  "rootType": "MyCompany.ViewModels.MyTreeViewModel",
  "allowExecution": false
}
```

- `assemblies` — resolved relative to the manifest and **may not escape the manifest's directory**. Loading runs module initializers; the listed assemblies register their component and serializer types.
- `rootType` — needed when the document was serialized through a concrete tree type (`treeViewModel.Serialize()` writes no root `$type`). Without it the CLI reads the graph into `TreeDefaultViewModel`.
- `allowExecution` — the manifest may opt into execution. `--allow-execution` does the same from the command line. Without either, execution tools are absent.

⚙ A missing dependency fails before deserialization with the type name. Add it to the manifest or `--assembly`; do not expect a directory scan.

## Launch flags

| Flag | Effect |
|---|---|
| `--archive <path>` | Required. The startup document. |
| `--manifest <path>` | Sidecar path. Defaults to `<archive>.manifest.json` if present. |
| `--assembly <path>` | Extra dependency; repeatable. `--assemblies a.dll;b.dll` is equivalent. |
| `--strict-plugin-versions` | Refuse a plugin whose VeloxDev.Core / Core.Extension reference has a different major version. Default: load it and warn. |
| `--single-archive` | Serve one document for the process lifetime; `LoadArchive` is refused, so runtime IDs stay valid. Start a new process per archive. |
| `--no-assembly-copy` | Load dependency DLLs directly from their source directory. The default copies them to `tasks/<taskId>_<timestamp>/` first so the project's build output stays unlocked. |
| `--no-run-csv` | Do not write execution results to `tasks/<taskId>_<timestamp>/runs/<timestamp>.csv`. The CSV export is on by default. |
| `--root-type <name>` | Concrete tree type; overrides the manifest. |
| `--allow-execution` | Adds blocking and background execution tools. |
| `--in-process-execution` | Runs node code in the server. Adds the toolkit's pause/resume/handle tools; refuses `LoadArchive`; a crash takes the server down. |
| `--redirect-on-error <n>` | Session-level fallback node for failures without `IRedirectable`. |
| `--checkpoint-file <path>` | `FileCheckpointStore`; survives a restart. |
| `--log-file <path>` | Full run log on disk; run results name it as `logFile`. |
| `--workspace <dir>` | Require every read/write path to be under this existing directory. |
| `--max-concurrent-runs <n>` | Worker limit (default 2; `0` = unlimited). Queued calls wait. |
| `--worker-kill-grace-ms <n>` | Grace after a run timeout before the worker is killed (default 5000). |

## One-shot execution

`run` is the same executable the MCP server uses as its worker. It always executes.

```powershell
veloxdev run --archive graph.json --start 0
veloxdev run --archive graph.json --start 0 --node-result
veloxdev run --archive graph.json --start 0 --seed '{"input":1}' --timeout-ms 30000
veloxdev run --archive graph.json --start 0 --log-file run.log --output-tree post-run.json
```

- `--start` is the 0-based node index from `ListNodes` (or a serialized archive's node order).
- `--node-result` uses the terminal role and computes that node from its ancestor cone.
- `--output-tree` writes the post-run tree and puts `stateFile` in the JSON result.
- Exit code: `0` when the result's `status` is `ok`, `1` when the run or worker failed, `2` for a usage error.

⚙ The worker never writes to the parent's stdout; a node that writes to stdout makes the result non-JSON and the call reports a worker failure. Use `IRuntimeContext.Log` or stderr.
