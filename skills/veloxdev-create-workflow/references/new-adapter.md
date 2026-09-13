# Supporting a GUI that has no adapter

The model, the canvas math and the execution engine are in `VeloxDev.Core` with **zero UI dependencies**. That is the design decision that makes this a contained job: you are writing a view layer, not porting a framework.

The seven shipped adapters are your reference implementations. Read the one whose host most resembles yours before you start — [view-layer.md](view-layer.md) is the shared contract, `gui/*.md` the divergent part.

## Is a full adapter what you need?

| Situation | Do this instead |
|---|---|
| The GUI is not one of the seven but is XAML-like | A full adapter. Expect about six classes and a slot-layout behaviour. |
| You only need the model and the engine, headless | Reference `VeloxDev.Core` alone — no adapter is involved at all. |
| You want the canvas inside an existing control of your own | Still a full adapter. The surface is not separable from the coordinate host. |
| You could switch to a GUI that already has an adapter | Do that. An adapter is a real cost and the seven cover most .NET GUIs. |

## The seven decisions

Answer these before writing code. Each one has a right answer determined by the host, and each one, answered wrongly, produces a silent misalignment rather than a crash.

| Decision | Question | What it controls |
|---|---|---|
| 1. Coordinate host | does the host own the scroll, or do you? | who writes `Viewport` and `ActualOffset` |
| 2. Scale container | does the framework have a `Viewbox`? | how node content scales |
| 3. Slot anchor function | what frame does a measured centre arrive in? | which of the three `SlotAnchor*` forms |
| 4. Link strategy | is there a surface-size limit? | retained views vs a viewport-sized overlay |
| 5. Zoom hook | which event does the framework actually give you? | the gesture plumbing |
| 6. Priority | does the framework have a dispatcher-priority type? | the scheduler's seventh type argument |
| 7. Change model | push (bindings) or pull (repaint)? | whether callers must `Refresh` |

### 1. Coordinate host

If the host gives you a scroll viewer and a canvas, take them: the surface behaviour resolves them by name, writes `Viewport` from the scroll offsets, and everything downstream is automatic. This is what WPF, Avalonia, WinUI and Blazor do.

If it does not — a self-drawn surface, or a framework with no scroll viewer — **you** own the scroll: compute `scroll − ActualOffset`, write `Viewport` yourself, and clamp with `ClampScrollOffset`. WinForms is the worked example, and the only adapter that also synthesizes its scroll from `ViewportOffset`.

⚙ Whichever you choose, `Viewport` is written **only by the adapter**, and always in canvas-local coordinates.

### 2. Scale container

`Viewbox` if the framework has one — the three XAML adapters and Jalium all use it, with the node's content pinned to its design size so the factor is exactly `1/Scale`.

If it does not, you have two workable substitutes:

- **measure and resize in code** (MAUI) — scale the row heights and font sizes rather than transforming. Use this when the framework's layout must produce the coordinates the slot behaviour reads; a render transform would break them.
- **a CSS transform** (Blazor) — correct because `getBoundingClientRect()` includes the transform, so measurement still agrees.

⚙ Whichever you pick, the invariant is the same: **the inner content is laid out once at design coordinates and never re-laid-out.** Re-laying it out per zoom reintroduces exactly the measurement drift the three-layer structure exists to prevent.

### 3. Slot anchor function

The single highest-risk choice in the whole adapter. Ask: **what frame is the measured centre in?**

| Your measurement gives you… | Use | Adapters |
|---|---|---|
| a screen-space centre, and nothing has applied the canvas pan for you | `SlotAnchorFromVisualCenter` | WPF, Avalonia |
| a centre already inside the canvas-local frame (the host applies the pan itself) | `SlotAnchorFromCanvasLocal` | WinUI, MAUI, WinForms |
| no coordinate host at all; you can only compute from model geometry | `SlotAnchorFromNode` | Jalium |
| a value from outside the process, already canvas-local (e.g. JavaScript) | `SlotAnchorFromCanvasLocal` | Blazor |

⚙ **Get this wrong and every link in the graph is off by a constant** — by `−ActualOffset` in the visual-centre/identity mix-up. There is no exception, no log and no visual clue other than the offset.

⚙ Write the decision down in a comment at the call site, the way the shipped adapters do.

### 4. Link strategy

Ask whether your renderer has a hard coordinate or size limit.

- **No limit** → retained views bound to `Sender.Anchor` / `Receiver.Anchor`, or immediate-mode drawing on a canvas-spanning overlay. WPF, Avalonia, WinForms and Blazor all do the first or the second.
- **A limit** → do not scale the drawing surface with the world:
  - a **viewport-sized overlay** that lives outside the growing canvas (MAUI — Win2D's ~16k texture cap),
  - an **offset frame**: bake the pan into the geometry and position the element at `−ActualOffset` so the geometry always lands in `[0, ActualSize]` (WinUI),
  - or **self-bounding**: keep each element on its own polyline bounding box and bake back in `OnRender` (Jalium — the renderer culls by layout box).

⚙ **The rule behind all three**: never hand a renderer an unbounded absolute coordinate or a whole-world canvas size. This is the single most likely way to end up with links that vanish at deep zoom and work fine at 100%.

⚙ Draw the same four-point golden-stub elbow as everyone else, and start the render with `link.IsRenderReady()` (or an equivalent `IsVisible` + NaN-anchor check).

### 5. Zoom hook

Take whatever the framework actually provides, and expect it to be different from the last one:

| Framework | Hook |
|---|---|
| WPF | `ScrollViewer.PreviewMouseWheel`, gated on `Ctrl` |
| Avalonia | `AddHandler(PointerWheelChangedEvent, …, RoutingStrategies.Tunnel)` |
| WinUI | `ScrollViewer.AddHandler(PointerWheelChangedEvent, h, handledEventsToo: true)` |
| MAUI | `PinchGestureRecognizer` + a platform wheel hook |
| WinForms | `MouseWheel` **plus** `Application.AddMessageFilter` on `WM_MOUSEWHEEL` |
| Blazor | a non-passive JS `wheel` listener |
| Jalium | host-driven `ZoomBy` + a committed-zoom state machine |

⚙ **Wheel-up is zoom-in, and zoom-in divides `Scale` by 1.1.** `Scale` is a collapse factor; writing `delta > 0 ? 1.1 : 1/1.1` inverts the gesture. This was wrong across all seven adapters once and has been fixed everywhere.

⚙ **Clamp `Scale` to `[0.1, 10]`** in the gesture, as every shipped adapter does.

⚙ Then follow the commit chain in [canvas-math.md](canvas-math.md#zoom--the-commit-chain) exactly: pivot, scale, cover, relayout, `PivotCenterScroll`, clamp. Three of the seven adapters have compressed or reordered variants; if your host is a plain scroll viewer, use the canonical order and **clamp the final scroll to the reachable range instead of growing the canvas** — growth is what makes a camera drift near an edge.

### 6. Priority

Use the framework's dispatcher-priority type when it has one, and `NonPriority` when it does not.

| | `TPriorityCore` |
|---|---|
| WPF, Avalonia, Jalium | `DispatcherPriority` |
| WinUI | `DispatcherQueuePriority` |
| MAUI, WinForms, Blazor | `NonPriority` |

⚙ The priority type appears in **two** places and they must agree: the seventh type argument of `Transition<T>`, and the type `CreateScheduler` tests the effect against. Getting the second wrong makes a theme switch fall back to instant switching, silently.

### 7. Change model

Push is better. If the framework has bindings, bind the node's position and size to the collapsed `Anchor`/`Size` and let the binding re-evaluate; the only thing you must remember is re-raising those two properties after a re-virtualization — which Core's `BroadcastVisibleItemLayout()` already does for you.

Pull is survivable but costly: the caller must refresh after every mutation, and you will need a synchronous escape hatch for the cases where a value must be correct in the same frame (see WinForms' `SyncNow`). Choose it only when the framework offers nothing else.

## Writing the adapter

A workable order, bottom-up — each step is verifiable on its own:

1. **`UIThreadInspector`** — `IsUIThread`, `ProtectedInvoke`, `ProtectedGetValue`, `IsAppAlive`. This is the only class needing real platform knowledge; copy the shipped one closest to your host.
2. **Samplers** — one `ISampler` per framework-owned value type (brush, colour, transform, thickness). Core already covers the primitives. Register them in a static constructor.
3. **`Interpolator`** — an `InterpolatorCore` subclass that registers those samplers and implements `CreateScheduler`. This is also what makes a theme switch animate on your platform.
4. **State, interpreter, scheduler** — only if your framework's value types need their own write path.
5. **The surface behaviour** and the named-part resolution.
6. **The node, slot and link behaviours** — drag, connect, and slot measurement.
7. **The grid decorator and minimap**, implementing Core's `IWorkflowGridDecorator` / `IWorkflowMinimapOverlay`.

Steps 1–4 are the "adapter" in the transition-system sense too: the sampler and `CreateScheduler` contracts in full — including `ProtectedInvoke`'s honesty rule and the colour, size and transform conventions — are in [the animation skill's adapter reference](../../veloxdev-create-animation/references/adapter.md).

## Verifying it

Build the demo before building the template pack — the demo is where the behaviour gets settled, and the pack is derived from it afterwards.

⚙ **Copy the shape of `Examples/Workflow/<GUI> Trimmed/Demo`**: a window hosting the surface, the seven view roles, a node the user can drag, a connection the user can make, and a payload the automation can read. It is the minimum that proves the adapter works.

⚙ What must actually work, end to end: drag a node; drag a slot onto another slot and get a link; pan and zoom with the point under the pointer staying put; the ruler and minimap track; and a graph round trip through `Serialize()` / `Deserialize()` that restores positions correctly.

⚙ **Test at a deep zoom, not at 100%.** Every adapter bug in this repository's history — vanished links, detached links, drifting cameras — is invisible at 100% and obvious at 40%.

## Optionally, wrap the views as a template pack

If you are going to build this GUI more than once, the time to wrap the seven views as a `dotnet new` item pack is after the demo works — the pack is then a mechanical transcription of it, and [templates.md](templates.md) gives the CLI surface.

⚙ **Name the symbols and defaults exactly as the seven shipped packs do** — `nodeBackground`, `slotColor`, `linkColor`, `gridSpacing` and so on. The names are a cross-GUI contract, and a developer who has used one pack expects the command line to carry over.

⚙ **Never accept an option and silently discard it.** Either wire it, or declare it with the "accepted for cross-GUI CLI parity" note the shipped packs use, so the person passing it finds out from the file rather than from a missing colour.

⚙ If you are only building this GUI once, skip the pack entirely and hand-write the seven views from the demo. A pack pays for itself on the second project.
