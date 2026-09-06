<div align="center">

# ⚡ VeloxDev

**Build modern, AI-controllable workflow editors on any .NET GUI — WPF, Avalonia, WinUI, MAUI, WinForms, Razor, or Jalium.**

<!-- Supported GUI frameworks -->
[![WPF](https://img.shields.io/badge/-WPF-512BD4?style=flat-square&logo=dotnet&logoColor=white)](https://learn.microsoft.com/dotnet/desktop/wpf/)
[![Avalonia](https://img.shields.io/badge/-Avalonia-8B5CF6?style=flat-square)](https://avaloniaui.net/)
[![WinUI](https://img.shields.io/badge/-WinUI-0C54A2?style=flat-square&logo=windows&logoColor=white)](https://learn.microsoft.com/windows/apps/winui/)
[![MAUI](https://img.shields.io/badge/-MAUI-512BD4?style=flat-square&logo=dotnet&logoColor=white)](https://learn.microsoft.com/dotnet/maui/)
[![WinForms](https://img.shields.io/badge/-WinForms-512BD4?style=flat-square&logo=dotnet&logoColor=white)](https://learn.microsoft.com/dotnet/desktop/winforms/)
[![Razor](https://img.shields.io/badge/-Razor-512BD4?style=flat-square&logo=dotnet&logoColor=white)](https://learn.microsoft.com/aspnet/core/razor-components/)
[![Jalium](https://img.shields.io/badge/-Jalium-6C5CE7?style=flat-square)](https://github.com/VeryJokerJal/Jalium.UI)

[![NuGet](https://img.shields.io/nuget/v/VeloxDev.Core?color=4caf50&logo=nuget&label=VeloxDev.Core)](https://www.nuget.org/packages/VeloxDev.Core/)
[![NuGet](https://img.shields.io/nuget/v/VeloxDev.Core.Extension?color=4caf50&logo=nuget&label=VeloxDev.Core.Extension)](https://www.nuget.org/packages/VeloxDev.Core.Extension/)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE.txt)
[![GitHub](https://img.shields.io/badge/GitHub-Axvser%2FVeloxDev-181717?logo=github)](https://github.com/Axvser/VeloxDev)

---

**📖 Wiki** — [Online (WASM)](https://axvser.github.io/VeloxDev.Docs/) · [Local](https://github.com/Axvser/VeloxDev.Docs/) — the online Wiki is a WebAssembly app, so its load speed depends on your network.

---

</div>

## ✨ What is VeloxDev?

VeloxDev gives .NET developers a complete foundation for building **interactive workflow editors** — the kind where users drag nodes, wire slots together, and watch data flow through a graph at runtime.

Three ideas hold the whole project together:

1. **One model, every GUI.** The workflow model, compile-time identity, runtime engine, serialization and undo/redo live in `VeloxDev.Core` with **zero UI dependencies**. Platform adapters (WPF, Avalonia, WinUI, MAUI, WinForms, Razor, Jalium) supply only the views. Your graph data and its execution semantics behave identically on every platform.
2. **A real execution engine, not just a canvas.** Besides the drag-and-drop surface, `CompilerEx` compiles any reachable sub-graph into a plan (linear chain / branch / parallel fan-out) and drives it deterministically — including **reverse (Terminal) compilation**: ask "what would this node output?" and it computes just the ancestor cone that feeds it, with no controller needed.
3. **AI is a first-class controller.** A 60+ function-calling *Workflow Agent* lets an LLM inspect, build and mutate graphs at runtime through natural language — with the same undo/redo, validation and lifecycle the GUI uses, plus optional **MCP** tool connectivity.

### The workflow system

| Layer | What it provides | Dependency |
|-------|-----------------|------------|
| ⛓️ **Workflow** | Tree / Node / Slot / Link templates with undo-redo, spatial indexing, deep-zoom canvas math, serialization, and a **compiled execution engine** (forward + reverse) | |
| 🤖 **Workflow Agent** | 60+ Function Calling tools — an AI can create nodes, wire slots, patch properties and run chains at runtime via natural language. Supports **MCP (Model Context Protocol)** for external tools/data. Ships bilingual (en/zh) embedded prompt docs so the agent knows exact tool semantics. | `VeloxDev.Core.Extension` |

### Other building blocks you can reuse

| Layer | What it provides | Dependency |
|-------|-----------------|------------|
| 🪶 **MVVM** | Source generators for observable properties and async, cancellable commands — keeps node ViewModels lightweight | |
| 🎞️ **Transition** | Cross-platform interpolation animation with easing & Fluent API — smooth visual feedback for workflow state changes | [Platform Adapter Package](#platform-adapter-packages) |
| 🎨 **Theme** | Runtime theme switching with animated transitions — instant visual identity for your editor | [Platform Adapter Package](#platform-adapter-packages) |
| 🌀 **AOP** | Compile-time aspect proxies — intercept node execution, add logging or validation without modifying business logic | |
| ⚙️ **MonoBehaviour** | Frame-driven lifecycle loop — tick-based node simulation or real-time graph execution | |

### Platform adapter packages

| Platform | Package | NuGet |
|----------|---------|-------|
| WPF | `VeloxDev.WPF` | [![NuGet](https://img.shields.io/nuget/v/VeloxDev.WPF?color=4caf50&logo=nuget)](https://www.nuget.org/packages/VeloxDev.WPF/) |
| Avalonia | `VeloxDev.Avalonia` | [![NuGet](https://img.shields.io/nuget/v/VeloxDev.Avalonia?color=4caf50&logo=nuget)](https://www.nuget.org/packages/VeloxDev.Avalonia/) |
| WinUI | `VeloxDev.WinUI` | [![NuGet](https://img.shields.io/nuget/v/VeloxDev.WinUI?color=4caf50&logo=nuget)](https://www.nuget.org/packages/VeloxDev.WinUI/) |
| MAUI | `VeloxDev.MAUI` | [![NuGet](https://img.shields.io/nuget/v/VeloxDev.MAUI?color=4caf50&logo=nuget)](https://www.nuget.org/packages/VeloxDev.MAUI/) |
| WinForms | `VeloxDev.WinForms` | [![NuGet](https://img.shields.io/nuget/v/VeloxDev.WinForms?color=4caf50&logo=nuget)](https://www.nuget.org/packages/VeloxDev.WinForms/) |
| Razor | `VeloxDev.Razor` | [![NuGet](https://img.shields.io/nuget/v/VeloxDev.Razor?color=4caf50&logo=nuget)](https://www.nuget.org/packages/VeloxDev.Razor/) |
| Jalium | `VeloxDev.Jalium` | [![NuGet](https://img.shields.io/nuget/v/VeloxDev.Jalium?color=4caf50&logo=nuget)](https://www.nuget.org/packages/VeloxDev.Jalium/) |

Adapter API docs: [WinForms](Src/Adapters/VeloxDev.WinForms/README.md) · [Razor](Src/Adapters/VeloxDev.Razor/README.md)

---

## 🧠 Core concepts (60 seconds)

- **A workflow is a Tree of Nodes.** Every Node is a ViewModel + a *Helper* (component/helper pattern). Nodes own **Slots**; slots are wired into **Links**. A slot has a *channel* (one/many, sender/receiver/both) that governs how connections are validated.
- **Views are templates over the model.** The adapter's `WorkflowSurface` hosts Node/Slot/Link views (plus grid decorator, minimap, ruler and tree views), virtualized against a spatial index so graphs with thousands of nodes stay fluid.
- **Everything the user does is an undoable command.** Move/anchor/size, create/delete slot, connect/disconnect and selector changes all flow through `IVeloxCommand` with a redo/undo pair — the Agent tool layer uses the *same* commands the GUI does.
- **Execution is compiled, then driven.** Nodes expose one receive entry `ReceiveCommand → Helper.ReceiveAsync(ITaskContext, ct)`. The engine (`RuntimeEngine`) compiles a sub-graph into segments once and then *pulls* data through them — nodes do not broadcast to each other on their own during a compiled run.
- **Phases are explicit.** Compile time assigns every node a fixed identity (`Order/ChainIndex/Offset`) and runs dataflow *validation* without data; runtime reuses the same `AccessAsync` gate with real payloads. A context hierarchy (`IContext → IAccessContext → ITaskContext`, plus `ICompileContext` / `IRuntimeContext`) describes every hand-off.

---

## ⚙️ Execution model — compile once, run deterministically

`CompilerViewModel` has **one** API, `CompileAsync(node, role)`:

```csharp
public enum CompileRole { Root, Terminal }   // the node is a starter, or the result you want
```

| Role | Meaning | What it compiles |
|------|---------|------------------|
| `Root` | The node starts a run (e.g. a controller) | Its reachable sub-graph, walking **downstream** along `Targets` |
| `Terminal` | You want this node's result | Its **ancestor cone** — the producers feeding it, walked **backward** along `Sources` — starting automatically from the cone's entry frontier |

```csharp
var compiler = new CompilerViewModel();

// 1) Forward: run the whole chain from a controller
var graph = (await compiler.CompileAsync(controller, CompileRole.Root))[0];
var session = new RuntimeContext();
await new RuntimeEngine().RunAsync(graph, session);
Console.WriteLine(session.Data);            // the chain's final payload

// 2) Reverse: compute just one node's value — no start node needed
var cone = (await compiler.CompileAsync(someNode, CompileRole.Terminal))[0];
var probe = new RuntimeContext { Target = someNode };
await new RuntimeEngine().RunAsync(cone, probe);

if (probe.TargetReached) Console.WriteLine(probe.Data);   // that node's output
else Console.WriteLine("target NOT reached — no value fabricated");
```

The plan it produces is a small tree of segments: a **linear chain**, a **router branch** (a node implementing `ICompileTimeRouter`, Static or Dynamic), and **parallel fan-out groups** — and it is always acyclic; "loops" are expressed as runtime *redirects* (`IRedirectable`), never as graph cycles.

Two properties worth calling out, because they keep reverse compilation honest:

- **Branches are real, never bypassed.** Terminal compilation keeps a router's true `BranchSegment` behavior; it only compiles the branch that leads into the target's cone. If the router actually selects a *sibling* branch at runtime, the target is **not reached** — you get an explicit "… was NOT reached … No result was produced." outcome, never a fabricated value. (Agent tools surface this as a specific error message; set the router's selection to the right branch and retry.)
- **Joins aggregate by source.** A multi-input node receives an `IGroupData` — a read-only map keyed by its upstream node — so a join "waits for all inputs" even when fan-out ran sequentially (the shared runtime session is intentionally not thread-safe).

```csharp
// Minimal "node" — the generator wires INotifyPropertyChanged, slot lifecycle and commands.
[WorkflowBuilder.Node<MyNodeHelper>]
public partial class MyNodeViewModel
{
    public MyNodeViewModel() => InitializeWorkflow();

    [AgentContext(AgentLanguages.English, "Input slot (receiver)")]
    [VeloxProperty] public partial MySlotViewModel InputSlot { get; set; }

    [AgentContext(AgentLanguages.English, "Output slot (sender)")]
    [VeloxProperty] public partial MySlotViewModel OutputSlot { get; set; }

    [VeloxProperty] private string title = "My Node";
}
```

---

## 🤖 AI control

The **Workflow Agent** turns a workflow Tree into a tool surface for any `IChatClient` (Microsoft.Extensions.AI), so an LLM can inspect the graph, build nodes, connect slots, patch properties, and run — or reverse-compute — compiled chains:

```csharp
var scope = tree.AsAgentScope()
    .WithAutoDiscovery(assemblyName: "MyApp")
    .WithInteractionSafety(3)          // confirm before destructive ops; present choices via tool
    .WithSelectionHandler(ShowDialog)
    .WithConfirmationHandler(ShowDialog);

var agent = chatClient.AsAIAgent(
    instructions: scope.ProvideProgressiveContextPrompt(),
    tools: scope.ProvideTools());
```

Highlights of the tool surface:

- **Inspect & mutate like the GUI** — `ListNodes`, `GetFullTopology`, `CreateNode`, `ConnectByProperty`, `PatchNodeProperties`, `SetEnumSlotCollection`, `Undo`/`Redo`, `MoveNode`, … every mutation dispatches a real component command, so undo/redo stays the source of truth.
- **Execute at three levels** — node-level (`ExecuteNode`), chain-level (`RunCompiledWorkflow`, Root role), and **result-level** (`GetNodeResult`, Terminal role). Plans can be read without running via `CompileWorkflow` / `CompileNodeResult`.
- **Gated by policy, not just prose** — node-execution tools are disabled until the host calls `WithAllowNodeExecution(true)`; generic command execution is allow-listed; interaction tools appear only when a selection/confirmation handler is wired. `MaxToolCalls`, `MaxReadToolCalls` and `MaxWriteToolCalls` bound a session.
- **Precision is baked into the prompt, in the host's language** — embedded (en/zh) prompt docs describe tool semantics, error/rejection handling, mount-before-operate and the exact "target not reached" contract, so the agent knows *before calling* what each tool does and what errors mean.

### 🔌 Connect MCP servers for external tooling

```csharp
var mcp = new McpScope()
    .WithMcpRoot(".evn/mcp")
    .WithSynchronizationContext(SynchronizationContext.Current);

var configs = new[]
{
    // Local stdio server (npx)
    new McpServerConfiguration
    {
        Name = "Filesystem",
        RunMode = McpServerRunMode.Npx,
        Package = "@modelcontextprotocol/server-filesystem",
        Arguments = ["C:/data"],
    },
    // Remote server over Streamable HTTP (SSE fallback for legacy servers)
    new McpServerConfiguration
    {
        Name = "Microsoft Learn",
        RunMode = McpServerRunMode.Http,
        Endpoint = "https://learn.microsoft.com/api/mcp",
        Options = new { connectionTimeout = 30 },
        // Header auth:  Options = new { headers = new { Authorization = "Bearer <token>" } }
        // OAuth 2.0:    Options = new { oauth = new { clientId = "...", redirectUri = "...", scopes = new[] { "read" } } }
    },
};

var mcpTools = await mcp.LoadAsync(configs);
var allTools = scope.ProvideTools().Concat(mcpTools).ToArray();   // merge into the agent
```

`McpScope` installs npm packages idempotently, manages stdio/HTTP transports, reports per-server failures without blocking the rest, and supports OAuth via `WithOAuthAuthorizationRedirect(...)`.

---

## 📦 Installation

Install one of the [platform adapter packages](#platform-adapter-packages) listed above and you get everything — workflow, execution engine, agent, animations and theming — wired up for that GUI.

### Generate a view suite from templates

Each adapter ships a `dotnet new` template pack that generates the full view suite — Node, Slot, Link, Tree, template selector, grid decorator and minimap — pre-wired to the model. WPF example (replace `MyApp` with your root namespace):

```powershell
dotnet new install VeloxDev.WPF.Templates
dotnet add package VeloxDev.WPF

dotnet new wpf-v-slot -n SlotView -ns MyApp.Views -o Views
dotnet new wpf-v-node -n NodeView -ns MyApp.Views -o Views
dotnet new wpf-v-link -n LinkView -ns MyApp.Views -o Views
dotnet new wpf-v-selector -n TemplateSelector -ns MyApp.Views -o Views
dotnet new wpf-v-decorator -n GridDecorator -ns MyApp.Views -o Views
dotnet new wpf-v-minimap -n MinimapOverlay -ns MyApp.Views -o Views
dotnet new wpf-v-tree -n TreeView -ns MyApp.Views -o Views

dotnet build
```

Avalonia, WinUI, MAUI, WinForms and Jalium suites expose the same style options (`jalium-v-*` for Jalium). Common style aliases:

| Template | Style aliases |
|----------|---------------|
| Node | `-bg` background, `-fg` foreground, `-bb` border brush, `-bt` border thickness, `-cr` corner radius |
| Slot | `-bg` background, `-sc` standby color, `-bc` border color, `-sp` SVG path data |
| Link | `-lc` line color, `-lt` line thickness |
| Tree | `-bg` background, `-bb` border brush, `-bt` border thickness, `-cr` corner radius |
| Grid decorator | `-bg` background, `-mic` minor color, `-mac` major color, `-ac` axis color, `-gs` spacing, `-mle` major interval, `-rb` ruler background, `-rtc` ruler tick color, `-rlc` ruler label color, `-rdc` ruler divider color |
| Minimap overlay | `-bg` background, `-bdr` border, `-nf` node fill, `-vs` viewport stroke |

All templates use `-ns` for the generated namespace.

### Core-only packages *(bring your own adapter)*

| Package | NuGet | Description |
|---------|-------|-------------|
| `VeloxDev.Core` | [![NuGet](https://img.shields.io/nuget/v/VeloxDev.Core?color=4caf50&logo=nuget)](https://www.nuget.org/packages/VeloxDev.Core/) | Workflow model, MVVM source generators, canvas math and the `CompilerEx` execution engine — multi-targeted down to `netstandard2.0`/`.NET Framework 4.6.1`, zero third-party dependencies |
| `VeloxDev.Core.Extension` | [![NuGet](https://img.shields.io/nuget/v/VeloxDev.Core.Extension?color=4caf50&logo=nuget)](https://www.nuget.org/packages/VeloxDev.Core.Extension/) | MAF-based Workflow Agent tools, MCP scope, and runtime extensions |

---

## 🗂️ Repository Layout

```text
VeloxDev/
├── VeloxDev.slnx
├── Src/
│   ├── Core/
│   │   ├── VeloxDev.Core                 # Workflow model, generators, canvas math, CompilerEx engine
│   │   ├── VeloxDev.Core.Extension       # Workflow Agent tools, MCP scope, runtime extensions
│   │   ├── VeloxDev.Core.Test            # Unit tests (model / canvas / execution)
│   │   └── VeloxDev.Core.Extension.Test  # Unit tests (agent tools / serialization / lifecycle)
│   ├── Adapters/
│   │   ├── VeloxDev.WPF · VeloxDev.Avalonia · VeloxDev.WinUI · VeloxDev.MAUI
│   │   ├── VeloxDev.WinForms · VeloxDev.Razor · VeloxDev.Jalium   # one view layer per platform
│   ├── Generators/
│   │   └── VeloxDev.Core.Generator       # Roslyn source generators (netstandard2.0)
│   └── Templates/                         # dotnet new item template packs per GUI adapter
├── Examples/
│   ├── Workflow/     # WPF · Avalonia · WinUI · WinForms · MAUI · Razor · Jalium
│   │                #   (+ "Trimmed" variants that compile no extraneous generator code)
│   │                #    Common/Lib holds the shared demo node library (Controller, routers, python workers)
│   ├── MVVM/         # WPF · Avalonia
│   ├── Transition/   # WPF · Avalonia · WinUI · WinForms · MAUI · Razor · Jalium
│   ├── Theme/ · AOP/ # WPF · Avalonia
│   └── MonoBehaviour/# WPF
└── Docs/
    └── VeloxDev.Docs # Documentation site (WebAssembly: online/local wiki)
```

---

## 🧪 Tests & coverage

Two test suites live beside the source (`VeloxDev.Core.Test`, `VeloxDev.Core.Extension.Test`) and are deliberately fast (~500+ assertions with no I/O in the hot path):

```bash
dotnet test                          # or open VeloxDev.slnx and run in Visual Studio
# targeted:
dotnet test Src/Core/VeloxDev.Core.Test --filter "FullyQualifiedName~CompilerEx"
```

Coverage is collected with **coverlet** (`--collect:"XPlat Code Coverage"`). The `CompilerEx` execution engine — compile decomposition, runtime driving, redirects, joins, and reverse compilation — is covered end-to-end with self-contained contract tests (probe nodes, no UI/no demo dependency). A full XML-comment/language pass keeps the public API documented in English.

---

## 📄 License

Released under the [MIT License](LICENSE.txt). © 2025 Axvser
