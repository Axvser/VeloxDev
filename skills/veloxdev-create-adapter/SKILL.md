---
name: veloxdev-create-adapter
description: Build a GUI adapter for VeloxDev from VeloxDev.Core — the interface surface for the workflow surface, slot layout, input routing, link hit testing, minimap and grid, transition scheduler and theme switch, the shared Core pipelines a new adapter plugs into instead of forking, the package/TFM layout and wiring, and the pitfalls that produce silent misalignment. Use when deriving a custom adapter for a GUI the seven official adapters do not cover, or when porting or vetting a workflow surface on another host.
---

## Responsibility

An adapter is the **platform-mechanism layer**. Write one when the GUI is not covered by the seven official adapters and you want the workflow canvas on screen there.

| Layer | Owns |
| --- | --- |
| **Core** (`VeloxDev.Core`) | Platform-free state, algorithms and the input surface: `CanvasLayout`, link hit-testing (`LinkCurve`, `LinkHitTestEx`), the input route (`WorkflowInput`), slot-layout orchestration, surface scroll/restore arithmetic, the minimap state machine, transition sampling and scheduling. |
| **Adapter** | What only the platform can do: translate pointer/keys/wheel into the standard route, resolve the target under the pointer, read and write scroll state, measure port controls, publish the drawn curve, draw the minimap, provide the dispatcher/interpreter/frame pacer and the platform value samplers. |
| **Item template** | Passive visuals: draw the model, publish the curve, declare the named parts. No interaction styling. |
| **Demo** | Effects and policy: hover, selection, glow, link menu entries, HUD. |

⚙ **An adapter ships attached behaviors, attachments and platform services — not workflow view controls.** There is no adapter-provided `Surface`, `NodeView`, `SlotView`, `LinkLayer`, `GridDecorator` or `Minimap` to inherit. Templates and demos compose the platform's basic elements (`Grid`, `Border`, `ScrollViewer`, `Canvas`, `UserControl`, …) plus the adapter's attached properties. The reference shape is WPF's `AttachedBehaviors/` + `ViewHosting/`.

⚙ **The seven official adapters are reference implementations.** Read the one whose host most resembles yours before writing code. For each pipeline, implement the narrow platform interfaces below; the arithmetic and state machines stay in Core.

## The pipelines you are plugging into

A new adapter does not re-derive the arithmetic. For each pipeline, implement the narrow platform interfaces and let Core hold the state machine.

| Pipeline | Core entry point | Adapter supplies |
| --- | --- | --- |
| Input → route → default action | `WorkflowInput.For(tree).Route(args)` | event translation, target resolution, the default action (pan, scroll, focus) |
| Measure → slot anchor → layout | `SlotLayoutPipelineCore` | `ISlotPartLocator` + `ISlotMeasurementHost` |
| Surface metrics → scroll → viewport restore | `SurfaceMetrics.Create`, `SurfaceMetricsPush`, `SurfaceScrollerCore`, `SurfaceViewportRestoreCore` | `ISurfaceMetricsSource`, `ISurfaceScrollHost`, `ISurfaceViewportRestoreHost` |
| Link publish → hit test | `LinkCurve`, `LinkHitTestEx` | the view calls `link.PublishCurve(curve, visual)` when it draws |
| Minimap mapping → draw → drag | `MinimapPipelineCore<…>` | `IMinimapRenderer`, `IMinimapViewportHost`, `IMinimapInputHost` |
| Transition prepare → run → held | `InterpolatorCore.Prepare`, `TransitionSchedulerCore<…>.Execute` | `UIThreadInspector`, interpreter, frame pacer, samplers, `CreateScheduler` |
| Theme switch | `ThemeManager` | `ThemeManager.SetPlatformInterpolator(...)`, `IThemeValueConverter`s, `CreateScheduler` |

Full member lists are in [references/practices/interfaces.md](references/practices/interfaces.md); the sequence and invariants are in [references/practices/pipelines.md](references/practices/pipelines.md).

## Read next

| You are about to… | Read |
| --- | --- |
| Learn every interface the adapter implements | [references/practices/interfaces.md](references/practices/interfaces.md) |
| Wire the pipelines in the right order, or debug where a value is lost | [references/practices/pipelines.md](references/practices/pipelines.md) |
| Lay out the packages, reference Core, register the adapter and validate it | [references/practices/build-and-package.md](references/practices/build-and-package.md) |
| Diagnose a silently misaligned surface | [references/pitfalls/adapter.md](references/pitfalls/adapter.md) |
| Answer the seven host decisions and follow the platform-specific build order | [../veloxdev-create-workflow/references/practices/new-adapter.md](../veloxdev-create-workflow/references/practices/new-adapter.md) |
| Write or customize the seven view roles | [../veloxdev-create-workflow/references/practices/view-layer.md](../veloxdev-create-workflow/references/practices/view-layer.md), then `practices/gui/<your-gui>.md` |
| Route pointer and key input | [../veloxdev-create-workflow/references/practices/input.md](../veloxdev-create-workflow/references/practices/input.md) |
| Get zoom, pan and overscroll right | [../veloxdev-create-workflow/references/practices/canvas-math.md](../veloxdev-create-workflow/references/practices/canvas-math.md) |
| Generate or customize the item-template pack | [../veloxdev-create-workflow/references/practices/templates.md](../veloxdev-create-workflow/references/practices/templates.md) |
| Build the transition half (samplers, priority, frame pacing) | [../veloxdev-create-animation/references/practices/adapter.md](../veloxdev-create-animation/references/practices/adapter.md) |
| Ship theme value converters and the platform scheduler seam | [../veloxdev-switch-themes/references/practices/converters-and-platforms.md](../veloxdev-switch-themes/references/practices/converters-and-platforms.md) |
| Put a periodic concern on a frame loop | [../veloxdev-tick-loop/SKILL.md](../veloxdev-tick-loop/SKILL.md) |

## Rules that decide whether the adapter works

⚙ **`Viewport` is written only by the adapter, and in canvas-local coordinates.** `local = scroll − CanvasLayout.ActualOffset`. Write it through `SurfaceMetricsPush.PushViewport`; never write a raw scroll offset there.

⚙ **Persist and restore the viewport in the right order.** Call `SurfaceViewportRestoreCore.Capture(tree)` **before** the first `PushViewport` (which writes `Layout.ViewportOffset` from the control's current position), then `Schedule(host, afterRestore: PushViewportOnceMore)`. Defer the restore until the platform has laid out the newly attached tree, or the extent is still zero and the position is clamped away.

⚙ **A slot anchor begins as `(NaN, NaN, 0)`.** Only a named, measured port gets an anchor. Pick the `SlotCoordinateSpace` that matches what your measurement actually returns; the wrong choice offsets every link by a constant with no error.

⚙ **Never hand a renderer an unbounded absolute coordinate or a whole-world canvas size.** Platforms with a size cap need an overlay, an offset frame, or self-bounding geometry. This is the difference between links that survive deep zoom and links that vanish at 40%.

⚙ **The wheel belongs to the adapter.** The platform scroll container must not scroll the workflow canvas. Intercept the non-Ctrl wheel, route it, and apply the default scroll through `WorkflowInput.Scroller` unless a subscriber set `PreventDefault`.

⚙ **Register `Scroller` and `Navigator` while attached; clear only your own on detach.** Both are optional capabilities read by the host. Clearing another surface's registration breaks a second surface on the same tree.

⚙ **The priority type appears in two places and they must agree:** the `TPriorityCore` argument the adapter's `Transition<T>` / scheduler closure is built with, and the type `CreateScheduler` tests the effect against. A disagreement makes an animated theme switch fall back to instant, silently.

⚙ **Call `ThemeManager.SetPlatformInterpolator(new Interpolator())` once at startup.** Without it Core never reaches `CreateScheduler`, and every theme switch is instant even when `Transition` was requested.

⚙ **The frame pacer must run on the same thread the write path resolves.** Derive it from `affinity.ThreadFor(target)`; a pacer that disagrees turns every frame into a dispatch. `PostCore` must return `false` when the platform could not accept the work.

⚙ **Ship behaviors, not view base classes.** Templates stay on platform primitives; a generated view should need little more than `InitializeComponent()`.

## Build order

1. `UIThreadInspector` — the only class that needs real platform knowledge; then samplers, `Interpolator` and `CreateScheduler`.
2. Surface metrics, scroll host and viewport restore.
3. Surface behavior: named-part resolution, panning, wheel, input routing, `Scroller`/`Navigator`.
4. Slot layout: `ISlotPartLocator` over the visual tree plus `ISlotMeasurementHost` over the UI thread.
5. Node drag and slot connection behaviors.
6. Link views: draw, `IsRenderReady()`, `PublishCurve`.
7. Grid decorator and minimap: Core interfaces, then `MinimapPipelineCore` layers.
8. Theme value converters (and the startup `SetPlatformInterpolator`).
9. Wire the surface and the view pool in the tree view; generate or hand-write the seven roles.
10. Validate against `Examples/Workflow/<GUI> Trimmed/Demo` and the headless/acceptance harness.

[references/practices/build-and-package.md](references/practices/build-and-package.md) carries the package layout, TFM choices, Debug/Release references and the validation commands.

## Checking your work

⚙ Build a runnable demo before packaging any templates. The minimum proof: host the surface, realize all seven roles, drag a node, connect two slots, pan and zoom with the point under the pointer staying put, and round-trip a graph through `Serialize()` / `Deserialize()`.

⚙ **Test at deep zoom, not at 100%.** Vanished links, detached ports and drifting cameras are invisible at 100% and obvious at 40%.

⚙ The headless suites pin the surface contract (named parts, viewport writes, restore, `VisibleItems`, `ViewPool`), and the acceptance suite drives the real demos. Run them with `Src/Test/Headless/run-headless-tests.ps1` and `Src/Test/AutoTest/run-autotest.ps1`; see [references/practices/build-and-package.md](references/practices/build-and-package.md#validation).

## When something goes wrong

| Symptom | Read |
| --- | --- |
| Every link is offset by a constant, or ports sit away from their visuals | [references/pitfalls/adapter.md](references/pitfalls/adapter.md) — coordinate space |
| The theme switch is instant, or a scheduler animates at two frames and hangs | [references/pitfalls/adapter.md](references/pitfalls/adapter.md) — priority, pacer, thread affinity |
| The wheel scrolls the page instead of the canvas, or scrolls twice | [references/pitfalls/adapter.md](references/pitfalls/adapter.md) — wheel ownership |
| A saved graph opens at the origin, or the camera drifts near an edge | [references/pitfalls/adapter.md](references/pitfalls/adapter.md) — viewport persist/restore |
| Links vanish or detach at deep zoom | [references/pitfalls/adapter.md](references/pitfalls/adapter.md) — renderer bounds |
| A connection drag never highlights a candidate, or Delete does nothing | [references/pitfalls/adapter.md](references/pitfalls/adapter.md) — preview, focus |
| The minimap throws, freezes, or updates on the wrong thread | [references/pitfalls/adapter.md](references/pitfalls/adapter.md) — cross-thread invalidation |
