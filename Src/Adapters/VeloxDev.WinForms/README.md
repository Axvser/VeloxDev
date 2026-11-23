# VeloxDev.WinForms — Adapter API

Package: `VeloxDev.WinForms` · Targets: `netframework4.6.1;net5.0-windows;netcoreapp3.0;net8.0-windows`
· Namespaces: `VeloxDev.WorkflowSystem.AttachedBehaviors` (the workflow surface),
`VeloxDev.WorkflowSystem.Platform` (Win32/DPI helpers).

The visual backend is **Easel**. The `Easel` submodule supplies the dependency-property runtime, the
Roslyn generator (`Easel.Generator`), and a WPF/Avalonia-shaped control library whose controls live in
`Easel.Visuals.Controls` and derive from `Easel.Visuals.VisualElement`. This adapter adds the workflow
surface, the transition engine, and the animation bridge over that runtime.
A `VisualHost` is the only WinForms `Control` in the tree: it owns one HWND and paints, lays out,
hit-tests and routes input for the whole tree. The minimap and info HUD are ordinary visual elements
inside that tree, not HWND siblings. The workflow surface is an attached behavior on that host.

## Type resolution and markup

`Easel.Controls` holds only shared primitives: `Thickness`, `GridLength`,
`RowDefinition`, `ColumnDefinition`, the layout enums (`Visibility`, `HorizontalAlignment`,
`VerticalAlignment`, `DockPlacement`, `GridUnitType`, `Stretch`, `TextAlignment`), internal
`XamlDrawing`, and `Shapes.SvgPathParser`.

XAML is written **unprefixed**:

| Written | Resolves to |
| --- | --- |
| `Foo` | `Easel.Visuals.Controls.Foo`, then `Easel.Visuals.Controls.Shapes.Foo`, then `Easel.Visuals.Foo`, then the primitives in `Easel.Controls` |
| `local:Foo` | a type in your own `clr-namespace` |
| `native:Foo` / `System.Windows.Forms` | rejected; a visual parent only accepts `VisualElement` children |
| `v:Foo` | accepted as a legacy alias for unprefixed `Foo`; not required |

An unprefixed name that resolves to no visual type is `EASEL_XAML019`; write `local:` for your own
type. A view root must derive from `VisualElement`, and its code-behind calls `InitializeComponent()`.

Supported forms: unprefixed controls, attached properties (`Grid.Row="1"` or `<Grid.Row>1</Grid.Row>`),
`Canvas.Left`, `local:`, `x:Name`, CLR events, `Resources`, `<Style>` (implicit by `TargetType` or keyed, with
direct or wrapped `Setter`/`Trigger` elements), `<ControlTemplate>`, `<DataTemplate>` as an inline item template
**and** as a keyed resource, `<ItemsPanelTemplate>` plus `<ItemsControl.ItemsPanel>`/`ItemsPresenter`,
selector resources (`local:Selector x:Key=... Template="{StaticResource ...}"`), `VisualTemplateSelector`,
bindings (`VisualBindingOperations`), `{StaticResource}`/`{DynamicResource}`, `VisualTrigger` setters, shapes,
`ItemsControl.ItemTemplate` and `ItemsControl.ItemTemplateSelector`.

## Visual backend

`VisualHost.Root` is a `ContainerVisual`. A `VisualElement` root is measured, arranged, hit-tested and
painted in one pass; a plain `ContainerVisual` root is painted only. Nothing in the tree creates a
second window.

- **Properties.** `VisualElement` derives from `WinformsObject` and implements `IWinformsObject`.
  Properties use the same WPF naming as the rest of the family: `DependencyProperty.Register(...,
  new PropertyMetadata(default) { AffectsMeasure/AffectsArrange/AffectsRender/Inherits = ... })`,
  `GetValue`/`SetValue`/`ClearValue`, attached properties via `RegisterAttached`. Values carry a
  priority layer (`Animation`, `LocalValue`, `StyleTrigger`, `Template`, `Style`, `Inherited`).
- **Bindings.** `element.SetBinding(Element.PropProperty, new Binding("Name") { Source = vm })`
  supports `OneWay`/`OneTime`/`TwoWay`, `DataContext` paths, converters, `StringFormat`,
  `FallbackValue` and `TargetNullValue`; `RelativeSource.Self`, `TemplateBinding`
  (`RelativeSource.TemplatedParent`) and `FindAncestor` resolve too. `ElementName` searches the nearest
  `VisualNameScope` first (so an item template never binds to another item's part), then ancestor names, then —
  outside templates — the whole tree. `GetObservable`/`Bind` push an `IObservable` into a layer. Validation
  rules are not part of this layer.
- **Resources and styles.** `VisualElement.Resources` and `VisualHost.Resources` are ordinary
  `ResourceDictionary` stores; `VisualResourceLookup.Get<T>` walks the element, its ancestors and the
  surface. Generated `{StaticResource}` values are anchored on the target element and resolve as soon as its
  ancestor chain is complete, so a dictionary on any ancestor or on the surface works; `{DynamicResource}`
  keeps listening to those dictionaries and updates on add, replace and remove. `VisualStyle` /
  `VisualSetter` / `VisualTrigger` come from code or from `Styles` collections. A style is a description,
  not a live object: mutating an applied style's `Setters` does not re-apply it. A setter may carry a
  `Binding` or a `TargetName`; `TargetName` resolves through the applied control template.
- **Templates.** `ContentControl` shows one `Content` or a `VisualControlTemplate`; the template's root
  reports `TemplatedParent`, named parts resolve through `GetTemplateChild<T>`, and
  `ContentPresenter` receives the content. `VisualStyle.Template` applies through a style;
  `ContentControl.Template` wins over it. Template triggers write at the `Template` layer and may
  target a named part with `TargetName`.
- **Controls.** `Border`, `StackPanel`, `Grid`, `DockPanel`, `Canvas`, `WrapPanel`, `UniformGrid`,
  `Viewbox`, `Decorator`, `HeaderedContentControl`, `GridSplitter`, `GroupBox`, `Expander`,
  `TabControl`, `TextBlock`, `TextBox`, `RichTextBox`, `Image`, `Button`, `RepeatButton`,
  `ToggleButton`, `CheckBox`, `RadioButton`, `Slider`, `ProgressBar`, `ScrollViewer`, `ItemsControl`,
  `ListBox`, `VirtualList`, `TreeView`, `ComboBox`, `Calendar`, `DatePicker`, `ContentControl`,
  `ContentPresenter`, `VisualMenu`/`VisualContextMenu`, and the `Shapes` elements
  (`Rectangle`/`Ellipse`/`Line`/`Polygon`/`Polyline`/`Path`); none of them creates a child window.
  `Button`, `RepeatButton`, `ToggleButton`, `CheckBox` and `RadioButton` share `ButtonBase`'s
  `Command`/`CommandParameter` dependency properties: activation executes the command when
  `CanExecute` allows it, and a refusing command disables the control until it allows again.
  `VisualPresenter` hosts a lightweight `ContainerVisual` tree inside the control tree.
- **Overlay and popups.** `VisualHost` paints and hit-tests an overlay layer after the root tree.
  `VisualPopup` moves its child into that layer and is therefore not clipped by an ancestor
  `ScrollViewer`; a press outside the popup and its placement target closes it, and Escape closes it
  from the focused element. `ComboBox` uses this for its drop-down and keeps logical focus on itself.
- **Native compatibility.** `NativeControlHost` hosts a real WinForms control as a child HWND above
  the surface. WinForms child windows always paint above the visual tree, so the island must stay
  rectangular and axis-aligned; it cannot be transformed, blended or clipped with its visual parent,
  and an overlay popup cannot cover it. The visual `TextBox` uses a private one only while focused, to
  supply IME composition and accessibility; it is not a general XAML control path.
- **Input.** Pointer, wheel, keyboard and text input are routed from the single surface to the
  deepest hit element; focus is logical and `FocusElement`/`Tab` move it. Document-level input rules
  are handled by `VisualHost`.
- **Visual XAML.** Ordinary unprefixed names (`UserControl`, `Canvas`, `Grid`, `Border`, `Viewbox`,
  `ItemsControl`, `DataTemplate`, `ItemsPanelTemplate`, `ItemsPresenter`, `Path`) resolve to the visual backend.
  Generated code assigns a single control to `Content` for `UserControl`/`ContentControl`/`ScrollViewer`, and
  `Add`s it elsewhere; a type with an `Items` collection writes `Items.Add`. `x:Name` becomes a field and
  `VisualElement.Name`; a name inside a `ControlTemplate`, `DataTemplate`/`ItemTemplate`/`ContentTemplate` or
  `ItemsPanelTemplate` registers into that instance's `VisualNameScope` instead. A `{StaticResource}` on a
  child resolves against the child's own chain, not against the view root only. `ItemsControl.ItemsPanel`
  replaces the default vertical stack; an `ItemsPresenter` (or a panel marked with
  `ItemsControl.IsItemsHost`) declared in the content serves as the host. `ItemsControl.ItemTemplateSelector`
  picks a `DataTemplate` per item and wins over `ItemTemplate`.
- **Triggers.** `VisualTrigger` applies `Setters` at `BindingPriority.StyleTrigger`; style triggers
  and template triggers work. `EnterAction` and `ExitAction` run once on activation and once on
  deactivation; `TransitionAction` and `KeyFrameAnimation` animate one dependency property through
  the transition system, and a custom action derives `Visuals.VisualTriggerAction`. A template
  trigger's actions receive the same `TargetName` part as its setters. `DataTrigger` and `MultiTrigger`
  are not supported on the visual backend and are reported as `VELOX_XAML004` instead of being skipped.
- **Theme.** `Theme.CreateDark()` / `CreateLight()` palettes follow the VS Code workbench / Scribalix
  tokens (window `#141414`, panels `#252526`, inputs `#2A2A2A`, borders `#333333`, text `#E7E7E7`,
  muted `#A0A0A0`, accent `#0078D4`, selection `#094771`; Light+ keeps `#F3F3F3` panels and
  `#ADD6FF` selection, 4 px radius, 28 px controls). Switching `Theme.Mode` re-renders the visual
  backend; call `Application.SetColorMode(...)` before creating native islands so they follow.

```csharp
var surface = new VisualHost { Dock = DockStyle.Fill };
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

### Animation

Markup has `TransitionAction` (one segment) and `KeyFrameAnimation` (a keyed chain) inside
`<Trigger.EnterAction>` / `<Trigger.ExitAction>`; `TriggerActionGroup` runs several actions in
order. The bridge is `Visuals.VisualTransition.Animate(target, property, value, effect)` or
`AnimateKeyFrames(...)`; `VisualTransition.Stop(target)` stops the animation where it stands. Frames
write at `BindingPriority.Animation`, and a new animation on the same element supersedes the
previous one. Code-behind can keep using `VeloxDev.TransitionSystem.Transition<T>` on the visual
element's DP-backed CLR wrappers.

### DPI

`VisualHost` lays out in logical DIPs and paints at `DeviceDpi/96`. Camera, pan, model anchors and
decoration offsets are logical; physical client sizes, the ruler, the minimap and scroll-bar metrics
are converted through `DpiScale`. `DpiScaleOverride` pins the scale for tests.

## Workflow surface

The workflow surface is an attached behavior on a `VisualHost`, not an adapter-provided control. The
tree template is a visual `UserControl` with named parts; the behavior resolves them from
`VisualHost.Root` by `VisualElement.Name`:

```xml
<local:TreeView x:Class="MyApp.Views.TreeView">
  <local:GridDecorator x:Name="PART_GridDecorator">
    <ScrollViewer x:Name="PART_ScrollViewer" IsScrollInputEnabled="False">
      <Canvas x:Name="PART_Canvas" UnboundedHitTest="True"
              xmlns:behaviors="clr-namespace:VeloxDev.WorkflowSystem.AttachedBehaviors;assembly=VeloxDev.WinForms"
              behaviors:ViewPool.ItemsSource="{Binding Helper.VisibleItems}"
              behaviors:ViewPool.TemplateSelector="{StaticResource WorkflowTemplateSelector}" />
    </ScrollViewer>
  </local:GridDecorator>
</local:TreeView>
```

```csharp
var treeView = new TreeView();
var host = new VisualHost { Dock = DockStyle.Fill, Root = treeView };
WorkflowSurfaceBehavior.SetCanvasName(host, "PART_Canvas");
WorkflowSurfaceBehavior.SetGridDecoratorName(host, "PART_GridDecorator");
WorkflowSurfaceBehavior.SetScrollViewerName(host, "PART_ScrollViewer");
WorkflowSurfaceBehavior.SetMinimapOverlayName(host, "PART_MinimapOverlay");
WorkflowSurfaceBehavior.SetPointerPressSourceName(host, "PART_SurfaceBorder");
// The tree ships an empty LinkContextMenu; the host supplies the entries.
WorkflowSurfaceBehavior.SetLinkMenuBuilder(host, (menu, link) =>
    menu.Items.Add("Delete", null, (_, _) => link.DeleteCommand.Execute(null)));
// The tree declares its selector as x:Key="WorkflowTemplateSelector"; the surface resolves that key by default.
WorkflowSurfaceBehavior.SetIsEnabled(host, true);
WorkflowSurfaceBehavior.SetWorkflowTree(host, tree);
```

- `WorkflowSurfaceBehavior` owns the camera: it writes one matrix to `PART_Canvas.Transform`, routes pointer/wheel/key input to Core, converts the pointer through the inverse matrix, divides drag deltas by zoom, and pushes grid/minimap/viewport offsets. Vertical wheel, Shift+wheel and `WM_MOUSEHWHEEL` all reach the same `IWorkflowSurfaceScroller` with the raw 120-delta. Its public camera API is `GetZoom`, `GetPan`, `SetPan`, `SurfaceToWorld`, `WorldToSurface`, `GetWorldViewport`, `SetWorldViewport`, `ZoomAt`, `ProcessWheel` and `Refresh`.
- `ViewPool` is a generic, workflow-free attached behavior on `PART_Canvas`. It rebuilds a Core `ViewSourcePipeline` (the shared view pool) when `ItemsSource`/`TemplateSelector` changes or `Refresh` runs; views are recycled by item type and rebound. A new view is parented before its `DataContext` is set, so `ElementName`/ancestor bindings resolve; a link (`Panel.ZIndex=-1`) is attached below the node cards. Bind `ItemsSource` to `Helper.VisibleItems`; a `VisualTemplateSelector` chooses the view.
- Node/slot/link templates are visual `UserControl`s. Node is `Viewbox > Grid 260×180` with a title, `PART_InputSlot` and `PART_OutputSlots` (`ItemsControl` + `DataTemplate`); slot is `Viewbox > Path`; every link is a `LinkView` that publishes the curve Core hit-tests. The tree's `DataTemplate`s bind `Width`/`Height`/`Canvas.Left`/`Canvas.Top`/`Panel.ZIndex` and the link endpoints.
- Slot measurement, title-row drag and slot connection are separate `VisualElement` attached behaviors: `WorkflowSlotLayoutBehavior`, `WorkflowNodeDragBehavior`, `WorkflowSlotConnectionBehavior`. The surface calls `Ensure` on each visual element; a behavior with no `IsEnabled` does nothing.
- The item-template `SlotView` code-behind is only `InitializeComponent()`; `PART_Glyph.Fill` binds the model `State` through `SlotStateBrushConverter`.
- `WorkflowMinimapOverlay` is a `VisualElement` implementing `IWorkflowMinimapOverlay` and `IWorkflowMinimapScrollSource`; `WorkflowInfoOverlay` is an in-tree HUD. Both are placed beside `PART_SurfaceBorder` in the tree's root `Grid`, so they share one HWND, one camera and one input route. The surface pushes scroll/content/viewport offsets and zoom to them; dragging the minimap raises `ViewportScrollRequested`.
- `CustomTemplateSelector : VisualTemplateSelector` returns `VisualElement` instances (`NodeTemplate?.Create()`, etc.). `VisualTemplateSelector` is the item → view seam.
- The shell (inspector, menus, status bar) stays ordinary WinForms; only the surface is visual.
- Tree templates put `PART_SurfaceBorder`, `PART_GridDecorator`, `PART_ScrollViewer`, `PART_Canvas`, `PART_MinimapOverlay` and `PART_InfoOverlay` in one visual `Grid`, structurally the same as the WPF tree. `PointerPressSourceName` names the visual part that blank-canvas panning starts from; no HWND difference remains.
- Runnable samples: `Examples/Workflow/WinForms` and `Examples/Workflow/WinForms Trimmed`; the visual catalogue is [Easel's gallery](../../../Easel/Examples/XamlGallery/README.md).

## Behaviors

| Behavior | Attach to | Purpose |
| --- | --- | --- |
| `WorkflowSurfaceBehavior` | A `VisualHost` | Resolves `PART_*` visual parts; owns the camera matrix, input routing, scroll/viewport push and the in-tree minimap/HUD. |
| `ViewPool` | A visual `Canvas` (`PART_Canvas`) | One Core-pooled visual child per item; recycling, re-binding and z-order on attach. |
| `WorkflowSlotLayoutBehavior` | A node view root | Measures named slot parts and writes `slot.Anchor`. |
| `WorkflowNodeDragBehavior` | A card's title row | Turns a press into a node drag measured in the coordinate host. |
| `WorkflowSlotConnectionBehavior` | A slot view root | Press starts a connection; release over a receiver completes it. |

`WorkflowSurfaceBehavior` API:

| Member | Notes |
| --- | --- |
| `Get/SetIsEnabled` | Master switch. |
| `Get/SetZoomEnabled` | Ctrl + wheel zoom. |
| `Get/SetScrollViewerName` · `Get/SetCanvasName` · `Get/SetGridDecoratorName` · `Get/SetMinimapOverlayName` · `Get/SetPointerPressSourceName` | Part names resolved from `VisualHost.Root` by `VisualElement.Name`; `PointerPressSourceName` names the part blank-canvas panning starts from. |
| `Get/SetVisualTemplateSelector` · `Get/SetVisualTemplateSelectorKey` | Item → `VisualElement` selector, or the resource key the surface resolves (default `WorkflowTemplateSelector`). |
| `Get/SetWorkflowTree(Control host, ITreeComponent?)` | Explicit tree binding. |
| `Get/SetLinkMenuKey` | Resource key of the visual `VisualContextMenu` opened on a right-pressed link; the menu's `DataContext` is the link. |
| `Get/SetLinkMenuBuilder` | Legacy WinForms `ContextMenuStrip` fallback when no visual menu key resolves. |
| `Refresh(Control host)` | Re-push viewport/offsets and persist the model viewport after a wholesale change. |
| `GetZoom` · `GetPan` · `SetPan` · `SurfaceToWorld` · `WorldToSurface` · `GetWorldViewport` · `SetWorldViewport` · `ZoomAt` · `ProcessWheel` | The camera and world/surface conversions. |

The minimap is restyled by deriving `WorkflowMinimapOverlay` and setting its palette; the HUD is `WorkflowInfoOverlay`. Both draw in the same camera surface; neither owns a window.

## Compatibility

- Runtime assemblies target `netframework4.6.1`, `netcoreapp3.0`, `net5.0-windows` and `net8.0-windows`; .NET Framework 4.8 uses the `net461` asset.
- Item-template source and generated XAML code require **C# 10** (`<LangVersion>latest</LangVersion>`): the templates use file-scoped namespaces and nullable annotations. A default .NET Framework project with C# 7.3 will not compile them.
- `Src/Test/Compatibility/VeloxDev.WinForms.Net48.Smoke` is the runtime smoke for .NET Framework 4.8; it opens a real window, renders a XAML view, focuses the native TextBox and exits.
- Live per-monitor DPI changes are available on the modern TFMs; the .NET Framework path falls back to the `Graphics` DPI at paint time and needs a DPI-aware application manifest for crisp output.
