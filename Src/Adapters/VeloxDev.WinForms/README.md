# VeloxDev.WinForms — Adapter API

Package: `VeloxDev.WinForms` · Namespace: `VeloxDev.WorkflowSystem.AttachedBehaviors`

The adapter has two surfaces. The **XAML layer** ships a dependency-property runtime, a Roslyn
generator and a WPF/Avalonia-shaped control library (see [XAML control library](#xaml-control-library)).
The **code-first behaviours** keep the old shape: static `Get`/`Set` methods that store per-control
state in a `ConditionalWeakTable<Control, State>`; attach behaviour `X` to a control by calling
`WorkflowXxxBehavior.SetYyy(control, value)`.

## XAML control library

Targets `netframework4.6.1;net5.0-windows;netcoreapp3.0;net8.0-windows`. The older TFMs use the
small shim in `Xaml/Compat.cs`; the XAML surface is the same on all four.

The XAML layer lives in `VeloxDev.WinForms.Xaml` (runtime) and `VeloxDev.WinForms.Xaml.Controls`
(controls and layout primitives); vector shapes are in `VeloxDev.WinForms.Xaml.Controls.Shapes`.
Every control derives from the XAML `Control`, so its properties are dependency properties and the
style, trigger, transition and binding pipeline applies.

| Family | Types |
| --- | --- |
| Layout | `Panel`, `StackPanel`, `DockPanel`, `WrapPanel`, `Grid`, `Canvas`, `Border`, `ScrollViewer`, `ContentControl`, `ItemsControl` |
| Text | `TextBlock`, `Label`, `TextBox`, `PasswordBox` |
| Selection | `Button`, `RepeatButton`, `ToggleButton`, `CheckBox`, `RadioButton`, `ListBox`, `ComboBox` |
| Range / media | `ProgressBar`, `Slider`, `Image`, `Separator` |
| Shapes | `Rectangle`, `Ellipse`, `Line`, `Path` |

View selection is the generic `VeloxDev.WinForms.Xaml.TemplateSelector` (`object?` → `DataTemplate?`), not a workflow-specific base; derive it, expose the templates your host supports, and hand it to `WorkflowSurfaceBehavior.TemplateSelector` or `ViewPool.SetTemplateSelector`.

Prefix rules are strict: **an unprefixed type is a VeloxDev type** (`Controls`, then
`Controls.Shapes`, then `.Xaml`), `native:` means `System.Windows.Forms`, and `local:` means a
template/Demo type. Declare `xmlns:native="clr-namespace:System.Windows.Forms;assembly=System.Windows.Forms"`
in files that use native controls. An unprefixed name that is not a VeloxDev type is
`VELOX_XAML019`; write `native:` or `local:`. `v:` is still accepted as a legacy alias for VeloxDev.
Code-behind names its XAML base with the full namespace, for example
`public sealed partial class TreeView : VeloxDev.WinForms.Xaml.Controls.UserControl`.

Attached coordinates are unprefixed too: `Canvas.Left`, `Grid.Row`,
`DockPanel.Dock`. `Width`, `Height`, `MinWidth`, `MinHeight`, `MaxWidth`, `MaxHeight`, `Margin`,
`Padding`, `Background`, `Foreground`, `Font`, `Visibility`, `IsEnabled`, `HorizontalAlignment`
and `VerticalAlignment` are dependency properties on every XAML control; native controls can still
receive attached values but have no XAML property change pipeline.

```csharp
WorkflowBehaviors.WorkflowSurfaceBehavior.SetIsEnabled(myCanvas, true);
WorkflowBehaviors.WorkflowSurfaceBehavior.SetWorkflowTree(myCanvas, tree);
```

---

## Visual backend (one HWND)

`VeloxDev.WinForms.Xaml.Visuals` is an opt-in, HWND-free control tree. A `VisualSurface` is the only
WinForms `Control`; `VisualSurface.Root` is a `VisualElement` tree that is measured, arranged,
hit-tested and painted in one pass. It is not a replacement for the HWND XAML library above: both
backends coexist and share the built-in `Theme`.

- **Properties.** `VisualElement` derives from `WinformsObject` and implements `IWinformsObject`.
  Properties use the same WPF naming as the HWND layer: `DependencyProperty.Register(...,
  new PropertyMetadata(default) { AffectsMeasure/AffectsArrange/AffectsRender/Inherits = ... })`,
  `GetValue`/`SetValue`/`ClearValue`, attached properties via `RegisterAttached`. Values carry a
  priority layer (`Animation`, `LocalValue`, `StyleTrigger`, `Template`, `Style`, `Inherited`).
- **Bindings.** `element.SetBinding(Element.PropProperty, new Binding("Name") { Source = vm })`
  supports `OneWay`/`OneTime`/`TwoWay`, `DataContext` paths, converters, `StringFormat`,
  `FallbackValue` and `TargetNullValue`; `RelativeSource.Self` and `TemplateBinding`
  (`RelativeSource.TemplatedParent`) resolve too. `GetObservable`/`Bind` push an `IObservable` into a
  layer. `ElementName` and validation rules are not part of this layer.
- **Resources and styles.** `VisualElement.Resources` and `VisualSurface.Resources` are ordinary
  `ResourceDictionary` stores; `VisualResourceLookup.Get<T>` walks the element, its ancestors and the
  surface. `VisualStyle` / `VisualSetter` / `VisualTrigger` still come from code or from the `Styles`
  collections.
- **Templates.** `ContentControl` shows one `Content` or a `VisualControlTemplate`; the template's root
  reports `TemplatedParent`, named parts resolve through `GetTemplateChild<T>`, and
  `ContentPresenter` receives the content. `VisualStyle.Template` applies through a style;
  `ContentControl.Template` wins over it. Template triggers write at the `Template` layer and may
  target a named part with `TargetName`.
- **Controls.** `Border`, `StackPanel`, `Grid`, `DockPanel`, `Canvas`, `WrapPanel`, `TextBlock`,
  `Image`, `Button`, `ToggleButton`, `CheckBox`, `RadioButton`, `TextBox`, `ScrollViewer`,
  `ItemsControl`, `ListBox`, `VirtualList`, `ContentControl`, `ContentPresenter`, `ComboBox`; none of
  them creates a child window.
- **Overlay and popups.** `VisualSurface` paints and hit-tests an overlay layer after the root tree.
  `VisualPopup` moves its child into that layer and is therefore not clipped by an ancestor
  `ScrollViewer`; a press outside the popup and its placement target closes it, and Escape closes it
  from the focused element. `ComboBox` uses this for its drop-down and keeps logical focus on itself.
- **Native compatibility.** `NativeControlHost` hosts a real WinForms control as a child HWND above
  the surface. WinForms child windows always paint above the visual tree, so the island must stay
  rectangular and axis-aligned; it cannot be transformed, blended or clipped with its visual parent,
  and an overlay popup cannot cover it.
- **Input.** Pointer, wheel, keyboard and text input are routed from the single surface to the
  deepest hit element; focus is logical and `FocusElement`/`Tab` move it. Document-level input
  rules still apply: `IsInputKey`/`ProcessDialogKey` are handled by `VisualSurface`.
- **Visual XAML.** Use the `v:` prefix for the experimental visual profile:
  `xmlns:v="clr-namespace:VeloxDev.WinForms.Xaml.Visuals.Controls;assembly=VeloxDev.WinForms"`.
  Unprefixed names keep resolving to the HWND `Xaml.Controls`. Visual markup supports literal
  properties, attached properties, `x:Name`, CLR events, `<Resources>` with scalar resources and
  `<Style>` / `<ControlTemplate>` entries, `Style="{StaticResource Key}"`, and `{Binding}` /
  `{TemplateBinding}` / `{StaticResource}` on dependency properties. `{DynamicResource}` and
  `<DataTemplate>` are diagnosed because they have no visual-backend implementation.
- **Theme.** The built-in `Theme.CreateDark()` / `CreateLight()` palettes follow VS Code Dark+ /
  Light+ (editor and side-bar colours, `#007ACC` accent, `#007FD4`/`#0090F1` focus ring, Dark+
  `#264F78` / Light+ `#ADD6FF` selection, 4 px radius, 28 px controls). Switching `Theme.Mode`
  re-renders the visual backend; call `Application.SetColorMode(...)` before creating native controls
  so the WinForms islands follow the same mode.

```csharp
var surface = new VisualSurface { Dock = DockStyle.Fill };
var root = new Border
{
    Padding = new Thickness(16f),
    Background = new SolidColorBrush(Theme.Current.Surface),
};
root.Child = new StackPanel { Spacing = 8 }
    .Add(new TextBlock { Text = "Hello", Foreground = Theme.Current.Foreground })
    .Add(new Button { Text = "Run" });
surface.Root = root;
```

The runnable sample is [`Examples/VisualWorkbench`](../../../Examples/VisualWorkbench/README.md),
including the secondary-screen capture mode and the Dark/Light toggle.

## XAML animations

A XAML control's dependency properties animate through the WinForms transition system. In markup,
put a `TransitionAction` under a trigger's `EnterAction` / `ExitAction`:

```xml
<Button Content="Save">
  <Button.Triggers>
    <Trigger Property="IsPointerOver" Value="True">
      <Trigger.EnterAction>
        <TransitionAction Property="Background" To="#FF2B6CFF"
                            Duration="0:0:0.18" Ease="CubicOut" />
      </Trigger.EnterAction>
      <Trigger.ExitAction>
        <TransitionAction Property="Background" To="#FF20242C"
                            Duration="0:0:0.24" Ease="CubicOut" />
      </Trigger.ExitAction>
    </Trigger>
  </Button.Triggers>
</Button>
```

For more than two stops, a `KeyFrameAnimation` chains one transition per adjacent frame pair:

```xml
<Trigger.EnterAction>
  <KeyFrameAnimation Property="Background" Duration="0:0:0.6" Ease="CubicInOut">
    <KeyFrame Offset="0" Value="#FF20242C" />
    <KeyFrame Offset="0.5" Value="#FF2B6CFF" />
    <KeyFrame Offset="1" Value="#FF20242C" />
  </KeyFrameAnimation>
</Trigger.EnterAction>
```

From code-behind, call `XamlTransition.Animate(control, property, value, effect)` for one segment or
`XamlTransition.AnimateKeyFrames(control, property, frames, duration, ease)` for a keyed chain; `Stop`
ends the run. A single `TransitionAction`'s default effect (240 ms `Cubic.Out`) applies only when
both `Duration` and `Ease` are omitted. Only XAML `Control` DPs animate; native controls do not. The
full reference — attributes, supported value types, custom actions and limits — is in
`skills/veloxdev-create-workflow/references/practices/gui/winforms-xaml.md`.

## Surface controls

Five controls carry the platform machinery, so a host supplies only the things that are actually its own — a
palette, a glyph, what a card looks like:

| Control | Derive to give it | It owns |
|---|---|---|
| `WorkflowTreeView` | `CreateNodeView` / `CreateLinkView`, the four surface colours | Assembly, grid, floating rulers, signed-pan engine, view pool, layout scheduling |
| `WorkflowNodeView` | the three `OnNodeRebound` / `OnCollapseChanged` / `OnTitleChanged` hooks, the card's paint | Binding, placement, zoom collapse, the reflective title / input-slot / slot-label lookups |
| `WorkflowSlotView` | `SlotPath`, the glyph colours | Binding, painting, state tinting, SVG path parsing |
| `WorkflowLinkView` | `LineColor` / `Thickness` / `SurfaceBackground` | Window-region carving, endpoint subscription, geometry |
| `WorkflowMinimapOverlay` | `MinimapBackground` / `MinimapBorderBrush` / `NodeBrush` / `ViewportStroke` | Top-right anchoring, the layout maths, the drag mapping |

`WorkflowSurfaceColors.Parse` turns the `#RRGGBB` / `#AARRGGBB` text a generated host carries into a colour; each
base also exposes it as a `protected static ParseColor` for its own subclass.

### `WorkflowTreeView`

The assembled workflow surface. Derive from it and supply two factories — everything else is platform machinery
that would be identical in every host.

```csharp
public sealed class TreeView : WorkflowTreeView
{
    public TreeView()
    {
        SurfaceBackground = ParseColor("#1E1E1E");
        SurfaceBorderBrush = ParseColor("#33FFFFFF");
    }

    protected override Control CreateNodeView(INodeComponent node) => new NodeView { ViewModel = node };
    protected override Control CreateLinkView(ILinkComponent link) => new LinkView { ViewModel = link };
}
```

| Member | Notes |
|--------|-------|
| `CreateNodeView` · `CreateLinkView` | **Required.** The card should implement `IWorkflowSurfaceNodeView` so the surface can place it on every pan; a link view needs nothing — its geometry follows its endpoints. |
| `TemplateSelector` | Optional. Returns a `DataTemplate` per item type; the surface still records each view's role from the item it was created for. |
| `ViewModel` · `MinimapOverlay` | The tree, and an optional minimap (`IWorkflowMinimapOverlay`, plus `IWorkflowMinimapScrollSource` to have its viewport drags pan the surface). |
| `SurfaceBackground` · `SurfaceBorderBrush` · `SurfaceBorderThickness` · `SurfaceCornerRadius` | Chrome styling, applied as they are set. |
| `PART_ScrollViewer` · `PART_Canvas` · `PART_GridDecorator` · `PART_MinimapOverlay` | The named parts the surface behaviour resolves. |
| `OnTreeAttached(tree)` · `OnSurfaceRefreshed()` | Optional hooks for host overlays — `OnSurfaceRefreshed` runs after every pan, zoom or layout. |
| `ParseColor("…")` | Static helper for `#RRGGBB` / `#AARRGGBB` / named colours. |

## Behaviors

| Behavior | Attach to | Purpose |
|----------|-----------|---------|
| [`WorkflowSurfaceBehavior`](#workflowsurfacebehavior) | The host canvas (`Panel`/`ScrollableControl`) | Scroll/viewport bookkeeping, pan trigger, pushes offsets into decorator & minimap |
| [`WorkflowNodeDragBehavior`](#workflownodedragbehavior) | A node card | Translates drag deltas into `INodeComponent.MoveCommand` |
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
WorkflowBehaviors.WorkflowSurfaceBehavior.SetScrollViewer(canvas, scrollViewer);
WorkflowBehaviors.WorkflowSurfaceBehavior.SetCanvas(canvas, canvasPanel);
WorkflowBehaviors.WorkflowSurfaceBehavior.SetGridDecorator(canvas, gridDecorator);
WorkflowBehaviors.WorkflowSurfaceBehavior.SetMinimapOverlay(canvas, minimap);
WorkflowBehaviors.WorkflowSurfaceBehavior.SetWorkflowTree(canvas, tree);
```

| API | Notes |
|-----|-------|
| `Get/SetIsEnabled(Control)` | Master switch. |
| `Get/SetScrollViewer` · `Get/SetCanvas` | Element wiring — you hand over the **control itself**, not a name to be looked up in the host subtree. |
| `Get/SetGridDecorator` · `Get/SetMinimapOverlay` | The control should implement `IWorkflowGridDecorator`/`IWorkflowMinimapOverlay`; offsets are pushed on every refresh. |
| `Get/SetWorkflowTree(Control, ITreeComponent?)` | Explicit tree binding (the WinForms analogue of setting `DataContext`). |
| `Refresh(Control host)` | **Manual pull**: updates the tree viewport, pushes scroll/content offsets into the decorator & minimap, applies the canvas transform, then `PerformLayout` + `Invalidate`. |

**Tree resolution order:** `SetWorkflowTree` → ancestor walk reading `ViewModel`, `DataContext`,
`BindingContext`, or `Tag`.

**Scroll resolution:** a `ScrollableControl` with `AutoScroll` is read via `AutoScrollPosition`;
otherwise the persisted `Layout.ViewportOffset` is used. Only a measured offset (one of those two
pan sources) is written back into `Layout.ViewportOffset` — the fallback *is* that value, and
writing it back would subtract `ActualOffset` a second time.

**Viewport restore is automatic.** When a tree is handed over (`SetWorkflowTree`) the surface
scrolls the host back to the tree's saved `Layout.ViewportOffset`, deferred until the handle
exists. You do **not** restore the position yourself.

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

**Node resolution:** the control's `Tag` (`INodeComponent`) or a
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

**Slot resolution:** the control's `Tag` (`ISlotComponent`) or a
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
| `Get/SetTemplateSelector(Control, TemplateSelector?)` | Selects a `DataTemplate` per item type. |

When both are set, a `ViewManager` starts: it creates a `Control` per item from the selected `DataTemplate`,
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
// Host canvas: surface + decorator/minimap wiring + explicit tree binding.
WorkflowBehaviors.WorkflowSurfaceBehavior.SetScrollViewer(this, this);
WorkflowBehaviors.WorkflowSurfaceBehavior.SetCanvas(this, this);
WorkflowBehaviors.WorkflowSurfaceBehavior.SetGridDecorator(this, this);
WorkflowBehaviors.WorkflowSurfaceBehavior.SetMinimapOverlay(this, minimap);
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
