---
name: veloxdev-create-workflow
description: Build a workflow editor in your own project with VeloxDev — reference the right package, author Tree/Node/Slot/Link ViewModels with [WorkflowBuilder], make nodes compute, compile and run a graph forwards or backwards, arrange a graph automatically, scaffold and customize the canvas views with the dotnet new template packs, and extend the library at its intended seams (custom link type, connection rules, variable port sets, a workflow view adapter for another GUI; a custom sampler is covered by veloxdev-create-animation) without patching generated code. Use when building or modifying a workflow editor, graph, node canvas, or its views.
---

## Responsibility

Write **application code** against VeloxDev — from the NuGet packages, or against a checkout of this repository. Two things have to be right in whatever you produce, and they are what this skill is for:

**1. The API is used the way it is meant to be used.** VeloxDev is generated code plus a component model; a few rules decide whether your code works at all, and they are listed below.

**2. Customization happens at the intended seam.** The library is designed to be extended, and every extension point has one right place. The generated members, the components the generator owns, and the adapter's behaviours each have a designated extension path.

Everything else about this repository (how the adapters are built, how the demos are maintained, how a release is cut) is out of scope here.

## Default: the whole item-template suite, elastic nodes

⚙ **Start every canvas from the adapter's item-template pack and generate all seven roles: tree, node, slot, link, selector, decorator and minimap.** The decorator draws the floating top/left ruler bands; the minimap is the canvas's thumbnail and navigation. The commands are in [references/practices/templates.md](references/practices/templates.md); all seven files go into one namespace.

⚙ **The generated node view is elastic.** It is a `<Viewbox Stretch="Uniform">` over a grid pinned to the node's design size (260 × 180), or the platform's layout-only equivalent in MAUI/WinForms and the CSS transform in Razor. The outer view is sized to the node's collapsed `Size`; the inner design scales by `1/Scale`. Put added content on the design-size grid and let the container handle zoom. Each [gui reference](references/practices/gui/) carries the exact code for its framework.

⚙ **`InputSlot` and `OutputSlots` are the item template's two default implementations, not a contract.** On the attached-property packs the template registers them with the layout behaviour so their controls are measured; Razor supplies the same pair through render fragments. Remove them, rename them, or add your own slot properties, and register each slot's control the same way — see [Ports: the model ↔ view correspondence](references/practices/view-layer.md#ports-the-model--view-correspondence).

## Which packages

| Need | Package |
|---|---|
| Model, canvas math, execution engine, auto-layout | `VeloxDev.Core` |
| The canvas on screen | one adapter: `VeloxDev.WPF` · `VeloxDev.Avalonia` · `VeloxDev.WinUI` · `VeloxDev.MAUI` · `VeloxDev.WinForms` · `VeloxDev.Razor` · `VeloxDev.Jalium` |
| `Serialize()` / `Deserialize()` | `VeloxDev.Core` itself — the entry points are extension methods in `VeloxDev.Serialization` (backed by `VeloxJsonSerializer`); no extra package |
| Driving the graph from an LLM | `VeloxDev.Core.Extension` |
| Saving a compiled plan | `VeloxDev.Core.Extension` (`CompiledGraphEx`) |

An adapter package brings `VeloxDev.Core` in transitively. Versions are deliberately not stated for installation: take the latest published. Rendering the canvas needs an adapter; using only the model and the engine needs neither.

## The model in sixty seconds

- A **Tree** owns `Nodes`, `Links`, `LinksMap` and a `CanvasLayout`. Nodes own **Slots**; a slot is a sender or a receiver; two slots wired together are a **Link**.
- Every component is a `partial` class split in two: a **ViewModel** holding identity, state and geometry, and a **Helper** holding behaviour. `[WorkflowBuilder.Node<MyNodeHelper>]` generates the ViewModel half; you write the helper.
- **A slot channel says how many connections it accepts** — `OneTarget` / `OneSource` / `MultipleBoth` / … Validation happens on the **receiver** side only.
- **A connection drag has a live preview.** `WorkflowInput.For(tree)` carries `ConnectionSender`, `PreviewReceiver`, `PreviewState` and `PreviewCheck`; the adapters feed the candidate slot on every pointer move, and the tree's `VirtualLink` (as `IConnectionPreviewLink`) publishes the stage a link view paints. `StandardCheckConnection(sender, receiver)` is the pure rule set behind it. `PreviewValidating` may tighten the framework's verdict for one drag: a host can turn a green candidate red, and the release-time decision stays unchanged.
- **Every structural mutation is an undoable command** — create, connect, disconnect, delete and `SetSelector` build a `WorkflowActionPair` and go through `Submit`. That is how the undo stack and the GUI stay in step, and how an AI agent sees the same changes a mouse does.
- **Anchor and Size getters return `Scale`-collapsed coordinates.** The fields store world coordinates; the getters return `world ÷ Scale`. This single fact explains most of the geometry traps in [references/pitfalls/geometry-and-viewport.md](references/pitfalls/geometry-and-viewport.md).
- **A compiled fan-out runs concurrently.** The engine drives each branch with `Task.WhenAll`; each branch gets its own `BranchRuntimeContext` facade. Branches interleave at await points on the host's synchronization context, and `RuntimeContext.MaxParallelBranches` caps a group. [References/practices/execution.md](references/practices/execution.md#fan-out) carries the full model.

## Where to read next

| You are about to… | Read |
|---|---|
| Write a node, slot, link or tree ViewModel | [references/practices/model.md](references/practices/model.md) |
| Make a node actually compute something | [references/practices/execution.md](references/practices/execution.md) |
| Run a chain, or ask "what would this node output?" | [references/practices/execution.md](references/practices/execution.md) |
| Save and restore a graph, or save a compiled plan | [references/practices/model.md](references/practices/model.md#serialization) |
| Arrange nodes automatically | [references/practices/layout.md](references/practices/layout.md) |
| Scaffold the whole canvas view layer (all seven roles) with `dotnet new` | [references/practices/templates.md](references/practices/templates.md) |
| Customize or debug those views | [references/practices/view-layer.md](references/practices/view-layer.md), then `practices/gui/<your-gui>.md` |
| React to zoom, panning, or links that do not line up | [references/practices/canvas-math.md](references/practices/canvas-math.md) |
| Paint a red/green connection preview while a drag is in progress | [references/practices/model.md](references/practices/model.md#the-connection-preview) |
| Take over the wheel, or scroll the surface from your own handler | [references/practices/view-layer.md](references/practices/view-layer.md#wheel-and-scrolling) |
| Hear pointer/key input, or resolve the component under the pointer | [references/practices/input.md](references/practices/input.md), [references/practices/view-layer.md](references/practices/view-layer.md#how-the-surface-picks-a-link) |
| Support a GUI that has no adapter | [references/practices/new-adapter.md](references/practices/new-adapter.md) |
| Drive the graph from an LLM | [../veloxdev-drive-workflow-with-ai/SKILL.md](../veloxdev-drive-workflow-with-ai/SKILL.md) |

Open your GUI's reference alongside whichever of the above you are reading — it carries that framework's attached properties, extension points and template defaults.

| GUI | Reference |
|---|---|
| WPF | [references/practices/gui/wpf.md](references/practices/gui/wpf.md) |
| Avalonia | [references/practices/gui/avalonia.md](references/practices/gui/avalonia.md) |
| WinUI 3 / Windows App SDK | [references/practices/gui/winui.md](references/practices/gui/winui.md) |
| .NET MAUI | [references/practices/gui/maui.md](references/practices/gui/maui.md) |
| Windows Forms | [references/practices/gui/winforms.md](references/practices/gui/winforms.md) |
| Blazor / Razor | [references/practices/gui/razor.md](references/practices/gui/razor.md) |
| Jalium | [references/practices/gui/jalium.md](references/practices/gui/jalium.md) |

## The rules that decide whether your code works

⚙ **Mark every component class `partial`.** Every generator — `[VeloxProperty]`, `[VeloxCommand]`, `[WorkflowBuilder.*]`, `[Tickable]` — matches on the `partial` modifier. `[VeloxProperty]` members also have their own diagnostics (`VELOX_MVVM_PROP*`); the other generators report nothing at the class level, so a missing `partial` produces no file and no warning.

⚙ **Call `InitializeWorkflow()` from your own constructor.** The generator emits the method but no constructor.

⚙ **Write geometry through the property setter.** `node.Anchor = new Anchor(x, y, layer)` and `node.Size = new Size(w, h)` notify the spatial index and the views. The getters return collapsed copies at any zoom other than 1.

⚙ **Change a slot's channel with `slot.SetChannelCommand.Execute(channel)`.** The generated slot property owns its slot instance; the setter installs and uninstalls the slot's lifecycle.

⚙ **A slot's `Anchor` begins as `(NaN, NaN, 0)`.** Core never computes it. A GUI measures the named port control and writes the anchor, and a link whose endpoints are unmeasured draws nothing until that measurement lands. The render-ready gate is working when that happens. See [Ports: the model ↔ view correspondence](references/practices/view-layer.md#ports-the-model--view-correspondence).

⚙ **Send structural mutations through `Submit`.** `tree.SubmitCommand.Execute(pair)` or `tree.GetHelper().Submit(pair)` is what records an undo entry and notifies the tree's observers.

⚙ **Treat `PreviewValidating` as advisory.** It runs for candidates the framework already accepts; `e.Reject(message)` reddens that candidate for this drag. The release-time `IWorkflowTreeEvents.Connecting` veto and `ValidateConnection` are the final decision.

⚙ **Turn virtualization on with `TreeHelper(cellSize)`.** The parameterless `TreeHelper` keeps virtualization off; the `cellSize` overload enables the spatial map and starts the `"TreeHelper"` tick loop.

⚙ **A compiled fan-out is concurrent and branch-isolated.** Each branch receives its own `BranchRuntimeContext`: `Data`, redirect state and report level are branch-private; identity, logs, the blackboard and the output table are shared. Branches interleave at await points; the engine does not use thread parallelism. Use `RuntimeContext.MaxParallelBranches` to cap a group.

## Customizing on top of VeloxDev

VeloxDev is meant to be extended, and each of these is the seam the library expects you to use.

| You want to… | The seam |
|---|---|
| Give a node behaviour | your own `NodeHelper<T>` overriding `ReceiveAsync` / `AccessAsync` |
| Use your own link type | override `TreeHelper.CreateLink` |
| Constrain what may connect | override `TreeHelper.ValidateConnection(sender, receiver)` |
| Constrain a candidate for one drag only | subscribe `WorkflowInput.PreviewValidating` and call `e.Reject(message)` |
| Give a node a variable number of ports | `SlotEnumerator<TSlot>` plus an `ISlotProvider` named by `[SlotSelectors]` |
| Change how a node looks | the GUI's node view, generated by the item templates and then edited |
| Add a property that changes with the theme | `[ThemeConfig]` — see the theme skill |
| Animate a value VeloxDev cannot interpolate | `ISampler` for a type, or `ISampleable` on your own type — see the animation skill |
| Intercept a member without editing it | `[AspectOriented]` plus `Aop()` — see the aspects skill |
| Put a periodic view concern on a timer | `[Tickable]` on your helper, the way `TreeHelper` does |
| Run on a GUI that has no adapter | write one — [references/practices/new-adapter.md](references/practices/new-adapter.md) |
| Let an LLM operate the graph | a `WorkflowAgentScope` on the tree |
| Arrange a graph | `node.Arrange(...)` against the tree's `IWorkflowLayoutSolver` — [references/practices/layout.md](references/practices/layout.md) |

## Checking your work

The behaviour that matters is visual and interactive, so check it in a running app:

⚙ Drag a node, drag a slot onto another slot, pan and zoom until the pointer stays under what you grabbed, then save and reload the graph. Those cover most of the model, the canvas and the serialization at once. During the slot drag, the candidate under the pointer shows the preview state and the rubber band carries its `ValidTarget` / `InvalidTarget` stage; a candidate your `PreviewValidating` rejects turns red without cancelling the drag.

⚙ **Test at a deep zoom, not at 100%.** Links that vanish, links that detach from their port and a camera that drifts are obvious at 40%.

⚙ **Check the port correspondence before calling a view done.** Every slot the node model can connect is measured through a named control on that node's view. The item template fills the standard pair; a node type with more slots establishes the same correspondence for each. An unmeasured slot is silent at 100% and appears as a link attached to the wrong place.

⚙ When something does not work, check in this order: `partial` on the class → `InitializeWorkflow()` called → the slot's channel permits the connection → the slot has been measured (is the anchor still `NaN`?) → the view is bound to the right property.

⚙ If you are working against a checkout rather than NuGet, `Examples/Workflow/Common/Lib` is a working node library to compare against — a controller, a dynamic router, a python worker, a timer and an agent message node — and `Examples/Workflow/<GUI> Trimmed/Demo` is a complete, minimal editor on your GUI. (Blazor's folder is `Examples/Workflow/Blazor Trimmed/Demo` — named after the platform, not the adapter — and Avalonia's demo is nested one level deeper: `Examples/Workflow/Avalonia Trimmed/Demo/Demo`.) `Src/Core/VeloxDev.Core/WorkflowSystem/` holds the model and `Src/Adapters/VeloxDev.<GUI>/` the view layer.

⚙ **The `"Trimmed"` in those folder names means *minimal demo*.** It is a demo-size choice, not a publish setting. `VeloxDev.Core`'s `net8.0` target sets `IsAotCompatible=true` and its agent surface is reflection-free.

⚙ **Undo coverage is structural.** Node and slot create/delete, connect/disconnect, selector changes and their cascades are undoable. Position, size, direct property patches and slot-channel writes are runtime state, not history entries.

⚙ **The repository Wiki (`Docs/VeloxDev.Docs`) has a per-subsystem QuickStart and API walkthrough.** It is the long-form companion to these references.

## When something goes wrong

Read the symptom first, then the pitfall file. Each file records a failure the repository has actually hit, the rule it violates, and the fix.

| Symptom | Read |
|---|---|
| Links are invisible, or attach to the wrong point | [references/pitfalls/geometry-and-viewport.md](references/pitfalls/geometry-and-viewport.md) |
| A node drag or a zoom writes to the wrong place, or the camera drifts | [references/pitfalls/geometry-and-viewport.md](references/pitfalls/geometry-and-viewport.md) |
| A connection succeeds but no link appears, or the channel rejects unexpectedly | [references/pitfalls/model-and-connections.md](references/pitfalls/model-and-connections.md) |
| Undo misses a change, or the AI and the GUI disagree about the graph | [references/pitfalls/model-and-connections.md](references/pitfalls/model-and-connections.md) |
| A node produces nothing during a compiled run | [references/pitfalls/execution.md](references/pitfalls/execution.md) |
| A fan-out branch sees stale data, or branches unexpectedly share state | [references/pitfalls/execution.md](references/pitfalls/execution.md) |
| A reroute or reverse query never reaches its target | [references/pitfalls/execution.md](references/pitfalls/execution.md) |
| The canvas does not pan, or a link swallows every gesture | [references/pitfalls/view-and-input.md](references/pitfalls/view-and-input.md) |
| The wheel scrolls the wrong thing, or a key chord does not reach the tree | [references/pitfalls/view-and-input.md](references/pitfalls/view-and-input.md) |
| A generated view does not build, or its siblings cannot be found | [references/pitfalls/templates.md](references/pitfalls/templates.md) |
