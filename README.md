<div align="center">

> **What this is** — a **node editor / node-graph / workflow-editor** framework for **.NET / C#**. Drag nodes, wire slots into links on a **zoomable, virtualized canvas**, drive the graph with a **compiled, pull-based execution engine**, gate structural edits behind **undo/redo**, and control it all through an **AI agent** (function calling + **MCP**). One model → **7 GUIs**.

## 🗂️ What's in this repository

Everything the badges above promise is built here — the seven adapters, the seven template packs and the source generator included. The Wiki is the manual; this file is the map.

| Path                                                                    | What it holds                                                                                                                                                                      |
| ----------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| [`Src/Core/VeloxDev.Core`](Src/Core/VeloxDev.Core)                     | the editor: the node / slot / link model, canvas geometry and hit testing, a virtualized spatial index, undo/redo, the execution engine, and six layers that need no canvas at all |
| [`Src/Core/VeloxDev.Core.Extension`](Src/Core/VeloxDev.Core.Extension) | everything AI — the Agent over a workflow tree, MCP, skills, sub-agents, checkpoints. One-directional: Core never references it                                                   |
| [`Src/Adapters/`](Src/Adapters/)                                       | the seven platform adapters, one per badge above                                                                                                                                   |
| [`Src/Generators/`](Src/Generators/)                                   | the Roslyn generator that writes the observable properties, commands and hooks; it ships inside the packages as an analyzer                                                        |
| [`Src/Templates/`](Src/Templates/)                                     | the seven`dotnet new` item-template packs                                                                                                                                        |
| [`Src/Verification/`](Src/Verification/)                               | the archiver benchmark, the trim probe, and the harnesses the demos are checked with                                                                                               |
| [`Examples/`](Examples/)                                               | a demo per feature per GUI — including trimmed-publish demos on seven platforms                                                                                                   |
| [`skills/`](skills/)                                                   | seven Claude Code skills that teach an agent to write*your* code against this library                                                                                            |
| [`Docs/`](Docs/)                                                       | the Wiki source behind the link above                                                                                                                                              |

## 📦 Packages

### An adapter per GUI

All seven ship from this repository as NuGet packages — not community forks or partial ports. An adapter brings `VeloxDev.Core` plus that platform's view layer:

| Platform | Package               | Targets                                                                                |
| -------- | --------------------- | -------------------------------------------------------------------------------------- |
| WPF      | `VeloxDev.WPF`      | `netframework4.6.1` · `net5.0-windows` · `netcoreapp3.0` · `net8.0-windows` |
| Avalonia | `VeloxDev.Avalonia` | `netstandard2.0` · `net6.0` · `net8.0`                                         |
| WinUI    | `VeloxDev.WinUI`    | `net8.0-windows10.0.19041.0` · `net10.0-windows10.0.19041.0`                      |
| MAUI     | `VeloxDev.MAUI`     | `net10.0` · `net10.0-windows10.0.19041.0`                                         |
| WinForms | `VeloxDev.WinForms` | `netframework4.6.1` · `net5.0-windows` · `netcoreapp3.0` · `net8.0-windows` |
| Razor    | `VeloxDev.Razor`    | `net6.0` · `net8.0`                                                               |
| Jalium   | `VeloxDev.Jalium`   | `net10.0`                                                                            |

Every adapter now carries a `net8.0` (or later) rung, and on that rung the trim and AOT analyzers are switched on — so *your* build reports the adapter's own trim warnings instead of staying quiet. That is analysis, not a promise: **WPF and WinForms are not AOT-compatible as frameworks**, and Blazor ships no frame pacer because no timer there fires on the renderer's own thread. Trimming and AOT stay per-platform realities rather than one library-wide claim.

`VeloxDev.Core` itself ships `netstandard2.0` · `netframework4.6.1` · `net5.0` · `netcoreapp3.0` · `net8.0`, and declares `IsAotCompatible` on `net8.0`.

### Core, and the optional extension

| Package                     | What it is                                                              | Install it when                                                                           |
| --------------------------- | ----------------------------------------------------------------------- | ----------------------------------------------------------------------------------------- |
| `VeloxDev.Core`           | the editor, the execution engine and the six layers — no UI dependency | you write your own views, drive the layers headless, or want no AI surface at all         |
| `VeloxDev.Core.Extension` | the Agent over a workflow tree, MCP, skills, sub-agents, checkpoints    | you want a model to read and edit the graph through the same commands your GUI dispatches |

**What `Core.Extension` adds**, precisely — it is additive, and `Core` never references it:

- **Inspect and mutate like the GUI** — `ListNodes`, `GetFullTopology`, `CreateNode`, `ConnectByProperty`, `PatchNodeProperties`, `SetEnumSlotCollection`, `Undo`/`Redo`, `MoveNode`, … every mutation dispatches the same component command the GUI dispatches, so the Agent and the GUI share one edit path — including the same undo semantics, i.e. the moves and property patches that create no undo entry.
- **Execute at three levels** — node (`ExecuteNode`), chain (`RunCompiledWorkflow`, Root role) and result (`GetNodeResult`, Terminal role). A plan can be read without running it (`CompileWorkflow` / `CompileNodeResult`), and a long run can be handed back as a handle to pause, resume, poll or stop.
- **Gated by policy, not just prose** — node-execution tools are disabled until the host calls `WithAllowNodeExecution(true)`; generic command execution is allow-listed; interaction tools appear only when a selection/confirmation handler is wired; `MaxToolCalls`, `MaxReadToolCalls` and `MaxWriteToolCalls` bound a session.
- **Four subsystems you can also take alone** — MCP servers, skills, sub-agents that spend from the parent's budget ledger, and a read-only dashboard mirror for the host UI.

```powershell
dotnet add package VeloxDev.Core              # the editor + the six layers, no AI surface
dotnet add package VeloxDev.WPF               # an adapter — Core + the WPF view layer
dotnet add package VeloxDev.Core.Extension    # additive — the Agent, MCP, skills, checkpoints
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

## ⚡ A workflow, in code

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

### Input is one router — and there is no highlight API, on purpose

The adapter translates native pointer and key input **once**, and Core fans it out:

```csharp
// The adapter's job: say where the pointer is, and who is under it.
WorkflowInput.For(tree).Route(new Wf.PointerMovedEventArgs(anchor, modifiers, source, target, new WorkflowEventHandle()));

// Core's job: expand the ancestor chain (link → tree, slot → node → tree, blank → tree), send Exited to whatever
// the pointer just left and Entered to whatever it just entered, and deliver target-first along that chain.
```

`WorkflowInput` performs **no action of its own** — no delete, no context menu, no highlight. A component's `Helper` exposes an `IInputEvents.Input` relay, and anything that wants a behaviour *registers for it where you can read it*:

```csharp
// Hover highlight, written by the host. There is no ILinkHighlight and no AutoHighlight in Core.
if (link?.GetHelper() is IInputEvents events)
{
    events.Input.PointerEntered += (_, _) => IsHighlighted = true;
    events.Input.PointerExited  += (_, _) => IsHighlighted = false;
}

// Delete is the host's too: routing brings the key to the link, and stops there.
private void OnKeyDown(object? sender, KeyDownEventArgs e)
{
    if (e.Key != InputKey.Delete || e.Handle.PreventDefault) return;
    if (DataContext is IWorkflowLinkViewModel link && link.DeleteCommand.CanExecute(null))
        link.DeleteCommand.Execute(null);
}
```

Three things this buys you, none of which the framework had to implement:

- **Mutual exclusion for free.** The router guarantees the link being left receives `Exited` before the next one receives `Entered`, so nothing has to track which link is currently lit.
- **Hit testing against what you actually see.** A link view publishes the curve it painted (`PublishCurve`), and hit testing runs against that published geometry rather than against anchors — so the curve you click is the curve on screen.
- **Seven different answers, none of them blessed.** The demos each do it their own way — WPF draws a halo behind the highlighted line, WinForms compares each link against `WorkflowInput.HoveredLink`, MAUI paints a dedicated overlay layer. The choice of condition, colour and glow is yours, because the framework never picked one.

### Execution model — compile once, run deterministically

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

Pausing, observing, retrying, checkpointing and resuming a run all hang off optional `RuntimeContext` members (`IExecutionGate`, `IExecutionObserver`, `INodeRetryPolicy`, `IExecutionErrorSink`, `IExecutionCompensation`, `IExecutionCheckpointStore`) — with none configured, a run behaves exactly as it did before they existed.

---

## 🎞️ Transition, in code

*Modern* in the tagline is not only about trimming and AOT — it is also about how the thing looks. Links that glow, panels that ease instead of jumping, a theme that animates between states: aesthetics is a feature here, and it is served by an **engine**, not a tween helper.

A transition is a chain you build, then execute — values, then the effect that governs how they get there:

```csharp
// WPF's entry point. Every adapter exposes the same one over its own types.
var rise = Transition<Rectangle>.Create()
    .Property(r => ((TranslateTransform)r.RenderTransform).X, 40d)
    .Property(r => r.Opacity, 1d)
    .Effect(new TransitionEffect { Duration = TimeSpan.FromSeconds(1), Ease = Eases.Back.Out });

rise.Execute(rect);            // or chain further: .Await(TimeSpan) · .Then() · .Repeat(n)
```

Several transitions can share **one transport**, and that is where it stops looking like a tween library:

```csharp
var fade = Transition<Rectangle>.Create()
    .Property(r => r.Opacity, 0d)
    .Effect(new TransitionEffect { Duration = TimeSpan.FromSeconds(1), FPS = 60 });

// One timeline, two animations. Pausing, seeking or re-rating it moves both,
// while each keeps its own pass and its own position in it.
var timeline = TimerCore.CreateTimeSource<ITimeSourceControl>();
rise.Execute(rectA, timeline);
fade.Execute(rectB, timeline);

Transition.Pause(rectA);            // both stop together
Transition.SetRate(rectA, 0.25);    // re-rate — neither position jumps
```

Each adapter contributes only a small platform piece: a **frame pacer** that decides when the next sampling pass happens — `DispatcherTimer` on WPF and Avalonia, `DispatcherQueueTimer` on WinUI, `IDispatcherTimer` on MAUI, a pooled timer posted to `Control.BeginInvoke` on WinForms. Razor deliberately ships none, because Blazor has no timer that fires on the renderer's own thread. Everything above that seam — easing families, keyframes, the timeline, the scheduler — is the same Core on all seven.

## 🧩 The other layers the editor is built on

All six live in `VeloxDev.Core` and none of them needs a canvas. Two are worth showing; the rest are a table.

**AOP — intercept a member without touching the class that declares it.** The type only marks where the seams are; the aspects are installed from outside, at runtime:

```csharp
// In the ViewModel: no aspect code, just the mark.
public partial class TeamViewModel
{
    [VeloxProperty][AspectOriented] private string _name = "Team";
    [AspectOriented] public void Reset() { /* … */ }
}

// Everywhere else: start runs before the member, coverage replaces its body, end runs after it.
var proxy = team.Aop();                     // generated, cached per instance
proxy.SetProxy(ProxyMembers.Getter, nameof(TeamViewModel.Name),
    (_, _) => { Log($"read at {DateTime.Now}"); return null; }, null, null);

proxy.SetProxy(ProxyMembers.Method, nameof(TeamViewModel.Reset),
    null, (_, _) => { Log("Reset() was replaced"); return null; }, null);
```

Hand `SetProxy` the real object instead of the proxy and it throws rather than silently doing nothing — the failure mode a proxy API usually hides.

**Tickable — a frame loop with a fixed-step pump beside it.** Marks the class, implements the hooks, and the loop registers itself:

```csharp
[Tickable("simulation")]                    // the generator implements ITickable and registers the instance
public partial class MainWindow
{
    partial void Update(FrameEventArgs e)      => ball.Step(e.DeltaTime.TotalSeconds);
    partial void FixedUpdate(FrameEventArgs e) => /* every owed fixed step, replayed after a hitch */;
}

TickManager.SetFixedUpdateInterval(16, "simulation");
TickManager.Pause("simulation");            // both pumps park on the bus: a paused loop costs no wake-ups
```

| Layer                     | What it gives you                                                                                                                                                                                                                                                                                                                       |
| ------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| 🪶**MVVM**          | Source generators for observable properties and async, cancellable commands — no`INotifyPropertyChanged` boilerplate                                                                                                                                                                                                                 |
| 🎞️**Transition**  | The animation engine above — see[its own section](#-transition-in-code)                                                                                                                                                                                                                                                                 |
| 🎨**Theme**         | Runtime theme switching that*animates* between states instead of snapping                                                                                                                                                                                                                                                             |
| 🌀**AOP**           | Generated aspect interfaces with runtime proxies — intercept members for logging or validation without touching business logic                                                                                                                                                                                                         |
| ⚙️**Tickable**    | A frame-driven lifecycle loop with a fixed-step pump beside the variable one, for simulation and real-time work                                                                                                                                                                                                                         |
| 📦**Serialization** | A closed-world archiver: the generator emits a reader and writer per type from compiler facts, so**the module contains no reflection at all** — which is what lets it sit inside a trimmed or AOT-published app, and what makes an unseen type fail loudly with `MissingWriter` instead of silently serializing an empty shell |

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

What makes the tool surface hold up under a real session:

- **The Agent edits like the GUI does.** Every mutation dispatches the same component command the GUI dispatches, so the two share one edit path and one undo stack — and the same undo semantics.
- **Three execution levels, and plans you can read without running.** Node-level, chain-level and result-level, all through the same compiler the canvas uses.
- **Gated by policy, not prose.** Node execution is off until the host allows it, generic command execution is allow-listed, and the three budget caps bound a session.
- **Precision is baked into the prompt.** Embedded (en/zh) prompt docs describe tool semantics, error and rejection handling, mount-before-operate and the exact "target not reached" contract, so the agent knows *before calling* what each tool does and what an error means.

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

## 📄 License

Released under the [MIT License](LICENSE.txt). © 2025 Axvser
