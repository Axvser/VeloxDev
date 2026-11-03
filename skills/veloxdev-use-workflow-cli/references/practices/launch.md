# Launching the CLI

## Obtain

`VeloxDev.Cli` is a .NET 8 tool published on NuGet. It is a framework-dependent .NET tool: the
package contains `tools/net8.0/any/veloxdev.dll` and the tool manifest; `dotnet tool install` creates
the cross-platform `veloxdev` command. NuGet is the only distribution this skill relies on.

### Check .NET

```powershell
dotnet --info
dotnet --list-runtimes
```

- The target is `net8.0` with `RollForward=LatestMajor`: .NET 8 or a newer major runtime runs it.
  If neither is present, install .NET 8+.
- `dotnet tool install` needs the `dotnet` CLI (the SDK is the usual way to have it); the tool cannot
  bootstrap its own runtime.
- On Unix, `~/.dotnet/tools` may not be on `PATH`; use `--tool-path` or add it to `PATH`.

### Install from NuGet

```powershell
dotnet tool install --global VeloxDev.Cli --version 11.0.0
veloxdev --help
```

The package bundles `VeloxDev.Core`, `VeloxDev.Core.Extension` and the MCP SDK in
`tools/net8.0/any/`; a consumer does not need those references.

For a project-local install:

```powershell
dotnet tool install --tool-path ./.veloxdev VeloxDev.Cli --version 11.0.0
./.veloxdev/veloxdev --help          # Windows: .\.veloxdev\veloxdev.exe --help
```

⚙ A global install creates the `veloxdev` shim (Windows: `veloxdev.exe`); the MCP client config uses
`"command": "veloxdev"`. A `--tool-path` install uses the absolute path to the shim.

⚙ The package is a tool, not a library: do not add it with `PackageReference`.

### Direct DLL launch

The installed tool store contains `tools/net8.0/any/veloxdev.dll`:

```powershell
# Windows: %USERPROFILE%\.dotnet\tools\.store\...
# Unix:    ~/.dotnet/tools/.store/...
dotnet <tools>/.store/veloxdev.cli/11.0.0/veloxdev.cli/11.0.0/tools/net8.0/any/veloxdev.dll --help
```

The CLI's own worker launches itself this way. For client configuration, prefer the `veloxdev` shim:
the `.store` path contains the version and is an implementation detail.

## Configure an MCP client

The server speaks stdio. Output is the JSON-RPC stream; all logging goes to stderr. Claude Code reads
`mcpServers` from `.mcp.json` at the project root (or through `claude mcp add`); Claude Desktop uses its own
configuration file with the same shape.

Installed tool:

```json
{
  "mcpServers": {
    "veloxdev": {
      "command": "veloxdev",
      "args": [
        "mcp",
        "--archive", "C:/work/graph.json",
        "--assembly", "C:/work/lib/MyNodes.dll",
        "--allow-execution",
        "--workspace", "C:/work"
      ]
    }
  }
}
```

Checkout or local build — run the DLL with `dotnet`:

```json
{
  "mcpServers": {
    "veloxdev": {
      "command": "dotnet",
      "args": [
        "<checkout>/Src/Cli/VeloxDev.Cli/bin/Debug/net8.0/veloxdev.dll",
        "mcp",
        "--archive", "C:/work/graph.json",
        "--allow-execution"
      ]
    }
  }
}
```

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
