# VeloxDev.Razor — Adapter API

Package: `VeloxDev.Razor` · Namespace: `VeloxDev.WorkflowSystem.AttachedBehaviors`

Blazor has no attached-property system and no element tree to attach to. This adapter therefore
expresses the workflow behaviors as **components with parameters**, wired together with
**render fragments** and `@ref`. It matches the WPF/Avalonia/WinUI/MAUI/WinForms adapters
capability-for-capability, but the composition model is Blazor-native:

| XAML attached-property framework | Blazor |
|----------------------------------|--------|
| `behaviors:WorkflowXxx.IsEnabled="True"` on your element | `<WorkflowXxx IsEnabled="true">…</WorkflowXxx>` component |
| Name-based wiring (`ScrollViewerName`, `CanvasName`, …) | Direct `ScrollViewerId`/`CanvasId` + render fragments |
| Grid decorator / minimap found by `x:Name` | Passed as `RenderFragment` (`GridDecorator`/`Minimap`) |
| `ViewPool.TemplateSelector` | `ViewPool.ItemTemplate` + consumer-side type dispatch |

Gestures (pan, node drag, slot connection, slot measurement) run in `wwwroot/veloxdev.workflow.js`
via JS interop; slot positions are measured on demand and auto-discovered from
`[data-veloxdev-slot-id]` attributes.

---

## Components

| Component | Renders | Purpose |
|-----------|---------|---------|
| [`WorkflowSurfaceBehavior`](#workflowsurfacebehavior) | The scrollable canvas (`scroller` + `canvas` + content layer) | Pan, scroll reporting, auto-expansion in all four directions, viewport persistence |
| [`WorkflowNodeDragBehavior`](#workflownodedragbehavior) | A positioned wrapper | Positions a node by `Anchor` and translates drags into `MoveCommand` |
| [`WorkflowSlotConnectionBehavior`](#workflowslotconnectionbehavior) | A slot wrapper (`data-veloxdev-slot-id`) | Drives the `SendConnection`/`SetPointer`/`ReceiveConnection`/`ResetVirtualLink` gesture |
| [`WorkflowSlotLayoutBehavior`](#workflowslotlayoutbehavior) | A node wrapper | Measures every `[data-veloxdev-slot-id]` descendant and writes `slot.Anchor` |
| [`ViewPool`](#viewpool) | Pooled item views | Renders an observable collection and re-renders on change |
| [`WorkflowGridDecorator`](#workflowgriddecorator) | Ruler overlay | Renders rulers; consumes the `SurfaceViewport` context |
| [`WorkflowMinimapOverlay`](#workflowminimapoverlay) | Minimap SVG | Node overview + viewport rect; click/drag to navigate |

Context records: `SurfaceViewport`, `SurfaceCanvas`.

---

## WorkflowSurfaceBehavior

```razor
<WorkflowSurfaceBehavior Tree="tree" IsEnabled="true"
                         ScrollViewerId="wf-scroll" CanvasId="wf-canvas"
                         GridSpacing="40" GridColor="#2A2D2E" Background="#0B1120">
    <GridDecorator Context="vp"><WorkflowGridDecorator Viewport="vp" …/></GridDecorator>
    <Minimap Context="vp"><WorkflowMinimapOverlay Viewport="vp" …/></Minimap>
    <ChildContent Context="sc">
        <svg class="wf-links-svg" width="@sc.Width" height="@sc.Height">…links…</svg>
        <ViewPool ItemsSource="tree.Nodes" KeySelector="n => n">…node templates…</ViewPool>
    </ChildContent>
</WorkflowSurfaceBehavior>
```

| Parameter | Default | Notes |
|-----------|---------|-------|
| `Tree` | — | The `IWorkflowTreeViewModel`. |
| `IsEnabled` | `false` | Enables pan/scroll tracking. Must be set to `true` explicitly (matches every other adapter). |
| `ScrollViewerId` / `CanvasId` | `veloxdev-wf-scroll` / `veloxdev-wf-canvas` | Element ids used by the JS gesture layer. |
| `GridDecorator` / `Minimap` | — | `RenderFragment<SurfaceViewport>` — the decorator/minimap components. |
| `ChildContent` | — | `RenderFragment<SurfaceCanvas>` — nodes, links, slots; receives the computed canvas size. |
| `Background` / `GridColor` / `GridSpacing` | `#0B1120` / `#2A2D2E` / `40` | Canvas styling. |

**Behavior:** middle-mouse / space+left / left-on-blank panning; scroll reporting that updates
`Helper.Viewport` and persists `Layout.ViewportOffset` (survives save/load); **auto-expansion in
all four directions** — right/down grows the canvas, left/up grows it and shifts the content
layer so world coordinates stay put. Call `scrollToPosition(scrollerId, x, y)` from JS after
loading to restore a saved viewport.

---

## WorkflowNodeDragBehavior

```razor
<WorkflowSlotLayoutBehavior Node="c">
    <WorkflowNodeDragBehavior Node="c" Style="@($"width:{c.Size.Width:F0}px;height:{c.Size.Height:F0}px;")">
        <div style="position:relative;width:100%;height:100%;">
            <WorkflowControllerView Controller="c" />
            …slot overlays…
        </div>
    </WorkflowNodeDragBehavior>
</WorkflowSlotLayoutBehavior>
```

| Parameter | Default | Notes |
|-----------|---------|-------|
| `Node` | — | The node to position and move. |
| `IsEnabled` | `false` | |
| `Style` | — | Extra styles (width/height); position/`left`/`top`/`z-index` are derived from `Node.Anchor`. |
| `ChildContent` | — | The node content. |

---

## WorkflowSlotConnectionBehavior

```razor
<div class="slot-overlay slot-output-overlay">
    <WorkflowSlotConnectionBehavior Slot="c.OutputSlot" Tree="tree">
        <WorkflowSlotView Slot="c.OutputSlot" IsOutput="true" />
    </WorkflowSlotConnectionBehavior>
</div>
```

| Parameter | Default | Notes |
|-----------|---------|-------|
| `Slot` | — | The slot that initiates/receives connections. |
| `Tree` | — | The owning tree (for `SetPointer`/`ResetVirtualLink`). |
| `IsEnabled` | `false` | |
| `Style` / `ChildContent` | — | Wrapper styling / the slot visual. |

The wrapper carries `data-veloxdev-slot-id="…"`, which is how JS resolves the drop target. On
press the source slot's position is measured on demand (so the virtual link starts at the slot
even if it was never laid out), then `SendConnectionCommand` runs; moves feed `SetPointerCommand`;
release on another slot runs `ReceiveConnectionCommand`.

---

## WorkflowSlotLayoutBehavior

| Parameter | Default | Notes |
|-----------|---------|-------|
| `Node` | — | The node whose slots are measured. |
| `IsEnabled` | `false` | |
| `CoordinateHostId` | — | Reserved for coordinate-host parity with the XAML adapters. |
| `ChildContent` | — | The node content (slots included). |

Measures **every** `[data-veloxdev-slot-id]` descendant automatically (no `SlotNames` list to
maintain), writes `slot.Anchor` in canvas/world coordinates, and re-measures when the node is
dragged — live during the drag — so links drawn from slot anchors follow in real time.

---

## ViewPool

| Parameter | Default | Notes |
|-----------|---------|-------|
| `ItemsSource` | — | Any `IEnumerable`; re-renders when it implements `INotifyCollectionChanged`. |
| `ItemTemplate` | — | `RenderFragment<object>` per item. |
| `EmptyContent` | — | Rendered when the source is empty. |
| `KeySelector` | — | Stabilizes re-renders (analogue of `@key`). |

---

## WorkflowGridDecorator

Built-in ruler component (the XAML adapters only ship the `IWorkflowGridDecorator` interface;
the control lives in the demos).

| Parameter | Default |
|-----------|---------|
| `Viewport` (`SurfaceViewport`) | — |
| `RulerThickness` / `Spacing` | `28` / `40` |
| `RulerBackground` / `TickColor` / `LabelColor` / `DividerColor` | `rgba(37,37,38,0.78)` / `#555555` / `#888888` / `#3A3D40` |

---

## WorkflowMinimapOverlay

Built-in minimap. Drag pans the surface; the viewport rect is clamped inside the minimap, and
dragging past an edge expands the canvas through a shared surface registry.

| Parameter | Default |
|-----------|---------|
| `Viewport` (`SurfaceViewport`) | — |
| `Width` / `Height` | `180` / `120` |
| `Background` / `BorderColor` | `#D2141922` / `#DC94A3B8` |
| `NodeFill` / `NodeRadius` | `#DC38BDF8` / `2` |
| `ViewportFill` / `ViewportStroke` / `ViewportStrokeWidth` | `rgba(255,255,255,0.15)` (= `#28FFFFFF`, like the WPF/Avalonia/MAUI/WinUI adapters) / `#F0FFFFFF` / `1.5` |
| `Padding` / `ScrollViewerId` | `6` / — |

---

## WorkflowCanvasTransformBehavior

Static helper (no component): `GetOffset(tree)`, `ToCss(offset)`, `GetTransformStyle(tree)`
produce the CSS `translate(...)` string for a workflow tree's `Layout.ActualOffset`. The surface
itself implements left/up expansion with a content layer, so consumers typically don't need to
apply it manually.

---

## Context records

```csharp
public sealed record SurfaceViewport(
    IWorkflowTreeViewModel Tree, double ScrollLeft, double ScrollTop,
    double ViewportWidth, double ViewportHeight,
    double ContentOffsetX, double ContentOffsetY);

public sealed record SurfaceCanvas(double Width, double Height);
```

`SurfaceViewport` is the context passed to the `GridDecorator` and `Minimap` fragments;
`SurfaceCanvas` is passed to `ChildContent`.

---

## Example (from the Blazor demo)

See `Examples/Workflow/Blazor/Demo/Demo/Components/Pages/Workflow.razor` for the complete usage —
an SVG link layer, a `ViewPool` of node templates that `@switch` on node type, and per-node
`WorkflowSlotLayoutBehavior` + `WorkflowNodeDragBehavior` + `WorkflowSlotConnectionBehavior`
wrappers, all inside one `WorkflowSurfaceBehavior`.
