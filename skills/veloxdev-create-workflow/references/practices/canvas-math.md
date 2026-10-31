# The canvas model

Two audiences in one file:

- **[What you need while writing application code](#what-you-need-while-writing-application-code)** — short, and the part that decides whether your nodes, links and overlays land in the right place.
- **[The full model](#the-full-model)** — for when you are writing an adapter for a GUI VeloxDev does not support, or drawing something of your own on the canvas.

## What you need while writing application code

Everything in Core's `WorkflowSystem/GUI/`, reachable through `tree.Layout` and the node properties. The adapter calls most of it. What follows is what surfaces in *your* code.

### Two coordinate spaces

| Frame | What it is | Where it lives |
|---|---|---|
| **world** | the coordinate a node was *placed* at, and what a file stores | the `Anchor`/`Size` **fields** |
| **collapsed** (canvas-local) | `world ÷ Scale` | the `Anchor`/`Size` **getters**, links, the spatial index, `Viewport`, the minimap |

```
collapsed = world / Scale          (per axis)
```

⚙ **Read through the getter, write through the setter.** `node.Anchor` gives you the collapsed value; assigning `node.Anchor = new Anchor(x, y, layer)` stores a world value. At any zoom other than 1 the getter returns a copy; at exactly 1 it returns the live object, and a write through that object bypasses the spatial index and the views.

⚙ **Anything you position yourself is in collapsed space**, because that is the frame the canvas lays out in. Place a custom element at the node's `Anchor` (collapsed) plus your offset in design pixels.

### Scale

`tree.Layout.Scale` is a **collapse factor**, so it runs backwards from an ordinary zoom level: zoom-in is `Scale < 1`, and the on-screen size of the content is `1 / Scale`. An adapter clamps it to `[0.1, 10]`.

⚙ **Wheel-up is zoom-in: `Scale *= 1 / 1.1`.** The inverse factor is the one place the inversion bites.

⚙ **Changing `Scale` leaves node positions where they are.** The getters recompute and the tree re-virtualizes on its own. Keeping the point under the pointer from jumping is the zoom commit chain's job — see [the zoom commit chain](#zoom--the-commit-chain).

### The canvas lays itself out

⚙ **`ActualSize`, `ActualOffset`, `PositiveOffset` and `NegativeOffset` are computed.** `CanvasLayout` derives all four from `OriginSize`, the two offsets and `Scale`. `ActualOffset ≡ NegativeOffset` (the canvas translation), and `ActualSize` grows only when `Scale < 1`, to hold the enlarged collapsed content.

⚙ `ActualOffset` is what you want if you draw on the canvas yourself: `screen = world + ActualOffset`, and `collapsed = scroll − ActualOffset`. It moves with panning and with negative-world content.

⚙ `OriginSize` defaults to `1920 × 1080` and is only a canvas *baseline*, not a world boundary; content may extend well past it. Change it through `layout.AdaptTo(...)` on a host resize if you care.

### Slot anchors and links

A link is drawn between two **slot anchors**, not node centres.

⚙ **`slot.Anchor` begins as `(NaN, NaN, 0)` and is written by a GUI.** Core does not compute it. The adapter measures the port and writes the anchor back; the render-ready gate suppresses the link until then. A virtual link (the drag preview) has no parents and is exempt.

⚙ If you draw links yourself, use `Sender.Anchor` / `Receiver.Anchor` and check `link.IsRenderReady()` first — the same predicate every shipped link view uses.

### Virtualization

⚙ **The visible set is driven by `tree.GetHelper().Viewport`, in collapsed coordinates.** An adapter with a scroll viewer writes it for you. If you host the canvas yourself, write it — and set the virtualize inset for the ruler band first, or nodes under the ruler get culled while still visible.

⚙ Writing `Viewport` virtualizes synchronously; the 10 fps dirty loop is the deferred second path. `MarkDirty()` is called for you when geometry changes.

### Minimap

If you build your own, `WorkflowSurfaceMath` has the whole mapping: `MinimapFit`, `MinimapLocal`, `MinimapViewportRect`, `MinimapToWorld`, `MinimapToScroll`, `MinThumbSize` — all in collapsed space.

## The full model

The rest of this file is for writing an adapter, or for drawing your own content on the canvas. It is the same math the shipped adapters call; they do not re-derive any of it.

### Who supplies each number

| Layer | Variables | Supplier |
|---|---|---|
| **stored / external** | node `Anchor`/`Size` fields (world), `CanvasLayout.OriginSize`, `PositiveOffset`, `NegativeOffset`, `Scale`, `ViewportOffset`, `ZoomCenter`, `CollapsePivot`, `TreeHelper.Viewport` | your gesture, your initialization, a loaded file, or Core's own clamps |
| **derived by Core** | the collapse getters, `ActualSize`, `ActualOffset`, spatial-index bounds, grid membership, `VisibleItems` | `CanvasLayout.Update()` and the getters |
| **measured by the GUI** | scroll offsets, viewport size, `slot.Anchor`, native canvas size | the adapter, every frame |
| **pure functions** | everything in `WorkflowSurfaceMath` except `ClampScrollOffset` and `EnsureNegativeCover` | computed at the call site |

### `CanvasLayout`

```csharp
private void Update()
{
    var baseWidth  = OriginSize.Width  + PositiveOffset.Horizontal + NegativeOffset.Horizontal;
    var baseHeight = OriginSize.Height + PositiveOffset.Vertical   + NegativeOffset.Vertical;

    var sx = Scale.Horizontal > 0 && Scale.Horizontal < 1 ? 1d / Scale.Horizontal : 1d;
    var sy = Scale.Vertical   > 0 && Scale.Vertical   < 1 ? 1d / Scale.Vertical   : 1d;

    ActualSize.Width  = baseWidth  * sx;      // grown only when zoomed IN (Scale < 1)
    ActualSize.Height = baseHeight * sy;
    ActualOffset = new Offset(NegativeOffset.Horizontal, NegativeOffset.Vertical);

    OnPropertyChanged(nameof(ActualSize));
}
```

| Property | Default | Written by | Triggers `Update()` |
|---|---|---|---|
| `OriginSize` | `1920 × 1080` | you (`AdaptTo`, on host resize) | yes |
| `PositiveOffset` | `(0, 0)` | **Core** (over-scroll clamp) | yes |
| `NegativeOffset` | `(0, 0)` | **Core** (over-scroll clamp, `EnsureNegativeCover`) | yes |
| `Scale` | `(1, 1)` | you (the zoom gesture) | yes |
| `ActualSize` | `1920 × 1080` | **Core only** | — |
| `ActualOffset` | `(0, 0)` | **Core only** | — |
| `ViewportOffset` | `(0, 0)` | you | no |
| `ZoomCenter` | `ViewportCenter` | design time only | no |
| `CollapsePivot` | `(0, 0, 0)` | you, immediately before `Scale` | no (deliberately) |

⚙ `ActualSize` is mutated in place and needs its explicit re-raise; `ActualOffset` is replaced, so its generated setter raises on its own. Both happen in the same `Update()`.

⚙ `CanvasLayout.Equals` compares `OriginSize`, `PositiveOffset`, `NegativeOffset`, `Scale` and `ZoomCenter` only.

### `ZoomCenter`

`WorldOrigin = 0` collapses everything toward `(0, 0)` — a node far from the origin visibly slides across the screen on every zoom step. `ViewportCenter = 1` (the default, and what every adapter uses) keeps the point under the viewport centre fixed instead. Nothing sets `WorldOrigin` at runtime; `CollapsePivot` is written by every adapter before it writes `Scale`.

### Zoom — the commit chain

Every shipped adapter follows these five steps in this order. The order is what keeps the camera still.

```csharp
// 1. which world point is under the viewport centre right now?
var (px, py) = WorkflowSurfaceMath.WorldAtViewportCenter(scrollX, scrollY, vpW, vpH, layout);

// 2. remember it, then commit the new scale
layout.CollapsePivot = new Anchor(px, py, 0);
layout.Scale = new Scale(layout.Scale.Horizontal * factor, layout.Scale.Vertical * factor);

// 3. make sure negative world content is reachable at the new scale
WorkflowSurfaceMath.EnsureNegativeCover(tree);

// 4. re-layout, then compute the scroll that puts the pivot back under the centre
var (sx, sy) = WorkflowSurfaceMath.PivotCenterScroll(px, py, layout, vpW, vpH);

// 5. clamp and apply
sx = WorkflowSurfaceMath.ClampScrollOffset(sx, WorkflowSurfaceMath.ScrollMax(layout.ActualSize.Width, vpW), layout, true);
```

⚙ **`WorldAtViewportCenter` and `PivotCenterScroll` are exact inverses.** The camera drifts when one of them reads a stale input — usually a viewport size or an `ActualOffset` read before the scale commit.

⚙ **`EnsureNegativeCover` runs after the scale write and before any read of `ActualOffset`/`ActualSize`.** It scans node anchors for negative collapsed coordinates and grows `NegativeOffset` monotonically so they stay reachable; the `0.01` dead-band is change detection, and it returns `true` only when it actually grew.

⚙ **Clamp the final scroll to the reachable range.** Growth near an edge makes the camera drift inward on every zoom step; the Blazor adapter clamps for that reason.

### Panning and over-scroll

```csharp
public static double ClampScrollOffset(double desired, double max, CanvasLayout layout,
    bool horizontal, double threshold = 0d, double extendRatio = 0d)
```

Scroll below `0` grows `NegativeOffset`; above `max` it grows `PositiveOffset`. The asymmetry is deliberate:

- **Positive edge** returns `max`. Growing `PositiveOffset` lengthens the extent without moving content, so the caller just clamps.
- **Negative edge** returns the growth amount when `extendRatio > 0`, because growing `NegativeOffset` *translates* everything by that amount, and the caller scrolls forward by the same delta to keep the content under the cursor still.

⚙ `extendRatio == 0` disables growth entirely. `DefaultPanExtendRatio` is `0.15`, which is what a drag uses.

⚙ `AxisBaseExtent = OriginSize + PositiveOffset + NegativeOffset` on that axis, so growth is a discrete step proportional to the canvas.

### Slot anchors — the three-way contract

`slot.Anchor` is written by the GUI from a measured visual centre, and **which function you call is decided by what coordinate the measurement arrived in.** Choosing wrong offsets every link in the graph by a constant.

| Function | Formula | Use when the measured centre is in… |
|---|---|---|
| `SlotAnchorFromVisualCenter(x, y, layer, layout)` | `(x − ActualOffset.H, y − ActualOffset.V, layer)` | **screen** space, and the host does *not* already translate its children by `ActualOffset` |
| `SlotAnchorFromCanvasLocal(x, y, layer)` | `(x, y, layer)` — identity | **already collapsed**, because the host applies the canvas translation itself |
| `SlotAnchorFromNode(nodeX, nodeY, localX, localY, layer)` | `(nodeX + localX, nodeY + localY, layer)` | a **collapsed node anchor plus a local offset** — no coordinate host at all |

All three return a **collapsed** value. WPF, Avalonia and Jalium use the first; WinUI/MAUI/WinForms the second and fall back to the third. **Razor calls none of them** — its JS measures the port and the code-behind assigns `slot.Anchor` directly, so there is no call site to choose. Every adapter measures a control and writes `slot.Anchor`.

⚙ **WinForms' own comment states the failure mode:** using `SlotAnchorFromVisualCenter` there subtracts `ActualOffset` a second time and shifts every link by `−ActualOffset`.

⚙ Grouped slot sets re-notify one frame late on purpose — after the binding engine has generated the containers. That ordering lets adapters measure containers that exist.

### Links

```csharp
var s = new Point(StartLeft, StartTop);          // sender slot anchor, collapsed
var e = new Point(EndLeft,   EndTop);            // receiver slot anchor, collapsed

// The shared control points: each one leaves along its OWN port's outward normal.
var points = LinkCurve.LinkCurvePoints(link, s.X, s.Y, e.X, e.Y, pullMinimum);
// points = start, c1, c2, end — cubic Bézier: s → (c1, c2) → e

// …or the sampled curve in one call (the same geometry the hit test uses):
var curve = LinkCurve.BuildLinkCubic(link, s.X, s.Y, e.X, e.Y, pullMinimum);
```

`LinkCurve.LinkCurvePoints` (and `BuildLinkCubic`, which samples its result) pulls each control point along the outward normal of its own port's node edge — `PortOutward` picks the edge nearest the port, normalised, so a wide node does not favour its horizontal edges. A port on the right edge leaves horizontally, one on the top edge leaves upward; the pull distance is measured on the normal's axis, so the two ends stay independent. The `pullMinimum` keeps two ports close together from degenerating the curve into a straight segment, and swapping sender and receiver returns the same four points in reverse.

⚙ **The horizontal rule is the fallback for an end with no node** — the drag preview, whose ends are placeholder slots, and an unbound pooled view. It equals the outward-normal result whenever the sender is on its node's right edge and the receiver on the other's left. `LinkCurve.BuildCubic` exposes that horizontal house curve for a caller with no link.

⚙ **The fallback formula is duplicated wherever a platform needs the geometry outside its render pass.** Razor's adapter mirrors `LinkCurve.PortOutward` in `wwwroot/veloxdev.workflow.js` (it only touches elements marked `data-veloxdev-link-curve`) so a wheel zoom can rewrite link curves in the same frame. Its .NET link view uses `LinkCurve.LinkCurvePoints`; the two paths implement the same normal rule. WinForms widens the same four points into the window region it carves out of the stroke band. Change the shape and all of them change with it.

⚙ **Keep every coordinate bounded and local.** A host with a size or coordinate limit needs one of: a viewport-sized overlay outside the growing canvas (MAUI), an offset frame that bakes the pan into the geometry (WinUI), or self-bounding geometry (Jalium).

⚙ Views are realized in background-priority batches, so a link can appear before its endpoints are measured. The bindings re-run the render once the measurement lands; no events or timestamps are involved.

### Virtualization internals

⚙ `WorkflowSpatialEx.SetVirtualizeInset(left, top, right, bottom)` widens the **query box only**, by the floating ruler band. It never touches the authoritative `Viewport` and never affects rendering. It is change-detecting and allocation-free, so calling it every pass is intended.

⚙ The grid is keyed by cell, `CellKey = (floor(x / cellSize), floor(y / cellSize))`, default `cellSize = 200`, fixed at `Install`. Empty bounds index one cell. The max edge uses `ceil`, so a rect ending on a boundary indexes one extra cell — over-selection only, never under-selection.

⚙ **The grid has a re-entrancy guard.** A zoom cascade re-enters it; without the guard the dictionary would be mutated mid-enumeration.

⚙ A link is indexed by the union of its two endpoints' bounds, so a long link crossing the viewport realizes both nodes.

### `WorkflowSurfaceMath` — the full surface

| Member | Result |
|---|---|
| `ToWorld(scroll, actualOffset)` | `scroll − actualOffset` — scroll to collapsed |
| `ToWorldAnchor(screenX, screenY, layer, layout)` | `(screenX − ActualOffset.H, screenY − ActualOffset.V, layer)` |
| `ToScreen(worldX, worldY, layout)` | `(worldX + ActualOffset.H, worldY + ActualOffset.V)` |
| `ScrollMax(extent, viewport)` | `max(0, extent − viewport)` |
| `ClampValue(value, min, max)` | `max(min, min(value, max))` |
| `ClampScrollOffset(…)` | see above; **mutates `layout`** |
| `GridWorldLeft(scrollOffset, contentOffset)` / `GridWorldTop` | `scrollOffset − contentOffset` |
| `GridX(worldValue, worldLeft, contentRectX)` / `GridY` | `contentRectX + (worldValue − worldLeft)` |
| `GridFirstLine(worldLeft, spacing)` | `floor(worldLeft / spacing) * spacing` |
| `SlotAnchorFromVisualCenter` / `SlotAnchorFromCanvasLocal` / `SlotAnchorFromNode` | see above |
| `ScrollCenter(scrollX, scrollY, vpW, vpH)` | the viewport centre in scroll space |
| `WorldAtViewportCenter(…)` | `((cx − ActualOffset.H) * Scale.H, (cy − ActualOffset.V) * Scale.V)` — the collapsed→world conversion in `WorkflowSurfaceMath` |
| `PivotCenterScroll(…)` | its exact inverse |
| `LayoutPivot(layout)` | `CollapsePivot`, or `(0, 0, 0)` under `ZoomCenter.WorldOrigin` |
| `ScaleCollapse(anchorX, anchorY, scaleX, scaleY)` | `(1/sx, 1/sy, −ax, −ay)` — the transform a scale container applies |
| `ScaleVisualBounds(…)` | the collapsed bounding box |
| `EnsureNegativeCover(tree)` | grows `NegativeOffset`; **mutates `layout`**, returns `true` if it grew |
| `MinimapFit` · `MinimapLocal` · `MinimapViewportRect` · `MinimapToWorld` · `MinimapToScroll` · `MinThumbSize` | the minimap mapping |

Plus `WorkflowBounds` — a struct with `FromNode`, `FromNodes`, `Union`, `IsEmpty`, in collapsed space.

⚙ Several members are *named* "world" (`ToWorld`, `GridWorldLeft`, `WorkflowBounds`) but operate in **collapsed** space. Trust the table, not the name.

⚙ **`WorldAtViewportCenter` is one collapsed→world conversion; the node helpers have another.** `WorkflowNodeEx.StandardMove` and `StandardSetLayer` convert through the shared `ViewToWorldFactors`, because a drag delta arrives in **view** space while `Anchor` is stored in **world** space. A custom drag uses those methods: `StandardSetAnchor` for an anchor that is already world (`screen − ActualOffset`), `StandardMove` for a view-space delta.

⚙ `ClampScrollOffset` and `EnsureNegativeCover` are the two members of this class that write to `CanvasLayout`; everything else is pure.

⚙ `Scale == 0` is treated as `1` everywhere. It is a guard, not a supported zoom level.

⚙ None of this is thread-safe, and the tree is UI-bound — zoom, pan and virtualization all run on the UI thread.

⚙ **`ViewportOffset` and `ActualOffset` are different numbers.** `ViewportOffset` is the world position of the visible region (used to restore scroll); `ActualOffset` is the canvas translation. WinForms falls back to `ViewportOffset` when it cannot read a pan translate because it has no scroll host — that is the only place the two are conflated, and it is deliberate.

⚙ **Both ends of the round trip belong to the surface.** The adapter persists `ViewportOffset` on every measured scroll and puts the host back on it when a tree arrives, so a load path scrolls nothing. The conversions are `ViewportOffsetFromScroll` (world) and `ViewportRestoreScroll` (scroll) — the pair exists to make the direction impossible to get wrong at a call site.

When links are invisible or the camera drifts, read [references/pitfalls/geometry-and-viewport.md](../pitfalls/geometry-and-viewport.md).
