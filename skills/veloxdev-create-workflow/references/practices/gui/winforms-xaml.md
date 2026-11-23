# WinForms XAML runtime — complete reference

`VeloxDev.WinForms` does not use a platform XAML. It ships one visual runtime in
`VeloxDev.WinForms.Xaml`: the control library lives in `VeloxDev.WinForms.Xaml.Visuals.Controls`
(controls derive from `VisualElement`), shapes in `.Visuals.Controls.Shapes`, shared layout primitives
in `VeloxDev.WinForms.Xaml.Controls`, and the source generator in
`VeloxDev.WinForms.Generator`. `VisualHost` is the only WinForms `Control`; it owns the single HWND
and paints, lays out, hit-tests and routes input for the whole tree. Read this page before writing
markup, a custom control, a style/trigger, or an animation on WinForms.
[winforms.md](winforms.md) covers the workflow-specific surface.

**Targets:** `netframework4.6.1`, `net5.0-windows`, `netcoreapp3.0`, `net8.0-windows`. On old TFMs
the adapter uses its own `Xaml/Compat.cs` shim (`IsExternalInit`,
`UnconditionalSuppressMessageAttribute`, `Clamp`, `IsFinite`, `TickCount64`).

## 1. Prefix and type resolution

| Written in XAML | Resolves to |
|---|---|
| `Foo` (no prefix) | `Xaml.Visuals.Controls.Foo`, then `.Visuals.Controls.Shapes.Foo`, then `.Visuals.Foo`, then the shared primitives in `.Xaml.Controls` |
| `local:Foo` | your own `clr-namespace` |
| `native:Foo` | rejected: a visual parent only accepts `VisualElement` children |
| `v:Foo` | legacy alias for unprefixed `Foo`; still accepted, not required |

An unprefixed name that is not a visual type is `VELOX_XAML019`; write `local:Foo`. The root follows
the same rule and must derive from `VisualElement`.

The usual namespaces:

```xml
xmlns:local="clr-namespace:MyApp.Views"
xmlns:behaviors="clr-namespace:VeloxDev.WorkflowSystem.AttachedBehaviors;assembly=VeloxDev.WinForms"
```

## 2. Root and code-behind

A XAML view is a `partial` class whose base is a visual control:

```csharp
public sealed partial class MyView : VeloxDev.WinForms.Xaml.Visuals.Controls.UserControl
{
    public MyView() => InitializeComponent();
}
```

Use the full namespace for the base type. The generator emits `InitializeComponent()` as a private
ordinary method, not a designer partial.

## 3. Controls

Every control derives from `VisualElement`, so its properties are dependency properties and styles,
triggers and bindings work. None of them creates a child window.

| Family | Types |
|---|---|
| Layout | `UserControl`, `ContentControl`, `Border`, `Panel`, `StackPanel`, `DockPanel`, `WrapPanel`, `Grid`, `Canvas`, `UniformGrid`, `Viewbox`, `Decorator`, `ScrollViewer`, `ScrollBar`, `ScrollContentPresenter`, `GridSplitter`, `GroupBox`, `Expander`, `TabControl` |
| Items | `ItemsControl`, `ListBox`, `VirtualList`, `TreeView`, `ComboBox`, `Calendar`, `DatePicker`, `ContentPresenter` |
| Text | `TextBlock`, `TextBox`, `RichTextBox`, `Image` |
| Selection / range | `Button`, `RepeatButton`, `ToggleButton`, `CheckBox`, `RadioButton`, `Slider`, `ProgressBar` |
| Shapes | `Rectangle`, `Ellipse`, `Line`, `Polygon`, `Polyline`, `Path` |
| Overlay / misc | `VisualPopup`, `VisualMenu`, `VisualContextMenu`, `VisualPresenter` |

`TextBox` uses `Text`; its editing, caret, selection and clipboard are visual, and a private
`NativeControlHost`-hosted WinForms `TextBox` exists only while it has focus (IME, accessibility).

## 4. Dependency properties and layout

Every `VisualElement` has these DPs:

- `Width`, `Height`, `MinWidth`, `MinHeight`, `MaxWidth`, `MaxHeight`
- `Margin`, `Padding` (`Thickness`)
- `Background` (`Brush`), `CornerRadius`
- `Visibility`, `IsEnabled`, `HorizontalAlignment`, `VerticalAlignment`

`Visual` adds `Bounds` (parent coordinates), `Transform`, `Opacity`, `Clip` and `Effect`.

Attached layout properties:

```xml
<StackPanel Spacing="4">
  <Button Text="OK" />
  <TextBlock>text bodies work too</TextBlock>
</StackPanel>

<Grid>
  <Grid.RowDefinitions>
    <RowDefinition Height="Auto" />
    <RowDefinition Height="*" />
  </Grid.RowDefinitions>
  <Border Grid.Row="1" />
</Grid>

<Canvas>
  <Border Canvas.Left="10" Canvas.Top="20" Width="80" Height="40" />
</Canvas>

<DockPanel>
  <Border DockPanel.Dock="Top" Height="36" />
  <Border />
</DockPanel>
```

`GridLength` literals are `Auto`, `*`, `2*` and pixel counts. `Grid` also supports
`RowSpan` / `ColumnSpan`; `Canvas` also supports `Right` / `Bottom`.

Measurement passes the available width down to `TextBlock` and `ContentControl`, so wrapping text
reports its wrapped height. `Border` measures its single child plus `Padding`, so a card needs no
fixed `Height`. Layout invalidation is metadata-driven: `PropertyMetadata.AffectsMeasure` /
`AffectsArrange` / `AffectsRender` on a DP drives measure/arrange/invalidate; do not call
`Parent.PerformLayout()` from a callback.

## 5. Bindings and resources

```xml
<TextBlock Text="{Binding Title}" />
<TextBox Text="{Binding Name, Mode=TwoWay}" />
<ContentControl Content="{Binding Selected}" Template="{StaticResource CardTemplate}" />
<Border Background="{StaticResource Accent}" />
```

- Forms: `{Binding Path}`, `{Binding Path, Mode=OneWay|TwoWay|OneTime}`. The path supports dotted
  properties and simple indexers.
- Source: `Source`, inherited `DataContext`; `RelativeSource.Self` and `TemplateBinding`
  (`RelativeSource.TemplatedParent`) resolve. `ElementName` and validation rules are not part of the
  visual backend.
- Value shaping: `Converter`, `ConverterParameter`, `StringFormat`, `FallbackValue`,
  `TargetNullValue`.
- `DataContext` is inherited through visual parents. `Bind`/`GetObservable` push an observable into
  a priority layer.
- `{StaticResource Key}` resolves at construction through the element, its ancestors and
  `VisualHost.Resources`; declare the key first. `{DynamicResource}` is diagnosed — the visual
  backend has no dynamic-resource lookup.
- A converter implements `IValueConverter` (or `IParameterizedValueConverter` for a parameter).

Resources live in a `ResourceDictionary`:

```xml
<UserControl.Resources>
  <ResourceDictionary>
    <DataTemplate x:Key="CardTemplate">
      <local:CardView />
    </DataTemplate>
    <Style x:Key="CardStyle" TargetType="Border">
      <Style.Setters>
        <Setter Property="Background" Value="#FF20242C" />
      </Style.Setters>
    </Style>
  </ResourceDictionary>
</UserControl.Resources>
```

A keyed `<DataTemplate>` resource is compiled into a `Visuals.DataTemplate` factory. A `{StaticResource}`
on a child element is anchored on the view root, because generated code assigns attributes before
parenting.

## 6. Styles and triggers

```xml
<Style x:Key="CardStyle" TargetType="Border">
  <Style.Setters>
    <Setter Property="Background" Value="#FF20242C" />
    <Setter Property="CornerRadius" Value="6" />
  </Style.Setters>
  <Style.Triggers>
    <Trigger Property="IsPointerOver" Value="True">
      <Trigger.Setters>
        <Setter Property="BorderBrush" Value="#FF5B8CFF" />
      </Trigger.Setters>
    </Trigger>
  </Style.Triggers>
</Style>
```

- A style setter targets a dependency property. Setters write the `Style` layer and triggers write
  `StyleTrigger`; a local value still wins.
- `Setter.Value` accepts a literal, `{StaticResource Key}` or a `{Binding ...}`.
- An implicit style has `TargetType` and no `x:Key`; `Style.BasedOn` merges a base style.
- `Trigger` watches one DP; `DataTrigger` watches a binding path; `MultiTrigger` requires every
  `Condition`.
- A style is a description, not a live object: mutating an applied style's `Setters` does not
  re-apply it.
- `Style.Template` applies a `ControlTemplate` while the style is active. A template with triggers
  declares `TargetType`; a trigger setter that targets a named part declares `TargetName`, and the
  part's own type as `TargetType` when the property belongs to the part.

## 7. Animations

Markup trigger actions animate one dependency property:

```xml
<Trigger Property="IsEnabled" Value="False">
  <Trigger.EnterAction>
    <TransitionAction Property="Width" To="120" Duration="0:0:0.2" Ease="CubicOut" />
  </Trigger.EnterAction>
  <Trigger.ExitAction>
    <KeyFrameAnimation Property="Width" Duration="0:0:0.3" Ease="Linear">
      <KeyFrame Offset="0" Value="120" />
      <KeyFrame Offset="1" Value="40" />
    </KeyFrameAnimation>
  </Trigger.ExitAction>
</Trigger>
```

`TransitionAction`, `KeyFrameAnimation` and `TriggerActionGroup` are the built-in actions; a custom
action derives `VeloxDev.WinForms.Xaml.Visuals.VisualTriggerAction` and overrides
`Invoke(VisualElement target)`. A template trigger passes the same `TargetName` part to setters and
actions. The code bridge is `VisualTransition.Animate` / `AnimateKeyFrames` / `Stop`.

Code-behind can also animate the visual element directly with
`VeloxDev.TransitionSystem.Transition<T>` on its CLR property wrappers:

```csharp
using VeloxDev.TransitionSystem;

Transition<VeloxDev.WinForms.Xaml.Visuals.VisualElement>.Create()
    .Property(v => v.Margin, new VeloxDev.WinForms.Xaml.Controls.Thickness(0, 8, 0, 0))
    .Property(v => v.CornerRadius, 8f)
    .Effect(new TransitionEffect
    {
        Duration = TimeSpan.FromMilliseconds(180),
        Ease = Eases.Cubic.Out,
    })
    .Execute(element);
```

- `T` is the object the animation writes into. For a visual element use `VisualElement` (or the
  concrete control) and name a DP-backed CLR property (`Margin`, `Padding`, `Width`, `Height`,
  `CornerRadius`, `Background`, or your own `[XamlProperty]`).
- A code-behind transition writes the local priority layer each frame; a style or trigger value is
  overridden while it runs. Markup actions write `BindingPriority.Animation`. A DP-backed setter
  invalidates through `AffectsMeasure`/`AffectsArrange`/`AffectsRender`; the plain
  `Visual.Opacity`/`Transform`/`Clip`/`Effect` do not invalidate, so feed opacity through a DP that
  marks `AffectsRender`.
- A declaration is immutable and reusable; cache a fixed-endpoint one in `static readonly` and build
  a computed one at the call site. A new animation on the same element supersedes the previous one;
  `VisualTransition.Stop(element)` ends it where it stands.
- Samplers cover `double`, `float`, `int`, `long`, `Color`, `Point`, `PointF`, `Size`, `SizeF`,
  `Rectangle`, `RectangleF`, `Vector2/3/4`, `Quaternion`, `Padding`, `Thickness`. `decimal`, `bool`,
  `string` and enums have no sampler and are skipped.
- The transition scheduler is the WinForms adapter's; frames are posted through
  `Control.BeginInvoke`, so start the first animation on the UI thread.

## 8. Templates and selectors

`DataTemplate` is a factory that creates one visual tree:

```xml
<DataTemplate x:Key="NodeTemplate">
  <local:NodeView />
</DataTemplate>
```

A selector derives from `VisualTemplateSelector`, not from any workflow-specific base:

```csharp
public sealed class CustomTemplateSelector : VisualTemplateSelector
{
    public override VisualElement? SelectTemplate(object? item) => item switch
    {
        ILinkComponent => new LinkView(),
        ISlotComponent => new SlotView(),
        INodeComponent => new NodeView(),
        _ => null,
    };
}
```

Assign it to the surface (`WorkflowSurfaceBehavior.SetVisualTemplateSelector(host, selector)`), or
declare it as an empty object resource and point the attached property at it.

### 8.1 ControlTemplate

- The template root reports `TemplatedParent`; `{TemplateBinding Property}` binds a template child to
  it.
- `x:Name` inside the template registers in the per-instance `VisualNameScope`; the templated control
  reads a part with `GetTemplateChild<T>("PART_...")`.
- `ControlTemplate.Triggers` evaluate against the templated control and write the `Template` layer; a
  setter may name a part with `TargetName`.

### 8.1.1 ContentTemplate

`ContentControl.ContentTemplate` takes a `<DataTemplate>` for **non-visual** content: the template
creates the visual and the raw `Content` becomes its `DataContext`. A `VisualElement` content is shown
as-is and ignores `ContentTemplate`. `ContentPresenter` resolves its own `ContentTemplate` first, then
the templated parent's; a plain string without either still becomes a `TextBlock`.

`ContentTemplateSelector` (a `DataTemplateSelector` resource) picks a template from the raw content; it
wins over `ContentTemplate` at the same level, and a `null` result falls through. `ContentStringFormat`
formats the text projection with the current culture and is ignored when a template supplies the visual.

```xml
<ContentControl Content="{Binding SelectedRow}"
                ContentTemplateSelector="{StaticResource RowPicker}"
                ContentStringFormat="{}{0:N0}" />
```

```xml
<Border>
  <ContentControl Content="{Binding SelectedRow}" ContentTemplate="{StaticResource RowTemplate}" />
</Border>
```

### 8.1.2 ScrollViewer and ScrollBar templates

- `ScrollViewer` is a `ContentControl`; its default chrome is a `Grid` with the presenter in the star
  cell and one `ScrollBar` in each Auto row/column. The bars occupy viewport space (WPF layout), so the
  presenter's viewport is smaller than the viewer by the bar thickness on a visible axis. A custom
  `Template` replaces the chrome; parts are `PART_ScrollContentPresenter`, `PART_HorizontalScrollBar`
  and `PART_VerticalScrollBar`.
- `ScrollBar` exposes `Orientation`, `Minimum`, `Maximum`, `Value`, `ViewportSize`, `SmallChange` and
  `LargeChange`; `Value` is expressed in content pixels by default. It has its own optional `Template`
  with `PART_Track`, `PART_Thumb`, `PART_DecreaseRepeatButton` and `PART_IncreaseRepeatButton`. Without
  a thumb part it draws the default rounded thumb.
- Bars start dragging from the thumb or the track (a track press pages once and continues as the same
  drag); holding a track/repeat button pages again after ~400 ms, then every ~50 ms.
- `ScrollViewer.CanContentScroll` delegates extent/offset/wheel/page steps to the content when it
  implements `IScrollInfo`, so an item panel can report items instead of pixels. The built-in
  `ScrollContentPresenter` implements the same interface in pixels; `VirtualList` still scrolls by
  pixels.
- A camera host can set `IsExternalScroll` and push offsets with `SetExternalOffset`; the presenter
  then reports extent/viewport without moving the content, and bar gestures raise
  `ScrollOffsetRequested`. `SetExternalOffset` should use the presenter viewport (excluding bars), not
  the whole client area, when deriving the scroll range.

### 8.2 ItemsControl and item templates

`ItemsControl.ItemTemplate` takes a `<DataTemplate>`; each realized item gets its source item as
`DataContext`, so `DataContext="{Binding Slot}"` works. The generator compiles the template body into
a factory. `Virtualizing` + `ItemHeight` supports fixed-row virtualized lists inside a
`ScrollViewer`; variable heights are not supported. `ListBox` and `VirtualList` build on
`ItemsControl`.

### 8.3 Lightweight visual tree

For non-interactive content that needs composition, `VisualHost` hosts a `Visual` tree painted in one
pass with no child window per node:

```csharp
var host = new VisualHost { Dock = DockStyle.Fill };
var group = new ContainerVisual { Bounds = new RectangleF(20, 20, 400, 200), Opacity = 0.7 };
group.Add(new ShapeVisual { Bounds = new RectangleF(0, 0, 80, 80), Geometry = new EllipseGeometry(), Fill = new SolidColorBrush(Color.White) });
group.Transform.RotateAt(8f, new PointF(200, 100));
host.Root.Add(group);
```

- `Visual.Bounds` is parent coordinates; content paints in `0..Width/Height`.
- `Transform`, `Opacity`, `Clip` and `Effect` apply to the subtree; group opacity and effects render
  through a temporary bitmap.
- `VisualHost.HitTestVisual(point)` walks the visual tree. Derive from `Visual` and override
  `RenderContent` / `HitTestContent` for new content.
- `VisualPresenter` hosts a lightweight `ContainerVisual` tree inside the control tree.

### 8.4 Native islands and DPI

- `NativeControlHost` hosts a real WinForms control as a child HWND above the surface. It must stay
  rectangular and axis-aligned; it cannot be transformed, blended or clipped with its visual parent,
  and an overlay popup cannot cover it.
- `VisualHost` routes pointer, wheel, keyboard and text input to the deepest hit element; focus is
  logical.
- `VisualHost` lays out in logical DIPs and paints at `DeviceDpi/96`. The workflow surface keeps the
  camera in logical DIPs and converts physical client/decoration values with `DpiScale`.
  `DpiScaleOverride` pins the scale for tests.

## 9. Events and input

- Visual controls expose `PointerPressed`, `PointerMoved`, `PointerReleased`, `PointerEntered`,
  `PointerExited`, `PointerWheel`, `KeyDown` and `KeyUp`. Events bubble along `ParentElement`.
- `IsPointerOver` follows the subtree: hovering a child keeps every visual ancestor's hover state
  true.
- `x:Name` creates a code-behind field and writes `VisualElement.Name`; workflow parts resolve that
  name, so a template writes `x:Name="PART_*"` once. An explicit `Name="..."` still overwrites it.
- Focus is logical; `FocusElement` keeps native focus on the `VisualHost`, and Tab moves it.

## 10. Theme

`Theme` holds the default palette. `Theme.Mode` swaps between `Light` and `Dark`; `Theme.Current`
can be replaced with a custom `ThemePalette`. The built-in dark palette follows the VS Code
workbench / Scribalix tokens (window `#141414`, panels `#252526`, inputs `#2A2A2A`, borders
`#333333`, text `#E7E7E7`, muted `#A0A0A0`, accent `#0078D4`, selection `#094771`); Light+ keeps
`#FFFFFF`/`#F3F3F3` surfaces and `#ADD6FF` selection. 4px corners, 28px control height. Visual
controls read `Theme.Current` at render time; switching `Theme.Mode` re-renders the surface. Call
`Application.SetColorMode(...)` before creating native islands so they follow the same mode.

## 11. Limits and diagnostics

Not in this subset:

- `ElementName`, validation rules, `DynamicResource`
- designer and hot reload
- a full WPF measure/arrange engine
- several properties in one markup action (use `TriggerActionGroup` with one action per property)
- variable-height virtualization
- per-keyframe easing
- transforming/clipping/blending a `NativeControlHost`

The generator reports `VELOX_XAML001–019` and `VELOX_XAMLP001–005`. The most useful ones here:

| ID | Meaning |
|---|---|
| `VELOX_XAML001` | unknown type |
| `VELOX_XAML002` | unknown property or event (error) |
| `VELOX_XAML003` | bad literal / ease / duration |
| `VELOX_XAML004` | unsupported construct |
| `VELOX_XAML013` | unknown attached property |
| `VELOX_XAML014` | invalid `Style.TargetType` |
| `VELOX_XAML019` | unprefixed name is not a visual type; write `local:` |

## 12. Workflow mapping

For the workflow surface, read [winforms.md](winforms.md). The XAML pieces it uses are:

- `WorkflowSurfaceBehavior` on the `VisualHost`, part names in `VisualElement.Name`; it owns the camera
  (one matrix on `PART_Canvas`) and routes input to Core
- `ViewPool` on `PART_Canvas` (`ItemsSource="{Binding Helper.VisibleItems}"`, `TemplateSelector`) for
  item materialization
- a visual `NodeView` with slot parts, a visual `SlotView` with a `Path`, and a visual `LinkView`
  that draws and publishes its curve
- a `CustomTemplateSelector : VisualTemplateSelector` with `NodeTemplate` / `SlotTemplate` / `LinkTemplate`
- a tree resource `x:Key="WorkflowTemplateSelector"`; `WorkflowSurfaceBehavior.VisualTemplateSelectorKey`
  defaults to that key and resolves it from the tree root's resources (then the host's)
- `SetLinkMenuBuilder` in code-behind for link menu entries

The node template uses the WPF-shaped attached behaviors; `WorkflowSurfaceBehavior` calls `Ensure` on
every visual element, and a behavior with no `IsEnabled` does nothing:

```xml
<local:NodeView xmlns:behaviors="clr-namespace:VeloxDev.WorkflowSystem.AttachedBehaviors;assembly=VeloxDev.WinForms"
                behaviors:WorkflowSlotLayoutBehavior.IsEnabled="True"
                behaviors:WorkflowSlotLayoutBehavior.SlotNames="PART_InputSlot"
                behaviors:WorkflowSlotLayoutBehavior.SlotEnumeratorNames="PART_OutputSlots"
                behaviors:WorkflowSlotLayoutBehavior.CoordinateHostName="PART_Canvas"
                ClipToBounds="False">
  <Grid>
    <Grid behaviors:WorkflowNodeDragBehavior.IsEnabled="True"
          behaviors:WorkflowNodeDragBehavior.CoordinateHostName="PART_Canvas" />
    <local:SlotView x:Name="PART_InputSlot" />
    <ItemsControl x:Name="PART_OutputSlots" />
  </Grid>
</local:NodeView>
```

- `SlotNames` / `SlotEnumeratorNames` are comma-separated **visual element names** (`x:Name`); the
  default is `PART_InputSlot` / `PART_OutputSlots`.
- When a card marks one element with `WorkflowNodeDragBehavior.IsEnabled`, only that element (and its
  descendants) starts a drag; when no element sets it, the card does not drag.
- When a slot marks `WorkflowSlotConnectionBehavior.IsEnabled="True"`, it starts and completes a
  connection; when no slot sets it, the slot does not connect.
- The slot view root carries `behaviors:WorkflowSlotConnectionBehavior.IsEnabled="True"`. Its code-behind
  is only `InitializeComponent()`; `PART_Glyph.Fill` binds to the slot's model `State` through
  `SlotStateBrushConverter`.

The tree template mirrors WPF's `PART_SurfaceBorder` → `PART_GridDecorator` → `PART_ScrollViewer` →
`PART_Canvas` shape:

```xml
<Border x:Name="PART_SurfaceBorder" Padding="0"
        Background="#1E1E1E" BorderBrush="#33FFFFFF" BorderThickness="1" CornerRadius="0">
  <local:GridDecorator x:Name="PART_GridDecorator"
                       GridSpacing="40" MajorLineEvery="5" RulerThickness="28"
                       DrawSurface="False" ClipToBounds="False">
    <ScrollViewer x:Name="PART_ScrollViewer" IsScrollInputEnabled="False">
      <Canvas x:Name="PART_Canvas" UnboundedHitTest="True" ClipToBounds="False"
              behaviors:ViewPool.ItemsSource="{Binding Helper.VisibleItems}"
              behaviors:ViewPool.TemplateSelector="{StaticResource WorkflowTemplateSelector}" />
    </ScrollViewer>
  </local:GridDecorator>
</Border>
```

Resources declare the views and selector:

```xml
<local:TreeView.Resources>
  <DataTemplate x:Key="NodeTemplate"><local:NodeView /></DataTemplate>
  <DataTemplate x:Key="LinkTemplate"><local:LinkView /></DataTemplate>
  <DataTemplate x:Key="SlotTemplate"><local:SlotView /></DataTemplate>
  <local:CustomTemplateSelector x:Key="WorkflowTemplateSelector"
                                NodeTemplate="{StaticResource NodeTemplate}"
                                SlotTemplate="{StaticResource SlotTemplate}"
                                LinkTemplate="{StaticResource LinkTemplate}" />
</local:TreeView.Resources>
```

The minimap/HUD remain HWND overlays outside the visual tree; `PointerPressSourceName` exists only
for WPF parity — the whole `VisualHost` routes input — and the in-tree `MinimapOverlay` from the WPF
tree has no visual-only equivalent.

## 13. Verification

```bash
dotnet test Src/Test/Unit/WinFormsXaml/VeloxDev.WinForms.Xaml.Test/VeloxDev.WinForms.Xaml.Test.csproj
dotnet test Src/Test/Headless/VeloxDev.WinForms.Headless.Tests/VeloxDev.WinForms.Headless.Tests.csproj
powershell -NoProfile -ExecutionPolicy Bypass -File Src/Test/Harness/verify-workflow-item-templates-all.ps1 -Platform WinForms -Strict
```

The runtime tests need a real control handle and a pumped message queue: transition frames are
posted through `Control.BeginInvoke`. The template harness generates and builds all seven items, but
it cannot prove drawing; run the Trimmed demo for that.

`Examples/XamlGallery` is the full feature catalogue. Build it or run it with `--smoke` after
changing runtime features; `--capture`, `--entry`, `--group`, `--scroll`, `--light`, `--dump` drive
and capture one card.
