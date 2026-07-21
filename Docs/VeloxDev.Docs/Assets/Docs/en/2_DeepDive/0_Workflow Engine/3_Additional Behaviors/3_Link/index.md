# WorkflowCanvasTransformBehavior & WorkflowMinimapOverlay

---

## WorkflowCanvasTransformBehavior

Canvas transform notification carrier — broadcasts the transform computed by `WorkflowSurfaceBehavior` via an attached property, so node and link views can bind `RenderTransform` in XAML.

### Attached Properties

| Property | Type | Description |
|----------|------|-------------|
| `Transform` | `Transform` | Current canvas transform |

### XAML Binding

```xml
<Canvas x:Name="PART_Canvas"
        RenderTransform="{Binding (behaviors:WorkflowCanvasTransformBehavior.Transform), RelativeSource={RelativeSource Self}}" />
```

This behavior **should not be applied to the host** — only to the internal Canvas panel. `OnTransformChanged` is intentionally empty: the property serves as a notification carrier only.

## WorkflowMinimapOverlay

Minimap overview control that renders a thumbnail of all nodes and the visible viewport.

### Dependency Properties

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `WorkflowTree` | `IWorkflowTreeViewModel` | `null` | Bound workflow tree |
| `ScrollOffsetX/Y` | `double` | `0` | Scroll offset (bind to ScrollViewer) |
| `ViewportWidth/Height` | `double` | `1` | Viewport size |
| `MinimapWidth/Height` | `double` | `200/140` | Minimap size |
| `IsMinimapVisible` | `bool` | `true` | Visibility |
| `RulerThickness` | `double` | `28` | Ruler thickness |
| `LinkStrokeThickness` | `double` | `2` | Link line thickness |
| `MinimapBackground` | `Brush` | dark | Background brush |
| `MinimapBorderBrush` | `Brush` | blue | Border brush |

### Interaction

Only the viewport indicator rectangle supports drag navigation.
