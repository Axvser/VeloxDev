# The canvas model

Two audiences in one file:

- **[What you need while writing application code](#what-you-need-while-writing-application-code)** — short, and the part that decides whether your nodes, links and overlays land in the right place.
- **[The full model](#the-full-model)** — for when you are writing an adapter for a GUI VeloxDev does not support, or drawing something of your own on the canvas.

## What you need while writing application code

Everything in Core's `WorkflowSystem/GUI/`, reachable through `tree.Layout` and the node properties. You will not call most of it — the adapter does. What follows is what surfaces in *your* code.

### Two coordinate spaces

| Frame | What it is | Where it lives |
|---|---|---|
| **world** | the coordinate a node was *placed* at, and what a file stores | the `Anchor`/`Size` **fields** |
| **collapsed** (canvas-local) | `world ÷ Scale` | the `Anchor`/`Size` **getters**, links, the spatial index, `Viewport`, the minimap |

```
collapsed = world / Scale          (per axis)
```

⚙ **Read through the getter, write through the setter.** `node.Anchor` gives you the collapsed value; assigning `node.Anchor = new Anchor(x, y, layer)` stores a world value. This is the rule from the SKILL, and the reason it is a rule: at any zoom other than 1 the getter hands you a discarded copy, and at exactly 1 it hands you the live object but changes it without telling the spatial index or the views.

⚙ **Anything you position yourself is in collapsed space**, because that is the frame the canvas lays out in. If you place a custom element on the canvas, take the node's `Anchor` (collapsed) as-is and add your offset in design pixels.

### Scale

`tree.Layout.Scale` is a **collapse factor**, so it runs backwards from an ordinary zoom level: zoom-in is `Scale < 1`, and the on-screen size of the content is `1 / Scale`. An adapter clamps it to `[0.1, 10]`.

⚙ If you write your own zoom gesture, wheel-up is `Scale *= 1 / 1.1` — not `* 1.1`. This is the one place the inversion bites, and it is easy to get backwards.

⚙ After changing `Scale`, node positions do not move themselves: the getters recompute, and the tree re-virtualizes on its own. What *does* need your attention is keeping the point under the pointer from jumping — see [the zoom commit chain](#zoom--the-commit-chain) if you are doing this yourself.

### The canvas lays itself out

⚙ **Do not write `ActualSize`, `ActualOffset`, `PositiveOffset` or `NegativeOffset`.** All four are computed by `CanvasLayout` from `OriginSize`, the two offsets and `Scale`. `ActualOffset ≡ NegativeOffset` (the canvas translation), and `ActualSize` grows only when `Scale < 1`, to hold the enlarged collapsed content.

⚙ `ActualOffset` is what you want if you draw on the canvas yourself: `screen = world + ActualOffset`, and `collapsed = scroll − ActualOffset`. Never assume it is zero — it moves with panning and with negative-world content.

⚙ `OriginSize` defaults to `1920 × 1080` and is only a canvas *baseline*, not a world boundary; content may extend well past it. Change it through `layout.AdaptTo(...)` on a host resize if you care.

### Slot anchors and links

A link is drawn between two **slot anchors**, not node centres.

⚙ **`slot.Anchor` starts at `(NaN, NaN, 0)` and is only ever written by a GUI.** Core does not compute it. Until the adapter measures the port and writes the anchor back, the render-ready gate suppresses the link — so "my links are invisible" almost always means the slots were never measured, not that the link is broken. A virtual link (the drag preview) has no parents and is exempt.

⚙ If you draw links yourself, use `Sender.Anchor` / `Receiver.Anchor` and check `link.IsRenderReady()` first — the same predicate every shipped link view uses.

### Virtualization

⚙ **The visible set is driven by `tree.GetHelper().Viewport`, which is in collapsed coordinates.** An adapter with a scroll viewer writes it for you. If you host the canvas yourself, you must write it — and set the virtualize inset for the ruler band first, or nodes under the ruler get culled while still visible.

⚙ Writing `Viewport` virtualizes synchronously; the 10 fps dirty loop is the deferred second path. `MarkDirty()` is called for you when geometry changes.

### Minimap

If you build your own, `WorkflowSurfaceMath` has the whole mapping: `MinimapFit`, `MinimapLocal`, `MinimapViewportRect`, `MinimapToWorld`, `MinimapToScroll`, `MinThumbSize` — all in collapsed space.

## The full model

The rest of this file is for writing an adapter, or for drawing your own content on the canvas. It is the same math the shipped adapters call; they do not re-derive any of it, and neither should you.

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

⚙ `ActualSize` is mutated in place and so needs its explicit re-raise; `ActualOffset` is replaced, so its generated setter raises on its own. Both happen in the same `Update()`.

⚙ `CanvasLayout.Equals` compares `OriginSize`, `PositiveOffset`, `NegativeOffset`, `Scale` and `ZoomCenter` only.

### `ZoomCenter`

`WorldOrigin = 0` collapses everything toward `(0, 0)` — a node far from the origin visibly slides across the screen on every zoom step. `ViewportCenter = 1` (the default, and what every adapter uses) keeps the point under the viewport centre fixed instead. Nothing sets `WorldOrigin` at runtime, and `CollapsePivot` is written by every adapter before it writes `Scale` but is read back by almost nothing.

### Zoom — the commit chain

Every shipped adapter follows these five steps in this order. Getting the order wrong produces a camera that drifts or jumps.

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

⚙ **`WorldAtViewportCenter` and `PivotCenterScroll` are exact inverses.** If the camera drifts, one of them has a stale input — usually a viewport size or an `ActualOffset` read before the scale commit.

⚙ **`EnsureNegativeCover` runs after the scale write and before any read of `ActualOffset`/`ActualSize`.** It scans node anchors for negative collapsed coordinates and grows `NegativeOffset` monotonically so they stay reachable; the `0.01` dead-band is change detection, and it returns `true` only when it actually grew.

⚙ **Clamp the final scroll to the reachable range rather than growing the canvas near an edge.** Growth is what makes a camera drift inward on every zoom step when the content is near a boundary — the Blazor adapter was rewritten to pure clamping for exactly this reason.

### Panning and over-scroll

```csharp
public static double ClampScrollOffset(double desired, double max, CanvasLayout layout,
    bool horizontal, double threshold = 0d, double extendRatio = 0d)
```

Scroll below `0` grows `NegativeOffset`; above `max` it grows `PositiveOffset`. The asymmetry is deliberate:

- **Positive edge** returns `max`. Growing `PositiveOffset` lengthens the extent without moving content, so the caller just clamps.
- **Negative edge** returns the growth amount when `extendRatio > 0`, because growing `NegativeOffset` *translates* everything by that amount, and the caller must scroll forward by the same delta to keep the content under the cursor still.

⚙ `extendRatio == 0` disables growth entirely. `DefaultPanExtendRatio` is `0.15`, which is what a drag uses.

⚙ `AxisBaseExtent = OriginSize + PositiveOffset + NegativeOffset` on that axis, so growth is a discrete step proportional to the canvas, not a smooth follow.

### Slot anchors — the three-way contract

`slot.Anchor` is written by the GUI from a measured visual centre, and **which function you call is decided by what coordinate the measurement arrived in.** Choosing wrong offsets every link in the graph by a constant.

| Function | Formula | Use when the measured centre is in… |
|---|---|---|
| `SlotAnchorFromVisualCenter(x, y, layer, layout)` | `(x − ActualOffset.H, y − ActualOffset.V, layer)` | **screen** space, and the host does *not* already translate its children by `ActualOffset` |
| `SlotAnchorFromCanvasLocal(x, y, layer)` | `(x, y, layer)` — identity | **already collapsed**, because the host applies the canvas translation itself |
| `SlotAnchorFromNode(nodeX, nodeY, localX, localY, layer)` | `(nodeX + localX, nodeY + localY, layer)` | a **collapsed node anchor plus a local offset** — no coordinate host at all |

All three return a **collapsed** value. WPF and Avalonia use the first, WinUI/MAUI/WinForms the second, Jalium the third; all fall back to `SlotAnchorFromNode`.

⚙ WinForms' own comment states the failure mode exactly: using `SlotAnchorFromVisualCenter` there subtracts `ActualOffset` a second time and shifts every link by `−ActualOffset`.

⚙ Grouped slot sets re-notify one frame late on purpose — after the binding engine has generated the containers. Firing synchronously made adapters measure containers that did not exist yet and fall back to `(0, 0)`.

### Links

```csharp
var s  = new Point(StartLeft, StartTop);      // sender slot anchor, collapsed
var e  = new Point(EndLeft,   EndTop);        // receiver slot anchor, collapsed
double dx = EndLeft - StartLeft;
const double phi = 0.6180339887;              // 1/φ
double stub = dx / 2.0 * (1.0 - phi);         // ≈ 0.190983 · dx
var p1 = new Point(s.X + stub, s.Y);
var p4 = new Point(e.X - stub, e.Y);
// polyline: s → p1 → p4 → e
```

The four-point golden-stub elbow is the shared geometry — every adapter draws it.

⚙ **Never hand a renderer an unbounded absolute coordinate or a whole-world canvas size.** That is the contract that keeps links visible at deep zoom. A host with a size or coordinate limit needs one of: a viewport-sized overlay outside the growing canvas (MAUI), an offset frame that bakes the pan into the geometry (WinUI), or self-bounding geometry (Jalium).

⚙ Views are realized in background-priority batches, so a link can appear before its endpoints are measured. Once the measurement lands the bindings re-run the render — no events or timestamps are involved.

### Virtualization internals

⚙ `WorkflowSpatialEx.SetVirtualizeInset(left, top, right, bottom)` widens the **query box only**, by the floating ruler band. It never touches the authoritative `Viewport` and never affects rendering. It is change-detecting and allocation-free, so calling it every pass is intended.

⚙ The grid is keyed by cell, `CellKey = (floor(x / cellSize), floor(y / cellSize))`, default `cellSize = 200`, fixed at `Install`. Empty bounds index one cell. The max edge uses `ceil`, so a rect ending on a boundary indexes one extra cell — over-selection only, never under-selection.

⚙ **The grid has a re-entrancy guard.** A zoom cascade re-enters it; without the guard the dictionary is mutated mid-enumeration. Do not remove it.

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
| `WorldAtViewportCenter(…)` | `((cx − ActualOffset.H) * Scale.H, (cy − ActualOffset.V) * Scale.V)` — the only collapsed→world conversion in Core |
| `PivotCenterScroll(…)` | its exact inverse |
| `LayoutPivot(layout)` | `CollapsePivot`, or `(0, 0, 0)` under `ZoomCenter.WorldOrigin` |
| `ScaleCollapse(anchorX, anchorY, scaleX, scaleY)` | `(1/sx, 1/sy, −ax, −ay)` — the transform a scale container applies |
| `ScaleVisualBounds(…)` | the collapsed bounding box |
| `EnsureNegativeCover(tree)` | grows `NegativeOffset`; **mutates `layout`**, returns `true` if it grew |
| `MinimapFit` · `MinimapLocal` · `MinimapViewportRect` · `MinimapToWorld` · `MinimapToScroll` · `MinThumbSize` | the minimap mapping |

Plus `WorkflowBounds` — a struct with `FromNode`, `FromNodes`, `Union`, `IsEmpty`, in collapsed space.

⚙ Several members are *named* "world" (`ToWorld`, `GridWorldLeft`, `WorkflowBounds`) but operate in **collapsed** space. The only real collapsed→world step in the whole library is inside `WorldAtViewportCenter`. Trust the table, not the name.

⚙ `ClampScrollOffset` and `EnsureNegativeCover` are the two members of this class that write to `CanvasLayout`; everything else is pure.

⚙ `Scale == 0` is treated as `1` everywhere. It is a guard, not a supported zoom level.

⚙ None of this is thread-safe, and the tree is UI-bound — zoom, pan and virtualization all run on the UI thread.

⚙ **The viewport is not the canvas offset.** `ViewportOffset` is the world position of the visible region (used to restore scroll); `ActualOffset` is the canvas translation. WinForms synthesizes scroll from `ViewportOffset` because it has no scroll host — that is the only place the two are conflated, and it is deliberate.
