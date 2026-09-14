# Windows Forms

`VeloxDev.WinForms` · targets `net461;net5.0-windows;netcoreapp3.0`

There is a long-form adapter README at `Src/Adapters/VeloxDev.WinForms/README.md`. This is the summary a consumer needs.

⚙ **Reference implementation:** `Examples/Workflow/WinForms Trimmed/Demo/Views/Workflow/` — all seven roles are single `.cs` files. `TreeView.cs` is where the pull model lives: the self-driven viewport, the virtualize inset and the `Refresh` calls.

⚙ **The "Trimmed" in that path means *minimal demo*, not trim configuration** — the library is **not** AOT- or trim-safe (`IsTrimmable=false`, and the animation path compiles expression trees at runtime). Do not read publish-time safety into the folder name.

## This adapter is different: nothing binds

WinForms has no attached properties, no bindings and no render transform. **The surface repositions everything itself, and you must tell it when something changed.**

```csharp
WorkflowSurfaceBehavior.SetIsEnabled(control, true);
WorkflowSurfaceBehavior.SetWorkflowTree(control, tree);   // the DataContext the XAML adapters get for free
WorkflowSurfaceBehavior.Refresh(control);                 // after every mutation
```

⚙ **`Refresh(host)` is mandatory after a mutation.** A node that moved without a refresh is still drawn where it was — there is no binding to notice.

⚙ **There is a synchronous escape hatch for the same-frame case.** A slot anchor written after a deferred refresh is a frame stale and the link visibly detaches from its port, so the adapter exposes a `SyncNow` for the node-sizing path and uses `Refresh` only at the end of a rebuild. Reach for `SyncNow` when you change a node's geometry and immediately need its links right.

## Adding your own content to a node

⚙ **No `Viewbox`.** `NodeView.ApplyPosition` computes the collapse factor and applies it in code — header height, row heights, slot sizes, font sizes — then re-syncs slot anchors in the same frame.

If you add content, add its scaling to that same path. Do not introduce a render transform.

⚙ **Do not move the port visuals**, and take the anchor frame seriously: this adapter measures with `PointToScreen`/`PointToClient` and writes through `SlotAnchorFromCanvasLocal`, because the canvas client area *is* the link rendering space and a node's `Location` already includes the pan and `ActualOffset`. Using the visual-centre form here subtracts `ActualOffset` a second time and shifts every link by `−ActualOffset` — this was a real bug in this adapter once.

## Zoom and pan

⚙ The gesture is `MouseWheel` **plus** an `Application.AddMessageFilter` on `WM_MOUSEWHEEL`, which swallows the message so no scrollable child scrolls instead. Without the filter a Ctrl+wheel reaches a child control first.

⚙ **There is no over-scroll growth on this adapter** — the scroll is clamped to the reachable range. That native clamping is the behaviour to copy if you write your own panning; growing the canvas near an edge makes the camera drift.

⚙ If you write your own zoom, wheel-up is `Scale *= 1 / 1.1` — `Scale` is a collapse factor.

## Links

Self-drawn in the consumer's `OnPaint` — `DrawLines` over the four-point polyline, endpoints from `Sender.Anchor` / `Receiver.Anchor`.

⚙ Use a synchronous `Invalidate()` + `Update()` after changing geometry. An asynchronous invalidate leaves trails during a drag, which is why the adapter also applies `WS_CLIPCHILDREN` / `WS_EX_COMPOSITED` window styling — and why it skips that styling above roughly a hundred descendants.

⚙ **If you host the canvas yourself, set the virtualize inset before writing `Viewport`.** This adapter has no decorator-hosted ruler to ask, so the reserve is passed explicitly; skipping it culls nodes that are still visible under the ruler.

## Item templates — `VeloxDev.WinForms.Templates`

**Generates code only.** All seven items are a single `.cs` file — no markup at all. Node and slot views are `UserControl` subclasses with `OnPaint`; the tree draws the surface itself.

⚙ **This is the only fully-wired pack.** Every declared parameter, including all four slot colours and `slotPath`, has a consumer here — which makes it the pack to read when you want to know what a parameter is supposed to do on a framework where it is inert.

⚙ Siblings are referenced as **static members**, not through markup namespace aliases, so all seven items must still be generated into one namespace.

⚙ **The generated tree calls `SetVirtualizeInset` before writing `Viewport`** and carries the `using VeloxDev.WorkflowSystem.StandardEx;` that makes it possible. Keep both when you edit it.

⚙ Style values are assigned in the generated `OnPaint` / constructor code, so a colour change is a normal C# edit — there is no markup layer to keep in sync.
