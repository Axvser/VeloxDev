# Windows Forms

`VeloxDev.WinForms` · targets `net461;net5.0-windows;netcoreapp3.0;net8.0-windows`

There is a long-form adapter README at `Src/Adapters/VeloxDev.WinForms/README.md`. This is the summary a consumer needs.

⚙ **Reference implementation:** `Examples/Workflow/WinForms Trimmed/Demo/Views/Workflow/` — all seven roles are single `.cs` files. The adapter's `WorkflowTreeView` is where the pull model lives (the self-driven viewport, the virtualize inset and the `Refresh` calls); the demo's `TreeView.cs` is the thin subclass that supplies the palette and view factories.

⚙ **The "Trimmed" in that path means *minimal demo*, not trim configuration** — see [the rule in SKILL.md](../../SKILL.md#checking-your-work).

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

⚙ **No `Viewbox`.** `WorkflowNodeAttachment.ApplyPosition` computes the collapse factor and raises `CollapseChanged`; the generated card's `OnCollapse` applies it in code — header height, row heights, slot sizes, font sizes — then re-syncs slot anchors in the same frame.

If you add content, add its scaling to that same path. Do not introduce a render transform.

```csharp
// Extend the generated card's OnCollapse(double collapse).
private void OnCollapse(double collapse)
{
    // The generated path already scales the header, rows, slot glyphs and fonts.
    myLabel.Font = new Font(myLabel.Font.FontFamily, 11f * (float)collapse);
    WorkflowSlotLayoutBehavior.SyncNow(this);   // re-measure the scaled slots in this frame
}
```

⚙ **Do not move the port visuals**, and take the anchor frame seriously: this adapter measures with `PointToScreen`/`PointToClient` and writes through `SlotAnchorFromCanvasLocal`, because the canvas client area *is* the link rendering space and a node's `Location` already includes the pan and `ActualOffset`. Using the visual-centre form here subtracts `ActualOffset` a second time and shifts every link by `−ActualOffset` — this was a real bug in this adapter once.

## Zoom and pan

⚙ The gesture is `MouseWheel` **plus** an `Application.AddMessageFilter` on `WM_MOUSEWHEEL`, which swallows the message so no scrollable child scrolls instead. Without the filter a Ctrl+wheel reaches a child control first.

⚙ **There is no over-scroll growth on this adapter** — the scroll is clamped to the reachable range. That native clamping is the behaviour to copy if you write your own panning; growing the canvas near an edge makes the camera drift.

⚙ If you write your own zoom, wheel-up is `Scale *= 1 / 1.1` — `Scale` is a collapse factor.

⚙ **A plain wheel is routed and applied by the adapter.** The message filter swallows the wheel so no child scrolls, routes it as `PointerWheelChanged`, and scrolls through its registered `IWorkflowSurfaceScroller` (`WorkflowInput.Scroller`) unless a subscriber set `PreventDefault`. Ctrl+wheel is the zoom gesture, and it can be refused the same way.

⚙ **`WorkflowSlotConnectionBehavior.UpdatePointer` feeds the connection candidate.** While the drag is active it calls `StandardSetPreviewReceiver(SlotHitTestEx.HitTest(tree, x, y))` — this adapter's `WindowFromPoint` target resolution only runs on release, so the drag path is geometric.

## Links

Pooled like the nodes: `ViewPool` materializes one `LinkView` per link in the tree's **visible set**
(`Helper.VisibleItems`, the gesture's virtual link included) and recycles them as that set changes.
Each view paints its own cubic Bézier (`DrawPath` over an `AddBezier` path, endpoints from
`Sender.Anchor` / `Receiver.Anchor`) — nothing draws links on behalf of another control.

⚙ **The attachment owns everything except the drawing.** `WorkflowLinkAttachment.Attach(control)` subscribes the endpoints, carves the window region, publishes the curve for hit-testing and raises the link's pointer events; your `OnPaint` draws the curve. The generated link view exposes it as `Attachment`.

⚙ **A link view is an opaque child window, so it carves its own window region to the stroke band of
the curve** (padded by the antialiasing fringe). A bounding box would paint over the grid, and a
full-size transparent sibling window does not composite here at all — WinForms clips it so only the
topmost one paints. Give it a `BackColor` equal to the surface's grid background, and keep the region
in step when the thickness or the curve changes.

⚙ **The pool fronts every view it materializes or recycles, so links must be sent to the back after
each visible-set change** — otherwise they end up over the node cards. The tree view does that in one
pass, from a hook registered behind the pool's own collection handler.

⚙ A zero-length curve (the connection gesture's first frame) has no stroke to widen and GDI+ throws
on it — skip the region instead of widening.

⚙ Use a synchronous `Invalidate()` + `Update()` after changing geometry. An asynchronous invalidate leaves trails during a drag, which is why the adapter also applies `WS_CLIPCHILDREN` / `WS_EX_COMPOSITED` window styling — and why it skips that styling above roughly a hundred descendants.

⚙ **If you host the canvas yourself, set the virtualize inset before writing `Viewport`.** This adapter has no decorator-hosted ruler to ask, so the reserve is passed explicitly; skipping it culls nodes that are still visible under the ruler.

⚙ **The link context menu is built in code, not markup.** The tree subclass overrides `OnBuildLinkMenu(ContextMenuStrip menu, IWorkflowLinkViewModel link)`, and the adapter opens the strip on right-press, positions it, and suspends the input router while it is open. **Neither the base override nor the generated template adds an item** — including Delete — so add or remove `ToolStripMenuItem`s in that override.

## Item templates — `VeloxDev.WinForms.Templates`

**Generates code only.** All seven items are a single `.cs` file — no markup at all. The tree paints the grid surface itself and pools node and link views; a node card and a slot view are `UserControl`/`Control` subclasses that carry an attachment, and a link view is a `Control` that draws itself.

⚙ **All seven items are thin.** The adapter owns the wiring through attachments; each generated file supplies only what is yours:

| item | derives from | you supply |
|---|---|---|
| tree | `WorkflowTreeView` | the palette, `CreateNodeView` / `CreateLinkView`, `OnBuildLinkMenu` (plus `OnTreeAttached` / `OnSurfaceRefreshed` if you add an overlay) |
| node | `UserControl` + `WorkflowNodeAttachment.Attach(this)` | the card itself — its panels and `OnPaint`; the attachment owns binding, placement, zoom collapse and the title/slot lookups |
| slot | `Control` + `WorkflowSlotAttachment.Attach(this)` | the glyph path and its by-state colours |
| link | `Control` + `WorkflowLinkAttachment.Attach(this)` | `OnPaint` — the attachment owns the endpoints, region carving and pointer events |
| minimap | `WorkflowMinimapOverlay` | the four palette colours |
| grid-decorator | `WorkflowGridDecorator` | eight colours, the spacing, how many lines make a major one |
| template-selector | `WorkflowTemplateSelector` | which view each item kind gets (the generated file wires node and link) |

Three helpers are shared by all of them: `WorkflowSurfaceColors.Parse`, `WorkflowSurfaceGraphics.RoundedRectangle`,
`WorkflowSurfaceGrid` (grid-line classification and ruler-label formatting).

⚙ **Placement belongs to the attachment, not the card.** `WorkflowNodeAttachment` implements `IWorkflowSurfaceNodeView`, so the surface re-places the card on every pan. The generated card exposes its attachment (`view.Attachment`) and its `PART_DynamicOutputs` host; keep both when you edit it. Node and link views are pooled by the surface; a slot view is a child of its node card, not a separately pooled item.

⚙ **This is the only fully-wired pack.** Every declared parameter, including all four slot colours and `slotPath`, has a consumer here — which makes it the pack to read when you want to know what a parameter is supposed to do on a framework where it is inert.

⚙ **Two parameters are this pack's own.** The link item declares `surfaceBackground` (it must match the tree's surface background, or the carved stroke band shows a seam), and the decorator's `rulerBackground` default is `#70252526` rather than the other packs' `#C8252526` — WinForms cannot dim the opaque node-card children over the ruler band, so it uses a lower alpha.

⚙ Siblings are referenced as **static members**, not through markup namespace aliases, so all seven items must still be generated into one namespace.

⚙ **The adapter's `WorkflowTreeView` calls `SetVirtualizeInset` before writing `Viewport`** (`RulerReserve` on both axes) and carries the `using VeloxDev.WorkflowSystem.StandardEx;` that makes it possible. The generated tree now only supplies the palette and the two view factories, so there is nothing to keep in step there.

⚙ Style values are assigned in the generated `OnPaint` / constructor code, so a colour change is a normal C# edit — there is no markup layer to keep in sync.
