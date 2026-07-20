# Additional Behaviors

Attached behaviors (in `VeloxDev.WorkflowSystem.AttachedBehaviors`) enable interactive canvas operations.

| Behavior | Function |
|----------|----------|
| `WorkflowSurfaceBehavior` | Pan, zoom, grid decorator, minimap |
| Standard commands | Mouse/keyboard wiring for drag-connect, select, move |
| VirtualLink feedback | Ghost line during slot drag-connect |

## Using WorkflowSurfaceBehavior (WPF example)

```xml
<UserControl xmlns:behaviors="clr-namespace:VeloxDev.WorkflowSystem.AttachedBehaviors"
             behaviors:WorkflowSurfaceBehavior.IsEnabled="True"
             behaviors:WorkflowSurfaceBehavior.ScrollViewerName="PART_ScrollViewer"
             behaviors:WorkflowSurfaceBehavior.CanvasName="PART_Canvas"
             behaviors:WorkflowSurfaceBehavior.GridDecoratorName="PART_GridDecorator">
    <!-- ... -->
</UserControl>
```
