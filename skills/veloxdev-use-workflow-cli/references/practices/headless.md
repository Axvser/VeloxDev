# Headless authoring — from no archive to a running workflow

The CLI operates an archive; it never creates one. This is the path that starts with an empty directory:
build a component DLL, write one small archive with that DLL, then do everything else through the tools.

## 1. The component library

A plain class library is enough. No adapter package, no XAML, no view files.

`MyNodes/MyNodes.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <LangVersion>latest</LangVersion>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="VeloxDev.Core" Version="*" />
  </ItemGroup>
</Project>
```

`MyNodes/Nodes.cs`

```csharp
using VeloxDev.MVVM;
using VeloxDev.WorkflowSystem;

namespace MyNodes;

[WorkflowBuilder.Node<SourceHelper>]
[DefaultSize(180, 120)]
public partial class SourceNode
{
    public SourceNode() => InitializeWorkflow();

    [VeloxProperty] public partial SlotDefaultViewModel OutputSlot { get; set; }
}

public sealed class SourceHelper : NodeHelper<SourceNode>
{
    public override Task<object?> ReceiveAsync(ITaskContext context, CancellationToken ct)
        => Task.FromResult<object?>("hello from source");
}

[WorkflowBuilder.Node<SinkHelper>]
[DefaultSize(180, 120)]
public partial class SinkNode
{
    public SinkNode() => InitializeWorkflow();

    [VeloxProperty] public partial SlotDefaultViewModel InputSlot { get; set; }
}

public sealed class SinkHelper : NodeHelper<SinkNode>
{
    public override Task<object?> ReceiveAsync(ITaskContext context, CancellationToken ct)
        => Task.FromResult<object?>(context.Data is null ? "sink: <no data>" : $"sink: {context.Data}");
}
```

⚙ **`VeloxDev.Core` from NuGet brings the generator.** A `ProjectReference` to Core does not: analyzers do
not flow across project references, so that project also needs `VeloxDev.Core.Generator` with
`OutputItemType="Analyzer"`.

⚙ **The generator emits four pieces, and all four matter.** The node's ViewModel half, its MVVM partial,
the VeloxJson reader/writer registration (without it the archive cannot be read), and the AIContextTree
entry (without it `ListCreatableTypes` and `CreateNode` cannot see the type). Component types enter the
context tree automatically; `[AgentContext]` only adds descriptions to the agent surface.

⚙ **Every node class is `partial`, calls `InitializeWorkflow()` from its own constructor, and has a public
parameterless constructor.** `CreateNode` requires the constructor; a missing `partial` produces no file and
no warning.

```powershell
dotnet build MyNodes -c Release
# MyNodes/bin/Release/net8.0/MyNodes.dll
```

## 2. Bootstrap the archive

The CLI requires an existing, non-empty document. Produce it with the library itself — hand-writing the
JSON is how `$id` / `$type` / member order get wrong.

`Bootstrap/Program.cs`

```csharp
using MyNodes;
using VeloxDev.Serialization;
using VeloxDev.WorkflowSystem;

var tree = new TreeDefaultViewModel();
tree.GetHelper().CreateNode(new SourceNode());

var path = args.Length > 0 ? args[0] : "graph.json";
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
File.WriteAllText(path, tree.Serialize());
```

`Bootstrap/Bootstrap.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <LangVersion>latest</LangVersion>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\MyNodes\MyNodes.csproj" />
    <PackageReference Include="VeloxDev.Core" Version="*" />
  </ItemGroup>
</Project>
```

```powershell
dotnet run --project Bootstrap -- run/graph.json
```

⚙ Serialize through the concrete tree type. A derived tree written this way carries no root `$type`; either
add `rootType` to the manifest or serialize through the base/interface so the marker is written.

## 3. The manifest

`run/graph.json.manifest.json`

```json
{
  "assemblies": [ "lib/MyNodes.dll" ],
  "allowExecution": true
}
```

Copy the built DLL to `run/lib/MyNodes.dll`. Assembly paths resolve relative to the manifest and **may not
escape its directory**. The CLI already carries `VeloxDev.Core` and `VeloxDev.Core.Extension`; put any
other dependency the component needs beside the DLL.

## 4. Drive the CLI

```powershell
veloxdev mcp --archive run/graph.json --allow-execution --workspace run
```

The server speaks stdio MCP with **newline-delimited JSON-RPC** (not `Content-Length` framing). The order
that works:

| Step | Call |
|---|---|
| handshake | `initialize` → wait for the response → `notifications/initialized` |
| discover | `ListCreatableTypes` — the DLL's types must appear here |
| edit | `CreateNode { fullTypeName: "MyNodes.SinkNode", left, top }` |
| connect | `ConnectByProperty { senderNodeIndex: 0, senderProperty: "OutputSlot", receiverNodeIndex: 1, receiverProperty: "InputSlot" }` |
| check | `ValidateWorkflow` |
| compile | `CompileWorkflow { startNodeIndex: 0 }` |
| run | `RunCompiledWorkflow { startNodeIndex: 0, timeoutMs: 60000 }` |
| persist | `SaveArchive` (no path = overwrite the loaded archive) |

A minimal stdio driver (run it from the directory that contains `run/`):

```powershell
$psi = [Diagnostics.ProcessStartInfo]::new()
$psi.FileName = "veloxdev"
foreach ($a in "mcp", "--archive", "run/graph.json", "--allow-execution", "--workspace", "run") {
    $psi.ArgumentList.Add($a)
}
$psi.RedirectStandardInput = $psi.RedirectStandardOutput = $true
$psi.StandardOutputEncoding = [Text.Encoding]::UTF8
$psi.UseShellExecute = $false
$p = [Diagnostics.Process]::Start($psi)

$id = 0
function Send($m) { $p.StandardInput.WriteLine(($m | ConvertTo-Json -Compress -Depth 20)); $p.StandardInput.Flush() }
function Receive-For([int]$wanted) {
    while (($line = $p.StandardOutput.ReadLine()) -ne $null) {
        if ([string]::IsNullOrWhiteSpace($line)) { continue }
        $m = $line | ConvertFrom-Json
        if ($m.id -eq $wanted) { return $m }
    }
}
function Call($name, $arguments) {   # not $args: that name is PowerShell's automatic array
    $script:id++
    Send @{ jsonrpc = "2.0"; id = $script:id; method = "tools/call"; params = @{ name = $name; arguments = $arguments } }
    $r = Receive-For $script:id
    if ($r.error) { throw "$name failed: $($r.error | ConvertTo-Json -Depth 5)" }
    Write-Output "=== $name ==="
    Write-Output $r.result.content[0].text
}

$id++
Send @{ jsonrpc = "2.0"; id = $id; method = "initialize"; params = @{
    protocolVersion = "2024-11-05"; capabilities = @{}; clientInfo = @{ name = "ci"; version = "1" } } }
Receive-For $id | Out-Null
Send @{ jsonrpc = "2.0"; method = "notifications/initialized" }

Call "ListCreatableTypes" @{}
Call "CreateNode" @{ fullTypeName = "MyNodes.SinkNode"; left = 400; top = 0 }
Call "ConnectByProperty" @{ senderNodeIndex = 0; senderProperty = "OutputSlot"; receiverNodeIndex = 1; receiverProperty = "InputSlot" }
Call "ValidateWorkflow" @{}
Call "CompileWorkflow" @{ startNodeIndex = 0 }
Call "RunCompiledWorkflow" @{ startNodeIndex = 0; timeoutMs = 60000 }
Call "SaveArchive" @{}
$p.StandardInput.Close()
if (-not $p.WaitForExit(10000)) { $p.Kill($true) }
```

Use an MCP client instead when one is available; the sequence is the same.

## 5. Verify without a UI

```powershell
veloxdev run --archive run/graph.json --start 0 --workspace run --output-tree run/post-run.json
```

The acceptance gate replaces the GUI's drag-and-drop check:

- exit code `0`;
- `"outcome": "Completed"`, `"endedWithError": false`;
- `data` is the value the last node produced (here, `sink: hello from source`).

`post-run.json` is the worker's post-run tree. Inside an MCP session the same run returns `logs` and a
`stateFile` as well.

⚙ **`$type` values are `Namespace.Type, AssemblyName`.** Renaming the component assembly makes every archive
written before the rename unreadable; keep it stable.

⚙ **Version coupling is the component's `VeloxDev.Core` reference**, not the CLI's version number. See
[launch.md](launch.md#choose-the-cli-version) for the allowed drift.
