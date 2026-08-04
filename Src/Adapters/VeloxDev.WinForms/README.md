# VeloxDev.WinForms — Adapter API

Package: `VeloxDev.WinForms` · Namespace: `VeloxDev.WorkflowSystem.AttachedBehaviors`

WinForms has no attached-property system and no data binding. This adapter mirrors the exact
attached-property surface of the WPF/Avalonia/WinUI/MAUI adapters using **static `Get`/`Set`
methods** that store per-control state in a `ConditionalWeakTable<Control, State>`. The API shape
is therefore: attach behavior `X` to a control by calling `WorkflowXxxBehavior.SetYyy(control, value)`.

```
WorkflowBehaviors.WorkflowSurfaceBehavior.SetIsEnabled(myCanvas, true);
WorkflowBehaviors.WorkflowSurfaceBehavior.SetWorkflowTree(myCanvas, tree);
```

---

## Behaviors

| Behavior | Attach to | Purpose |
|----------|-----------|---------|
| [`WorkflowSurfaceBehavior`](#workflowsurfacebehavior) | The host canvas (`Panel`/`ScrollableControl`) | Scroll/viewport bookkeeping, pan trigger, pushes offsets into decorator & minimap |
| [`WorkflowNodeDragBehavior`](#workflownodedragbehavior) | A node card | Translates drag deltas into `IWorkflowNodeViewModel.MoveCommand` |
| [`WorkflowSlotConnectionBehavior`](#workflowslotconnectionbehavior) | A slot control | Drives the `SendConnection`/`SetPointer`/`ReceiveConnection` gesture |
| [`WorkflowSlotLayoutBehavior`](#workflowslotlayoutbehavior) | A node host | Measures slot controls and writes each `slot.Anchor` in canvas coordinates |
| [`ViewPool`](#viewpool) | A container | Materializes pooled views from an `INotifyCollectionChanged` source |
| [`WorkflowCanvasTransformBehavior`](#workflowcanvastransformbehavior) | The host canvas | Publishes the content translate offset for self-drawn canvases |

Interfaces your controls implement to receive data pushed by the surface:
`IWorkflowGridDecorator`, `IWorkflowMinimapOverlay`.

---

## WorkflowSurfaceBehavior

```csharp
WorkflowBehaviors.WorkflowSurfaceBehavior.SetIsEnabled(canvas, true);
WorkflowBehaviors.WorkflowSurfaceBehavior.SetScrollViewerName(canvas, "PART_Scroll");
WorkflowBehaviors.WorkflowSurfaceBehavior.SetCanvasName(canvas, "PART_Canvas");
WorkflowBehaviors.WorkflowSurfaceBehavior.SetGridDecoratorName(canvas, "PART_Grid");
WorkflowBehaviors.WorkflowSurfaceBehavior.SetMinimapOverlayName(canvas, "PART_Minimap");
WorkflowBehaviors.WorkflowSurfaceBehavior.SetPointerPressSourceName(canvas, "PART_Surface");
WorkflowBehaviors.WorkflowSurfaceBehavior.SetWorkflowTree(canvas, tree);
```

| API | Notes |
|-----|-------|
| `Get/SetIsEnabled(Control)` | Master switch. |
| `Get/SetScrollViewerName` · `Get/SetCanvasName` | Name-based element wiring (searched in the host subtree). |
| `Get/SetGridDecoratorName` · `Get/SetMinimapOverlayName` | Named control should implement `IWorkflowGridDecorator`/`IWorkflowMinimapOverlay`; offsets are pushed on every refresh. |
| `Get/SetPointerPressSourceName` | The control that starts canvas panning on a blank-press. |
| `Get/SetWorkflowTree(Control, IWorkflowTreeViewModel?)` | Explicit tree binding (the WinForms analogue of setting `DataContext`). |
| `Refresh(Control host)` | **Manual pull**: updates the tree viewport, pushes scroll/content offsets into the decorator & minimap, applies the canvas transform, then `PerformLayout` + `Invalidate`. |

**Tree resolution order:** `SetWorkflowTree` → ancestor walk reading `ViewModel`, `DataContext`,
`BindingContext`, or `Tag`.

**Scroll resolution:** a `ScrollableControl` with `AutoScroll` is read via `AutoScrollPosition`;
otherwise the persisted `Layout.ViewportOffset` is used.

> Because WinForms has no change-notification pipeline, **you call `Refresh(host)` after each
> mutation** (node moved, tree loaded, session changed). This is the deliberate pull model.

---

## WorkflowNodeDragBehavior

```csharp
WorkflowBehaviors.WorkflowNodeDragBehavior.SetIsEnabled(nodeCard, true);
WorkflowBehaviors.WorkflowNodeDragBehavior.SetCoordinateHostType(nodeCard, typeof(Panel));
```

| API | Notes |
|-----|-------|
| `Get/SetIsEnabled(Control)` | |
| `Get/SetCoordinateHostName(Control, string?)` | Optional named coordinate host. |
| `Get/SetCoordinateHostType(Control, Type?)` | Coordinate host by type (default `Panel`). |

The behavior **recursively hooks the control tree** and keeps hooks in sync via
`ControlAdded`/`ControlRemoved`. It uses `Control.Capture` while dragging, converts deltas into
the coordinate host's client space, and executes `MoveCommand`. `TextBoxBase`, `ComboBox`,
`ButtonBase`, `CheckBox`, and slot controls are excluded as drag handles.

**Node resolution:** the control's `Tag` (`IWorkflowNodeViewModel`) or a
`ViewModel`/`DataContext`/`BindingContext` property on itself or an ancestor.

---

## WorkflowSlotConnectionBehavior

```csharp
WorkflowBehaviors.WorkflowSlotConnectionBehavior.SetIsEnabled(slotControl, true);
```

| API | Notes |
|-----|-------|
| `Get/SetIsEnabled(Control)` | |

On left-press the behavior starts a connection: it executes the slot's `SendConnectionCommand`,
then installs a **global `IMessageFilter`** that observes `WM_MOUSEMOVE`/`WM_LBUTTONUP` — the
WinForms equivalent of `Mouse.Capture`/document-level listeners. Releasing over another enabled
slot resolves the target and executes its `ReceiveConnectionCommand`; releasing on blank space
resets the virtual link. The gesture is cancelled if the sender is disposed.

**Slot resolution:** the control's `Tag` (`IWorkflowSlotViewModel`) or a
`ViewModel`/`DataContext`/`BindingContext` property.

---

## WorkflowSlotLayoutBehavior

```csharp
WorkflowBehaviors.WorkflowSlotLayoutBehavior.SetIsEnabled(nodeHost, true);
WorkflowBehaviors.WorkflowSlotLayoutBehavior.SetSlotNames(nodeHost, "PART_Input,PART_Output");
WorkflowBehaviors.WorkflowSlotLayoutBehavior.SetSlotEnumeratorNames(nodeHost, "PART_Slots");
WorkflowBehaviors.WorkflowSlotLayoutBehavior.SetCoordinateHostType(nodeHost, typeof(Panel));
```

| API | Notes |
|-----|-------|
| `Get/SetIsEnabled(Control)` | |
| `Get/SetSlotNames` | Comma-separated named slot controls to measure. |
| `Get/SetSlotEnumeratorNames` | Named controls exposing a slot enumerator (`SlotEnumerator<TSlot>`) — every item slot is measured. |
| `Get/SetCoordinateHostName` · `Get/SetCoordinateHostType` | Coordinate host for canvas-space math. |
| `Get/SetParentHostName` | Optional host whose layout/offset is read. |
| `Get/SetLayoutPropertyName` · `Get/SetActualOffsetPropertyName` | Reflection property names used to resolve the layout and its `ActualOffset` (defaults `Layout` / `ActualOffset`). |
| `SlotPropertyNames` (internal) | The set of node property names that trigger a re-sync — seeded with `Anchor`, `Size`, `InputSlot`, the configured `SlotNames`, and each `SlotEnumeratorNames` member's `Slot` property. |

The behavior measures slot controls in the coordinate host's space and writes `slot.Anchor`.
It re-syncs on `Layout`/`SizeChanged`/`ControlAdded`/`ControlRemoved` and — critically — on the
node's **property changes**, so slot anchors (and the links drawn from them) track a live node
drag. Syncs are throttled with a pending flag.

---

## ViewPool

```csharp
WorkflowBehaviors.ViewPool.SetItemsSource(container, tree.Nodes);
WorkflowBehaviors.ViewPool.SetTemplateSelector(container, selector);
```

| API | Notes |
|-----|-------|
| `Get/SetItemsSource(Control, INotifyCollectionChanged?)` | The pooled collection. |
| `Get/SetTemplateSelector(Control, IWorkflowTemplateSelector?)` | Creates a view per item type. |

When both are set, a `ViewManager` starts: it creates a `Control` per item via the selector,
**recycles pooled views per item type** (a `Queue<Control>`), applies the item as context, and
keeps them in sync with collection add/remove/reset/replace. Set either to `null` to stop.

---

## WorkflowCanvasTransformBehavior

```csharp
var offset = WorkflowBehaviors.WorkflowCanvasTransformBehavior.GetTransform(canvas);
```

| API | Notes |
|-----|-------|
| `Get/SetTransform(Control, Offset?)` | Stores/clears the translate offset. |

`WorkflowSurfaceBehavior.Refresh` writes the content offset here every cycle. It is a
**notification carrier only** — a self-drawn canvas reads `GetTransform` in `OnPaint` and
translates its drawing origin, mirroring how XAML node/link views bind their `RenderTransform`.

---

## Example (from the WinForms demo)

`WorkflowCanvas` (host) + `WorkflowNodeCard` (node) wire up like this:

```csharp
// Host canvas: surface + decorator/minimap offsets + explicit tree binding.
WorkflowBehaviors.WorkflowSurfaceBehavior.SetScrollViewerName(this, nameof(WorkflowCanvas));
WorkflowBehaviors.WorkflowSurfaceBehavior.SetCanvasName(this, nameof(WorkflowCanvas));
WorkflowBehaviors.WorkflowSurfaceBehavior.SetGridDecoratorName(this, nameof(WorkflowCanvas));
WorkflowBehaviors.WorkflowSurfaceBehavior.SetPointerPressSourceName(this, nameof(WorkflowCanvas));
WorkflowBehaviors.WorkflowSurfaceBehavior.SetIsEnabled(this, true);
WorkflowBehaviors.WorkflowSurfaceBehavior.SetWorkflowTree(this, session.Tree);

// After every mutation:
WorkflowBehaviors.WorkflowSurfaceBehavior.Refresh(this);

// Node card: drag + slot layout + slot connection.
WorkflowBehaviors.WorkflowNodeDragBehavior.SetIsEnabled(this, true);
WorkflowBehaviors.WorkflowNodeDragBehavior.SetCoordinateHostType(this, typeof(Panel));
WorkflowBehaviors.WorkflowSlotLayoutBehavior.SetIsEnabled(this, true);
WorkflowBehaviors.WorkflowSlotLayoutBehavior.SetCoordinateHostType(this, typeof(Panel));
WorkflowBehaviors.WorkflowSlotConnectionBehavior.SetIsEnabled(slotControl, true);
```

See `Examples/Workflow/WinForms/Demo/Controls/WorkflowCanvas.cs` and
`WorkflowNodeCard.cs` for the full usage.
