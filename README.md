<div align="center">

# ⚡ VeloxDev

**Build modern, AI-controllable workflow editors on any .NET GUI — WPF, Avalonia, WinUI, MAUI, WinForms, Razor, or Jalium.**

<!-- Supported GUI frameworks: all seven ship from this repository as first-party NuGet packages. -->
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

**📖 Wiki** — [Online](https://axvser.github.io/VeloxDev.Docs/) /  [Local](https://github.com/Axvser/VeloxDev.Docs/) — the online Wiki is a WebAssembly app, so its load speed depends on your network.

---

</div>

> **What this is** — a **node editor / node-graph / workflow-editor** framework for **.NET / C#**. Drag nodes, wire slots into links on a **zoomable, virtualized canvas**, drive the graph with a **compiled, pull-based execution engine**, gate structural edits behind **undo/redo**, and control it all through an **AI agent** (function calling + **MCP**). One model → **7 GUIs**.

## Every GUI above is a first-party adapter

All seven ship from this repository as NuGet packages — not community forks or partial ports. An adapter brings `VeloxDev.Core` plus that platform's view layer:

| Platform | Package | Targets |
|----------|---------|---------|
| WPF | `VeloxDev.WPF` | `netframework4.6.1` · `net5.0-windows` · `netcoreapp3.0` · `net8.0-windows` |
| Avalonia | `VeloxDev.Avalonia` | `netstandard2.0` · `net6.0` · `net8.0` |
| WinUI | `VeloxDev.WinUI` | `net8.0-windows10.0.19041.0` · `net10.0-windows10.0.19041.0` |
| MAUI | `VeloxDev.MAUI` | `net10.0` · `net10.0-windows10.0.19041.0` |
| WinForms | `VeloxDev.WinForms` | `netframework4.6.1` · `net5.0-windows` · `netcoreapp3.0` · `net8.0-windows` |
| Razor | `VeloxDev.Razor` | `net6.0` · `net8.0` |
| Jalium | `VeloxDev.Jalium` | `net10.0` |

`VeloxDev.Core` itself ships `netstandard2.0` · `netframework4.6.1` · `net5.0` · `netcoreapp3.0` · `net8.0`.

> Every adapter now carries a `net8.0` rung, so a `net8.0` app gets a TFM-specific build and the trim/AOT analyzers run against the adapter's own code. **Trimming is still not the same as support**: WPF and WinForms are [not AOT-compatible](https://learn.microsoft.com/gaming/gdk/docs/gdk-dev/pc-dev/tutorials/get-started-with-custom-engine/native-aot-for-gaming) as frameworks, and no adapter declares `IsAotCompatible` yet — see [Scope and status](#scope-and-status).

Adapter API docs: [WinForms](Src/Adapters/VeloxDev.WinForms/README.md) — the one adapter with no markup
language, so its attached-property surface needs spelling out. The other six express it in markup.

## 🤔 Is this the right library for you?

There are two ways in, and they have different answers. **The workflow editor is the core** — but it stands on six layers that are useful without it:

| You are here for | Verdict |
| --- | --- |
| **A workflow editor** — a low-code platform, a visual ETL, a simulation canvas — on more than one GUI toolkit, or one an LLM should be able to read and write through the same commands your UI uses | ✅ **The core.** Nothing else in .NET pairs a node canvas on seven toolkits with a compiled execution engine behind it. |
| **The layers, not the editor** — MVVM source generators, a cross-platform animation engine, runtime theming, a trim-friendly archiver | ✅ **Take them on their own.** None of them needs the editor; each has its own demos and tests here. See [the layers below](#-the-layers-the-editor-is-built-on). |
| **A WPF-only canvas** | ❌ [Nodify](https://github.com/miroiu/nodify) and [NodeNetwork](https://github.com/Wouterdek/NodeNetwork) are smaller, focused, and have no cross-platform layer to pay for. |
| **NativeAOT today** | ⚠️ The archiver is reflection-free, and compiled expression trees run under NativeAOT through the interpreter (measured: see [Scope and status](#scope-and-status)). **WPF and WinForms** are the real blocker — the frameworks themselves are not AOT-compatible. |

## 🧩 The layers the editor is built on

All six live in `VeloxDev.Core` and none of them needs a canvas. They are also what makes the word *modern* in the title more than a slogan:

| Layer | What it gives you | Weight in this repo |
| --- | --- | --- |
| 🪶 **MVVM** | Source generators for observable properties and async, cancellable commands — no `INotifyPropertyChanged` boilerplate | ~4.8k lines · 33 test files |
| 🎞️ **Transition** | An animation **engine**: easing families, keyframe sequences, a timeline you can pause / seek / re-rate, and a shared transport several animations can anchor to so a group stays in lockstep without any of them knowing about the others | ~5.4k lines · 26 test files · 7 platform demos + a conformance harness |
| 🎨 **Theme** | Runtime theme switching that *animates* between states instead of snapping | ~0.9k lines |
| 🌀 **AOP** | Generated aspect interfaces with runtime proxies — intercept members for logging or validation without touching business logic | ~0.3k lines |
| ⚙️ **Tickable** | A frame-driven lifecycle loop with a fixed-step pump beside the variable one, for simulation and real-time work | ~1.2k lines |
| 📦 **Serialization** | A closed-world archiver: the generator emits a reader and writer per type from compiler facts, so **the module contains no reflection at all** — and it writes materially smaller documents than a reflection-based writer (measured in `Src/Verification/`) | ~4.6k lines · 41 test files |

Two of those carry the "modern" claim more than the rest:

- **Serialization is the one that survives trimming.** Its engine, its generated code and its registry touch no reflection; the format is decided entirely at compile time. That is what lets it sit inside a trimmed or AOT-published app — and it is the same design that makes an unseen type fail loudly with `MissingWriter` instead of silently serializing an empty shell.
- **Transition is an engine, not a tween helper.** One `ITimeSourceControl` can be shared by many animations: pausing, re-rating or seeking it moves them all together while each keeps its own pass and its own place in it. It reaches seven GUIs through the same Core, with each adapter contributing only a pacer subclass.

## ⚡ What it looks like in code

A workflow is a **tree of nodes**; nodes own **slots**, and slots are wired into **links**. A slot has a *channel* (one/many × sender/receiver/both) that governs which connections are legal. `VeloxDev.Core` holds that model, the undo/redo stack, the execution engine and serialization with **zero UI dependencies** — the adapters add views and platform glue only.

The snippets below walk one three-node chain, `ticker → bias → printer`.

### A node is a partial class

```csharp
// The generator wires INotifyPropertyChanged, slot lifecycle and the commands from these attributes.
[WorkflowBuilder.Node<BiasNodeHelper>]
public partial class BiasNodeViewModel
{
    public BiasNodeViewModel() => InitializeWorkflow();

    [VeloxProperty] public partial BiasSlotViewModel InputSlot { get; set; }
    [VeloxProperty] public partial BiasSlotViewModel OutputSlot { get; set; }
    [VeloxProperty] private string title = "Bias";
}

public sealed class BiasNodeHelper : NodeHelper<BiasNodeViewModel>
{
    // What this node computes. The return value is handed to the next node in the chain.
    public override Task<object?> ReceiveAsync(ITaskContext context, CancellationToken ct)
        => Task.FromResult<object?>($"{context.Data}->bias");
}
```

### The canvas is edited through undoable commands

```csharp
tree.CreateNodeCommand.Execute(bias);
tree.SendConnectionCommand.Execute(ticker.OutputSlot);   // start from the sender…
tree.ReceiveConnectionCommand.Execute(bias.InputSlot);   // …complete on the receiver
tree.UndoCommand.Execute(null);                          // the connect is one undoable step
```

### Compile once, then run

```csharp
var compiler = new CompilerViewModel();
var graph = (await compiler.CompileAsync(ticker, CompileRole.Root)).Single();

var session = new RuntimeContext();
await new RuntimeEngine().RunAsync(graph, session, CancellationToken.None);
Console.WriteLine(session.Data);          // tick->bias->print
```

### Or ask for one node's result — with no start node

```csharp
var cone = (await compiler.CompileAsync(printer, CompileRole.Terminal)).Single();

var probe = new RuntimeContext { Target = printer };
await new RuntimeEngine().RunAsync(cone, probe, CancellationToken.None);
Console.WriteLine(probe.TargetReached ? probe.Data : "not reached");
```

### And an LLM can drive all of it

```csharp
var scope = tree.AsAgentScope().WithAutoDiscovery().WithInteractionSafety(3);
var agent = chatClient.AsAIAgent(scope.ProvideProgressiveContextPrompt(), scope.ProvideTools());
```

---

## ⚙️ Execution model — compile once, run deterministically

`CompilerViewModel` has **one** API, `CompileAsync<T>(node, role, ct = default)`. The role decides which way the compiler walks:

| Role         | Meaning                                   | What it compiles                                                                                                                                             |
| ------------ | ----------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| `Root`     | The node starts a run (e.g. a controller) | Its reachable sub-graph, walking**downstream** along `Targets`                                                                                       |
| `Terminal` | You want this node's result               | Its**ancestor cone** — the producers feeding it, walked **backward** along `Sources` — starting automatically from the cone's entry frontier |

The plan is a small tree of segments — a **linear chain**, a **router branch** (a node implementing `ICompileTimeRouter`, Static or Dynamic), and **fan-out groups** — and it is always acyclic. Looping is expressed as runtime *redirects* instead of graph cycles: when a node signals an error, the engine checks for `IRedirectable` and, if the node implements it, re-runs the graph toward the returned target under an internal retry limit. The demo's Python node is the reference implementation.

Three properties keep reverse compilation honest:

- **Branches are real, never bypassed.** If a router actually selects a *sibling* branch at runtime, the target is **not reached** — you get an explicit "… was NOT reached … No result was produced." outcome, never a fabricated value.
- **Joins aggregate by source.** A multi-input node receives an `IGroupData` — a read-only map keyed by its upstream node — so a join "waits for all inputs" however the fan-out's branches interleaved. (The shared runtime session is intentionally not thread-safe, which is why a branch that burns CPU still takes the thread in turn.)
- **`TargetReached` means "the target was driven", not "a value was produced".** Read it together with `Outcome` (`Unknown` / `Completed` / `Cancelled` / `Failed`), not with the raw `Status` string — `Status` has to share one value between a failure and a cancellation.

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

The **Workflow Agent** turns a workflow Tree into a tool surface for any `IChatClient` (Microsoft.Extensions.AI):

```csharp
var scope = tree.AsAgentScope()
    .WithAutoDiscovery()               // reads the compile-time context tree
    .WithInteractionSafety(3)          // confirm before destructive ops; present choices via tool
    .WithSelectionHandler(ShowDialog)
    .WithConfirmationHandler(ShowDialog);

var agent = chatClient.AsAIAgent(
    instructions: scope.ProvideProgressiveContextPrompt(),
    tools: scope.ProvideTools());
```

Highlights of the tool surface:

- **Inspect & mutate like the GUI** — `ListNodes`, `GetFullTopology`, `CreateNode`, `ConnectByProperty`, `PatchNodeProperties`, `SetEnumSlotCollection`, `Undo`/`Redo`, `MoveNode`, … every mutation dispatches the same component command the GUI dispatches, so the Agent and the GUI share one edit path — including the same undo semantics, i.e. the moves and property patches that create no undo entry.
- **Execute at three levels** — node-level (`ExecuteNode`), chain-level (`RunCompiledWorkflow`, Root role), and **result-level** (`GetNodeResult`, Terminal role). Plans can be read without running via `CompileWorkflow` / `CompileNodeResult`, and a long run can be handed back as a handle to pause, resume, poll or stop.
- **Gated by policy, not just prose** — node-execution tools are disabled until the host calls `WithAllowNodeExecution(true)`; generic command execution is allow-listed; interaction tools appear only when a selection/confirmation handler is wired. `MaxToolCalls`, `MaxReadToolCalls` and `MaxWriteToolCalls` bound a session.
- **Precision is baked into the prompt** — embedded (en/zh) prompt docs describe tool semantics, error/rejection handling, mount-before-operate and the exact "target not reached" contract, so the agent knows *before calling* what each tool does and what errors mean.

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

Install a platform adapter package (workflow, execution engine, animation, theming and the platform view layer), and add `VeloxDev.Core.Extension` **only if you want the Agent**:

```powershell
dotnet add package VeloxDev.WPF                 # an adapter — Core + WPF views
dotnet add package VeloxDev.Core.Extension      # optional — the Agent, MCP, checkpoints
```

### Generate a view suite from templates

Each adapter ships a `dotnet new` template pack that generates the full view suite — Node, Slot, Link, Tree, template selector, grid decorator and minimap. WPF example (replace `MyApp` with your root namespace):

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

The other adapters expose the same seven items under their own prefix — `ava-v-*` (Avalonia), `winui-v-*`, `maui-v-*`, `winforms-v-*`, `razor-v-*`, `jalium-v-*`. Every item takes `-ns` for the generated namespace; each view also takes style options (`-bg`, `-fg`, `-cr`, …) documented inside its own template pack.

---

## 🧪 Tests & verification

Two test suites live beside the source — **1771 `[TestMethod]`s** (1129 in `VeloxDev.Core.Test`, 642 in `VeloxDev.Core.Extension.Test`), MSTest 4.0.2 on `net10.0`:

```bash
dotnet test                          # or open VeloxDev.slnx and run in Visual Studio
# targeted:
dotnet test Src/Core/VeloxDev.Core.Test --filter "FullyQualifiedName~CompilerEx"
```

Coverage is collected with **coverlet** (`--collect:"XPlat Code Coverage"`). The `CompilerEx` engine — compile decomposition, runtime driving, redirects, joins, reverse compilation — is covered end-to-end with self-contained contract tests (probe nodes, no UI and no demo dependency).

`Src/Verification/VeloxDev.Serialization.Benchmarks` measures the archive engine against `System.Text.Json` and Newtonsoft at four graph sizes. **Read its caveats before quoting a number**: `Src/Verification/README.md` states the in-process toolchain shares JIT and GC, so absolute timings are not comparable across processes — the run is meant to compare *magnitudes on one machine in one session*, and the allocation column is the most trustworthy.

---

## Scope and status

The four things worth knowing before you depend on it:

| | |
|---|---|
| **AOT and trimming are partial** | `VeloxDev.Core` and `VeloxDev.Core.Extension` declare `IsAotCompatible=true` on their `net8.0` target; the adapters deliberately do **not** — they only switch the analyzers on, so a consumer sees the adapter's own trim warnings without the library making a claim it has not earned. Expression trees are *not* a blocker: under NativeAOT `Expression.Compile()` falls back to the interpreter and works (probe: `IsDynamicCodeSupported=False`, result correct) — it is a speed cost, not a failure. The remaining hazards are two reflection sites (`TransitionProperty.cs` for indexer metadata, `CompileKeyNormalizer.cs` for branch-key type names) and **28 trim warnings** the newly-enabled analyzers surfaced across the seven adapters (WPF 4, WinForms 6, Avalonia 4, Jalium 14; Razor, WinUI and MAUI are clean) — all in the theme value converters, one WinForms node attachment, and Jalium's port geometry. |
| **Undo coverage is structural** | Create/delete node or slot, connect/disconnect and selector cascades are undoable; position, size and direct property patches are not. |
| **Fan-out overlaps, but is not thread parallelism** | Branches interleave as async operations on the host's `SynchronizationContext`, sharing one runtime blackboard that is intentionally not thread-safe. A node that starts its own `Task.Run` is on its own. |
| **Redirects ship one implementation** | The engine drives `IRedirectable` end to end — the demo's Python node is the reference. A redirect target must be strictly backward. |

The rest is opt-in and off by default: pausing, observing, retrying, checkpointing and resuming a run all hang off optional `RuntimeContext` members (`IExecutionGate`, `IExecutionObserver`, `INodeRetryPolicy`, `IExecutionErrorSink`, `IExecutionCompensation`, `IExecutionCheckpointStore`) — with none configured, a run behaves exactly as it did before they existed. Waiting and offloading CPU work are node-body decisions rather than engine features, ease overshoot (Back/Elastic) is still handled per sampler type, and the one-model / 7-GUI seam sits at data and geometry — the *view* layer is still written per platform.

## 📄 License

Released under the [MIT License](LICENSE.txt). © 2025 Axvser
