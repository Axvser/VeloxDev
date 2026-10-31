# VeloxDev

**A node-editor and workflow-engine framework for .NET.**

![VeloxDev Flow Studio — a professional node editor built with VeloxDev](https://pic1.imgdb.cn/i/034i52aFN0kg2b1nmdDcYp.png)

**One model, seven GUIs.** Build the editor above with **WPF, Avalonia, WinUI, MAUI, WinForms, Blazor/Razor, or Jalium** — the same graph model, commands and execution engine behind all of them. The same model also runs headless through `VeloxDev.Core`, so the interactive and non-interactive paths are one implementation.

[Documentation](https://axvser.github.io/VeloxDev.Docs/) · [NuGet](https://www.nuget.org/packages/VeloxDev.Core) · [Examples](Examples/) · [MIT License](LICENSE.txt)

## What this repository is

A workflow is a **tree of nodes**. Nodes own **slots**, slots are wired into **links**, and a slot declares a **channel** — how many connections it accepts in each direction — so the model itself knows which wiring is legal.

VeloxDev is built around that model, for the two ways it gets used:

- **Interactive** — a person edits a node editor: drags nodes, wires ports, pans and zooms, undoes structural changes.
- **Non-interactive** — a program, a service, or an AI agent builds the graph, compiles it, and runs it headlessly.

These are not two implementations. They use the same model, the same commands, the same connection validation and the same undo stack. A graph edited on a canvas runs in a console; a graph built by code or an Agent opens on the canvas and can be edited there.

### The four layers

| Layer                                                | It owns                                                                                                                                                                                                                   | It does not own                                                                                                                         |
| ---------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------- |
| **`VeloxDev.Core`**                          | The tree / node / slot / link model, undoable commands, canvas geometry and hit testing, spatial virtualization, standard input routing, the compiler and runtime, and the closed-world serializer. Zero UI dependencies. | Any drawing, any native input, any platform lifecycle.                                                                                  |
| **`VeloxDev.Core.Extension`**                | The optional AI surface over a tree: Agent tools, MCP, skills, sub-agents, and file-backed checkpoints. Additive — `Core` never references it.                                                                         | Anything the editor needs to run. A plain editor does not install it.                                                                   |
| **`VeloxDev.<GUI>`** (seven adapters)        | Platform mechanics: translate native pointer and key input, pool and place views, implement drag / pan / zoom / connect gestures, focus and scroll routing, and — where the platform has one — a frame pacer.           | Graph rules, execution semantics, and what interaction should look like.                                                                |
| **`VeloxDev.<GUI>.Templates`** (seven packs) | `dotnet new` item templates for the seven view roles: tree, node, slot, link, template selector, grid decorator, minimap.                                                                                               | Interaction effects and app policy. Templates draw the passive view; your app decides how a hover, a selection or a theme change looks. |

The **source generator** (`VeloxDev.Core.Generator`) is what makes the model pleasant to write: it turns `[WorkflowBuilder.*]`, `[VeloxProperty]` and `[VeloxCommand]` into `INotifyPropertyChanged`, commands, helper lifecycle and serializer code. It ships inside the packages as an analyzer.

### What it solves

| If you are building…                                         | VeloxDev gives you…                                                                                                                                                                |
| ------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| A node editor for automation, data pipelines, or agent graphs | The complete editing layer: model, commands, undo/redo, coordinate math, and hit testing against the curve the view painted                                                         |
| A canvas that stays responsive on large graphs                | A spatial grid and virtualized visible set (`TreeHelper(cellSize)`), plus adapter-side view pooling                                                                               |
| Wiring rules that cannot be bypassed                          | Slot channels, two-phase connection (`SendConnection` → `ReceiveConnection`), a `ValidateConnection` hook, and drag-time preview checks with reason codes                    |
| Ports that change with the node's configuration               | `SlotEnumerator<T>` + `[SlotSelectors]`: an enum, `bool`, or your own provider rebuilds the ports; existing links are restored where possible                                 |
| A workflow runtime                                            | A compiled plan with chains, router branches, parallel fan-out and joins; redirects instead of cycles; optional pause, observation, retry, error, compensation and checkpoint hooks |
| Headless execution in a service or CI                         | `VeloxDev.Core` only — no UI assembly is referenced, and the graph can be built, compiled and run from a console                                                                 |
| An AI agent that can operate the editor                       | `VeloxDev.Core.Extension`: tools that dispatch the same commands as the canvas, behind explicit policy gates and call budgets                                                     |
| The same product on several GUIs                              | Seven adapters with the same public surface: WPF, Avalonia, WinUI, MAUI, WinForms, Razor, Jalium                                                                                    |

## Packages

```powershell
dotnet add package VeloxDev.Core              # the graph model + engine, no UI
dotnet add package VeloxDev.WPF               # one adapter (brings Core)
dotnet add package VeloxDev.Core.Extension    # optional — the AI surface
```

| Platform | Package               | Targets                                                                                |
| -------- | --------------------- | -------------------------------------------------------------------------------------- |
| WPF      | `VeloxDev.WPF`      | `netframework4.6.1` · `net5.0-windows` · `netcoreapp3.0` · `net8.0-windows` |
| Avalonia | `VeloxDev.Avalonia` | `netstandard2.0` · `net6.0` · `net8.0`                                         |
| WinUI    | `VeloxDev.WinUI`    | `net8.0-windows10.0.19041.0` · `net10.0-windows10.0.19041.0`                      |
| MAUI     | `VeloxDev.MAUI`     | `net10.0` · `net10.0-windows10.0.19041.0`                                         |
| WinForms | `VeloxDev.WinForms` | `netframework4.6.1` · `net5.0-windows` · `netcoreapp3.0` · `net8.0-windows` |
| Razor    | `VeloxDev.Razor`    | `net6.0` · `net8.0`                                                               |
| Jalium   | `VeloxDev.Jalium`   | `net10.0`                                                                            |

`VeloxDev.Core` itself targets `netstandard2.0`, `netframework4.6.1`, `net5.0`, `netcoreapp3.0` and `net8.0`, and declares `IsAotCompatible` on `net8.0`. Trimming and AOT remain per-platform realities: the adapters enable the analyzers, but the GUI framework itself may not support them.

### Generate the view suite

Each adapter ships a template pack with the same seven items.

```powershell
dotnet new install VeloxDev.WPF.Templates

dotnet new wpf-v-tree      -n WorkflowView     -ns MyApp.Views
dotnet new wpf-v-node      -n NodeView         -ns MyApp.Views
dotnet new wpf-v-slot      -n SlotView         -ns MyApp.Views
dotnet new wpf-v-link      -n LinkView         -ns MyApp.Views
dotnet new wpf-v-selector  -n TemplateSelector -ns MyApp.Views
dotnet new wpf-v-decorator -n GridDecorator    -ns MyApp.Views
dotnet new wpf-v-minimap   -n MinimapOverlay   -ns MyApp.Views
```

Other adapters use their own prefix: `ava-v-*`, `winui-v-*`, `maui-v-*`, `winforms-v-*`, `razor-v-*`, `jalium-v-*`.

## Workflow in code

The snippets below walk one three-node chain: `ticker → bias → printer`.

### A node is a partial class

```csharp
// The generator supplies INotifyPropertyChanged, the commands and the Helper lifecycle.
[WorkflowBuilder.Node<BiasNodeHelper>]
public partial class BiasNodeViewModel
{
    public BiasNodeViewModel() => InitializeWorkflow();

    [VeloxProperty] public partial SlotViewModel Input { get; set; }
    [VeloxProperty] public partial SlotViewModel Output { get; set; }
    [VeloxProperty] private string title = "Bias";
}

public sealed class BiasNodeHelper : NodeHelper<BiasNodeViewModel>
{
    // What this node computes. The return value is passed to the next node in the chain.
    public override Task<object?> ReceiveAsync(ITaskContext context, CancellationToken ct)
        => Task.FromResult<object?>($"{context.Data}->bias");
}
```

Slot types are declared the same way, with `[WorkflowBuilder.Slot<...>]`. A slot's `Channel` decides its capacity:

```csharp
output.SetChannelCommand.Execute(SlotChannel.MultipleTargets); // fan out from this port
input.SetChannelCommand.Execute(SlotChannel.OneSource);        // one upstream connection only
```

### Edit through the same commands the canvas uses

```csharp
tree.CreateNodeCommand.Execute(bias);
tree.SendConnectionCommand.Execute(ticker.Output);   // start from the sender…
tree.ReceiveConnectionCommand.Execute(bias.Input);   // …complete on the receiver
tree.UndoCommand.Execute(null);                      // the connection is one undoable step
```

Connection legality is a host rule, not a hard-coded policy:

```csharp
public sealed class GraphHelper : TreeHelper<GraphViewModel>
{
    public override bool ValidateConnection(
        IWorkflowSlotViewModel sender, IWorkflowSlotViewModel receiver)
        => receiver.Parent is not SinkNodeViewModel;   // your rule
}
```

During a drag, the same rules produce a preview for the candidate port, with a reason when it is invalid. A host can narrow a valid candidate further, but cannot turn a framework rejection into a valid one.

### Compile once, run deterministically

```csharp
var compiler = new CompilerViewModel();
var graph = (await compiler.CompileAsync(ticker, CompileRole.Root)).Single();

var session = new RuntimeContext { Data = "seed" };
await new RuntimeEngine().RunAsync(graph, session, CancellationToken.None);

Console.WriteLine(session.Data);   // seed -> tick -> bias -> print
```

`CompileRole.Root` walks downstream from a start node. `CompileRole.Terminal` does the opposite — it compiles only the ancestor cone needed for one node's result, and needs no start node:

```csharp
var cone = (await compiler.CompileAsync(printer, CompileRole.Terminal)).Single();

var probe = new RuntimeContext { Target = printer };
await new RuntimeEngine().RunAsync(cone, probe, ct);

Console.WriteLine(probe.TargetReached ? probe.Data : "target not reached");
```

The execution model is deliberately explicit:

- **Branches are real.** A node implementing `ICompileTimeRouter` compiles to a branch; static keys are pruned at compile time, dynamic keys are resolved at runtime. If a router selects a sibling branch, the target is reported as not reached — never fabricated.
- **Fan-out is concurrent, joins are keyed.** A route key with several targets compiles to a parallel segment; a join receives an `IGroupData` keyed by the upstream node, so "wait for all inputs" works regardless of interleaving.
- **Loops are redirects, not cycles.** `IRedirectable` returns an upstream node; the engine maps it to that node's compile order and re-runs the graph from there, up to 50 redirects per run.
- **Run controls are optional session objects.** `ExecutionGate`, `Observer`, `RetryPolicy`, `ErrorSink`, `Compensation` and `CheckpointStore` are attached to `RuntimeContext`; none of them changes a run when absent.
- **Outcome and position are separate.** `TargetReached` says the target was driven; `Outcome` says whether the run completed, was cancelled or failed.

### Input is routed; appearance is yours

Adapters translate native pointer and key input once. Core routes it to the hit component, then along its ancestors, and delivers `Exited` before the next `Entered`:

```csharp
if (link?.GetHelper() is IInputEvents events)
{
    events.Input.PointerEntered += (_, _) => IsHighlighted = true;
    events.Input.PointerExited  += (_, _) => IsHighlighted = false;
}
```

There is no `ILinkHighlight` in Core, and that is intentional. The library supplies events, hit testing against the curve the view actually painted, and a per-action handle. The host decides what a hover looks like; a handler can set `PreventDefault` to veto one framework action, such as Delete, without disabling it globally.

### Ports that change shape

```csharp
[VeloxProperty]
[SlotSelectors(typeof(PythonPortProvider))]
public partial SlotEnumerator<SlotViewModel> Inputs { get; set; }
```

Changing the selector rebuilds the port set. Links are restored by name when the provider stays the same, and by position when the selector type changes. The same operation is available to the Agent as `SetEnumSlotCollection`.

## AI control (optional)

The **Workflow Agent** turns a tree into a tool surface for any `IChatClient`. Every mutation goes through the same component command the GUI dispatches, so the Agent and the canvas share one edit path and one undo stack.

```csharp
var scope = tree.AsAgentScope()
    .WithAutoDiscovery()
    .WithMaxToolCalls(200)
    .WithAllowNodeExecution(true)                              // node execution is off by default
    .WithSynchronizationContext(SynchronizationContext.Current);

var agent = chatClient.AsAIAgent(
    scope.CreateContextProviders(),
    instructions: scope.ProvideProgressiveContextPrompt());
```

What keeps the surface safe under a real session:

- **Node execution is opt-in.** Until the host calls `WithAllowNodeExecution(true)`, execution tools are absent.
- **Budgets are enforced.** `WithMaxToolCalls`, plus separate read and write caps, bound a session.
- **Interaction is explicit.** Selection and confirmation tools appear only when the host wires a handler.
- **Plans can be read before they run.** `CompileWorkflow` / `CompileNodeResult` expose the plan. `RunCompiledWorkflow` waits for the end; `StartCompiledWorkflow` returns a handle that can be paused, resumed, polled or stopped.
- **MCP, skills and sub-agents are separate subsystems.** Each can also be used on its own; an MCP server joins the tool set on the next turn without rebuilding the agent.

## Transition in code

The same source also ships the rendering layer the editor uses for polish. A transition is a chain you build, then execute:

```csharp
var fade = Transition<Rectangle>.Create()
    .Property(r => r.Opacity, 0d)
    .Effect(new TransitionEffect { Duration = TimeSpan.FromSeconds(0.4), Ease = Eases.Cubic.Out });

fade.Execute(rect);
```

Several transitions can share one timeline: pausing, seeking or re-rating it moves all of them, while each keeps its own progress. The adapters contribute only a frame pacer where the platform has one (Razor deliberately has none); easing, keyframes, sampling and scheduling are Core code shared by all seven.

## The other layers

All of these live in `VeloxDev.Core` and need no canvas.

| Layer                   | What it gives you                                                                                                                                  |
| ----------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------- |
| **MVVM**          | Generated observable properties and async, cancellable commands — no `INotifyPropertyChanged` boilerplate                                       |
| **Transition**    | The animation engine above: timelines, easing, keyframes, scheduling                                                                               |
| **Theme**         | Runtime theme switching that animates between states instead of snapping                                                                           |
| **AOP**           | Generated aspect interfaces with runtime proxies — intercept a member without touching the class that declares it                                 |
| **Tickable**      | A frame-driven lifecycle loop with a fixed-step pump beside the variable one                                                                       |
| **Serialization** | A closed-world archiver with generated readers and writers — no reflection, and an unseen type fails loudly instead of serializing an empty shell |

## Repository map

| Path                                                                    | What it holds                                                            |
| ----------------------------------------------------------------------- | ------------------------------------------------------------------------ |
| [`Src/Core/VeloxDev.Core`](Src/Core/VeloxDev.Core)                     | The model, canvas mechanics, compiler, runtime and the supporting layers |
| [`Src/Core/VeloxDev.Core.Extension`](Src/Core/VeloxDev.Core.Extension) | The Agent, MCP, skills and sub-agents — additive to Core                |
| [`Src/Adapters/`](Src/Adapters/)                                       | The seven platform adapters                                              |
| [`Src/Templates/`](Src/Templates/)                                     | The seven `dotnet new` item-template packs                             |
| [`Src/Generators/`](Src/Generators/)                                   | The Roslyn generator                                                     |
| [`Examples/`](Examples/)                                               | A demo per feature per GUI, including trimmed-publish demos              |
| [`Docs/`](Docs/)                                                       | The source of the documentation site                                     |

## License

Released under the [MIT License](LICENSE.txt). © 2025 Axvser
