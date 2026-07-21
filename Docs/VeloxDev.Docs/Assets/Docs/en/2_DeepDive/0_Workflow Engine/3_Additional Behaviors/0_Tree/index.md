# WorkflowSurfaceBehavior

Host-level behavior providing **canvas pan, zoom, grid snap, and minimap** for the UserControl.

---

## Attached Properties

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `IsEnabled` | `bool` | `false` | Enable/disable all surface behaviors |
| `ScrollViewerName` | `string` | `null` | ScrollViewer element name (required) |
| `CanvasName` | `string` | `null` | Canvas element name (required) |
| `GridDecoratorName` | `string` | `null` | `IWorkflowGridDecorator` element name |
| `MinimapOverlayName` | `string` | `null` | `IWorkflowMinimapOverlay` element name |
| `PointerPressSourceName` | `string` | `null` | Background click source for pan start |

## Features

| Feature | Trigger | Description |
|---------|---------|-------------|
| Pan | Drag on blank area | Updates ScrollViewer offset |
| Zoom | Ctrl + Mouse wheel | Scales Canvas ScaleTransform |
| Grid | Set `GridDecoratorName` | Delegates to `IWorkflowGridDecorator` |
| Minimap | Set `MinimapOverlayName` | Delegates to `IWorkflowMinimapOverlay` |

## XAML Usage

```xml
<UserControl xmlns:behaviors="clr-namespace:VeloxDev.WorkflowSystem.AttachedBehaviors"
             behaviors:WorkflowSurfaceBehavior.IsEnabled="True"
             behaviors:WorkflowSurfaceBehavior.ScrollViewerName="PART_ScrollViewer"
             behaviors:WorkflowSurfaceBehavior.CanvasName="PART_Canvas"
             behaviors:WorkflowSurfaceBehavior.GridDecoratorName="PART_GridDecorator">
    <ScrollViewer x:Name="PART_ScrollViewer">
        <Canvas x:Name="PART_Canvas" />
    </ScrollViewer>
</UserControl>
```

## Programmatic Refresh

```csharp
WorkflowSurfaceBehavior.Refresh(hostUserControl);
```
