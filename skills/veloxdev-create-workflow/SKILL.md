---
name: veloxdev-create-workflow
description: Build a workflow editor in your own project with VeloxDev — reference the right package, author Tree/Node/Slot/Link ViewModels with [WorkflowBuilder], make nodes compute, compile and run a graph forwards or backwards, scaffold and customize the canvas views with the dotnet new template packs, and extend the library at its intended seams (custom link type, connection rules, variable port sets, custom sampler, adapter for another GUI) without patching generated code
---

## Responsibility

Write **application code** against VeloxDev — from the NuGet packages, or against a checkout of this repository. Two things have to be right in whatever you produce, and they are what this skill is for:

**1. The API is used the way it is meant to be used.** VeloxDev is generated code plus a component model; a few rules decide whether your code works at all, and they are listed below.

**2. Customization happens at the intended seam.** The library is designed to be extended, and every extension point has exactly one right place. Reaching past it — editing generated members, replacing a component the generator owns, patching an adapter — is the usual cause of code that compiles and then misbehaves.

Everything else about this repository (how the adapters are built, how the demos are maintained, how a release is cut) is out of scope here.

## Which packages

| Need | Package |
|---|---|
| Model, canvas math, execution engine | `VeloxDev.Core` |
| The canvas on screen | one adapter: `VeloxDev.WPF` · `VeloxDev.Avalonia` · `VeloxDev.WinUI` · `VeloxDev.MAUI` · `VeloxDev.WinForms` · `VeloxDev.Razor` · `VeloxDev.Jalium` |
| `Serialize()` / `Deserialize()` | `VeloxDev.Core.Extension` — **Newtonsoft-based and not in Core**, so a project that saves graphs needs it |
| Driving the graph from an LLM | `VeloxDev.Core.Extension` |

An adapter package brings `VeloxDev.Core` in transitively. Versions are deliberately not stated: take the latest published. Rendering the canvas needs an adapter; using only the model and the engine needs neither.

## The model in sixty seconds

- A **Tree** owns `Nodes`, `Links`, `LinksMap` and a `CanvasLayout`. Nodes own **Slots**; a slot is a sender or a receiver; two slots wired together are a **Link**.
- Every component is a `partial` class split in two: a **ViewModel** holding identity, state and geometry, and a **Helper** holding behaviour. `[WorkflowBuilder.Node<MyNodeHelper>]` generates the ViewModel half; you write the helper.
- **A slot channel says how many connections it accepts** — `OneTarget` / `OneSource` / `MultipleBoth` / … Validation happens on the **receiver** side only.
- **Every structural mutation is an undoable command** — create, connect, delete, `SetSelector` all build a `WorkflowActionPair` and go through `Submit`. It is how the undo stack and the GUI stay in step, and how an AI agent sees the same changes a mouse does.
- **Angles are in `Scale`-collapsed space.** Node `Anchor`/`Size` *store* world coordinates but their *getters* return canvas-local ones (`world ÷ Scale`). That single fact explains most of the traps below.

## Where to read next

| You are about to… | Read |
|---|---|
| Write a node, slot, link or tree ViewModel | [references/model.md](references/model.md) |
| Make a node actually compute something | [references/execution.md](references/execution.md) |
| Run a chain, or ask "what would this node output?" | [references/execution.md](references/execution.md) |
| Save and restore a graph | [references/model.md](references/model.md) |
| Scaffold the canvas views with `dotnet new` | [references/templates.md](references/templates.md) |
| Customize or debug those views | [references/view-layer.md](references/view-layer.md), then `gui/<your-gui>.md` |
| React to zoom, panning, or links that do not line up | [references/canvas-math.md](references/canvas-math.md) |
| Support a GUI that has no adapter | [references/new-adapter.md](references/new-adapter.md) |
| Drive the graph from an LLM | [../veloxdev-drive-workflow-with-ai/SKILL.md](../veloxdev-drive-workflow-with-ai/SKILL.md) |

Open your GUI's reference alongside whichever of the above you are reading — it carries that framework's specifics, and the difference between the seven is where most surprises live.

| GUI | Reference |
|---|---|
| WPF | [references/gui/wpf.md](references/gui/wpf.md) |
| Avalonia | [references/gui/avalonia.md](references/gui/avalonia.md) |
| WinUI 3 / Windows App SDK | [references/gui/winui.md](references/gui/winui.md) |
| .NET MAUI | [references/gui/maui.md](references/gui/maui.md) |
| Windows Forms | [references/gui/winforms.md](references/gui/winforms.md) |
| Blazor / Razor | [references/gui/razor.md](references/gui/razor.md) |
| Jalium | [references/gui/jalium.md](references/gui/jalium.md) |

## The rules that decide whether your code works

⚙ **`partial` on every component class, or nothing happens.** Every generator — `[VeloxProperty]`, `[VeloxCommand]`, `[WorkflowBuilder.*]`, `[MonoBehaviour]` — matches on the `partial` modifier, and there is not one diagnostic in the whole generator project. A missing `partial` produces no file, no warning, and a puzzling error ten minutes later at the use site.

⚙ **Call `InitializeWorkflow()` from your own constructor.** The generator does not emit one.

⚙ **Write geometry through the property setter; never mutate what the getter returned.** `Anchor`/`Size` getters collapse by `Scale`, so at any zoom other than 1 they hand you a discarded copy — and at exactly 1 they hand you the live object but change it without notifying the spatial index or the views. Both halves are wrong. `node.Anchor = new Anchor(x, y, layer)` is right; `node.Anchor.Horizontal = x` and `node.Size.Width = w` are not.

⚙ **Do not replace a generator-created slot with a new instance.** The generated property's setter deletes the old slot, so you get ghost undo entries instead of a slot. Change the channel with `slot.SetChannelCommand.Execute(channel)`.

⚙ **A slot's `Anchor` is `(NaN, NaN, 0)` until a GUI measures it.** Core never computes it, and a link whose endpoints have not been measured draws nothing — that is the render-ready gate working, not a bug. If your links are invisible, look here first.

⚙ **Structural mutations go through `Submit`.** A mutation written as a direct collection edit is invisible to undo and to anything watching the tree.

⚙ **Virtualization is off until you ask for it.** The parameterless `TreeHelper` disables it; only `TreeHelper(cellSize)` turns it on. A graph that always realizes every node is usually this, not a rendering problem.

## Customizing on top of VeloxDev

VeloxDev is meant to be extended, and each of these is the seam the library expects you to use. The right-hand column is the shortcut to avoid — every one of them compiles.

| You want to… | The seam | Do not |
|---|---|---|
| Give a node behaviour | your own `NodeHelper<T>` overriding `ReceiveAsync` / `AccessAsync` | put logic in the generated node class |
| Use your own link type | override `TreeHelper.CreateLink` | construct a link and add it to `Links` yourself |
| Constrain what may connect | override `TreeHelper.ValidateConnection(sender, receiver)` — the only connection predicate | remove links after the fact |
| Give a node a variable number of ports | `SlotEnumerator<TSlot>` + an `ISlotProvider` named by `[SlotSelectors]` | adding and removing slots by hand |
| Change how a node looks | the GUI's node view, generated by the item templates and then edited | edit the adapter |
| Add a property that changes with the theme | `[ThemeConfig]` — see the theme skill | branch on the theme in the view |
| Animate a value VeloxDev cannot interpolate | `ISampler` for a type, or `ISampleable` on your own type — see the animation skill | rebuild the value per frame by hand |
| Intercept a member without editing it | `[AspectOriented]` + `Aop()` — see the aspects skill | wrap the class in a decorator |
| Put a periodic view concern on a timer | `[MonoBehaviour]` on your helper, the way `TreeHelper` does | a `DispatcherTimer` beside the graph |
| Run on a GUI that has no adapter | write one — [references/new-adapter.md](references/new-adapter.md) | fork Core, or reimplement the canvas |
| Let an LLM operate the graph | a `WorkflowAgentScope` on the tree | call your own commands from a tool wrapper |

## Checking your work

The behaviour that matters is visual and interactive, so check it in a running app rather than by reading code:

⚙ Drag a node, drag a slot onto another slot, pan and zoom until the pointer stays under what you grabbed, then save and reload the graph. Those cover most of the model, the canvas and the serialization at once.

⚙ **Test at a deep zoom, not at 100%.** Links that vanish, links that detach from their port and a camera that drifts are all invisible at 100% and obvious at 40%.

⚙ When something does not work, the order to suspect is: `partial` on the class → `InitializeWorkflow()` called → the slot's channel permits the connection → the slot has been measured (is the anchor still `NaN`?) → the view is bound to the right property.

⚙ If you are working against a checkout rather than NuGet, `Examples/Workflow/Common/Lib` is a working node library to compare against — a controller, a dynamic router, a python worker, a timer and an agent message node — and `Examples/Workflow/<GUI> Trimmed/Demo` is a complete, minimal editor on your GUI. `Src/Core/VeloxDev.Core/WorkflowSystem/` holds the model and `Src/Adapters/VeloxDev.<GUI>/` the view layer; both are small enough to read when a reference here does not answer the question.

⚙ **The `"Trimmed"` in those folder names means *minimal demo*, not trim configuration.** The library is **not** AOT- or trim-safe — `VeloxDev.Core` declares `IsTrimmable=false`, and the animation path compiles expression trees at runtime. Nothing warns you at build time; it surfaces at publish.

⚙ **Undo coverage is structural, and the gaps are by design.** Node and slot create/delete, connect/disconnect, selector changes and their cascades are undoable; **position, size and direct property patches are not** — a drag leaves no history entry. Do not build a feature that assumes a drag can be undone.

⚙ **Fan-out is sequential.** A compiled run shares one runtime context that is intentionally not thread-safe, so parallel branches do not execute in parallel.

⚙ The repository Wiki (`Docs/VeloxDev.Docs`) has a per-subsystem QuickStart and API walkthrough and is the long-form companion to these references.
