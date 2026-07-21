# WorkflowNodeDragBehavior & WorkflowSlotLayoutBehavior

Node cards use two behaviors for drag-move and slot position sync.

---

## WorkflowNodeDragBehavior

### Attached Properties

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `IsEnabled` | `bool` | `false` | Enable drag |
| `CoordinateHostName` | `string` | `null` | Coordinate reference element name (usually Canvas) |
| `CoordinateHostType` | `Type` | `null` | Coordinate reference element type |

### Behavior

- `PreviewMouseLeftButtonDown`: record drag start position
- `PreviewMouseMove`: calculate offset, call `MoveCommand.Execute(offset)`
- `PreviewMouseLeftButtonUp`: end drag

### XAML Usage

```xml
<UserControl behaviors:WorkflowNodeDragBehavior.IsEnabled="True"
             behaviors:WorkflowNodeDragBehavior.CoordinateHostName="PART_Canvas">
</UserControl>
```

## WorkflowSlotLayoutBehavior

Responds to Slots collection changes, automatically syncing slot anchor positions to Canvas coordinates.

### Attached Properties

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `IsEnabled` | `bool` | `false` | Enable layout sync |
| `SlotNames` | `string` | `null` | Semicolon-separated slot property names |
| `SlotEnumeratorNames` | `string` | `null` | Semicolon-separated SlotEnumerator property names |
| `CoordinateHostName` | `string` | `null` | Coordinate reference element name |
| `CoordinateHostType` | `Type` | `null` | Coordinate reference element type |
