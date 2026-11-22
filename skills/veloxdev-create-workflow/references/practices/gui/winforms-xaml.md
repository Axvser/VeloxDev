# WinForms XAML runtime — complete reference

`VeloxDev.WinForms` does not use a platform XAML. It ships its own small runtime in
`VeloxDev.WinForms.Xaml`, a control library in `VeloxDev.WinForms.Xaml.Controls`, vector shapes in
`VeloxDev.WinForms.Xaml.Controls.Shapes`, and the source generator in `VeloxDev.WinForms.Generator`.
Read this page before writing markup, a custom control, a style/trigger, or an animation on WinForms.
[winforms.md](winforms.md) covers the workflow-specific surface; this page covers the runtime itself.

**Targets:** `netframework4.6.1`, `net5.0-windows`, `netcoreapp3.0`, `net8.0-windows`. The XAML
surface is identical on all four. On old TFMs the adapter uses its own `Xaml/Compat.cs` shim
(`IsExternalInit`, `UnconditionalSuppressMessageAttribute`, `Clamp`, `IsFinite`, `TickCount64`);
native hosts without a `DataContext` are watched through `BindingContextChanged`, and the text field
placeholder uses `EM_SETCUEBANNER` instead of a newer `TextBox` property.

## 1. Prefix and type resolution

| Written in XAML | Resolves to |
|---|---|
| `Foo` (no prefix) | VeloxDev: `VeloxDev.WinForms.Xaml.Controls.Foo`, then `.Controls.Shapes.Foo`, then `VeloxDev.WinForms.Xaml.Foo` |
| `native:Foo` | `System.Windows.Forms.Foo` |
| `local:Foo` | your own `clr-namespace` |
| `v:Foo` | legacy alias for unprefixed `Foo`; still accepted so older markup compiles |

An unprefixed name that is not a VeloxDev type is an error: `VELOX_XAML019`. Write `native:Foo` for a
WinForms type or `local:Foo` for your own type. The root follows the same rule: `<UserControl>` (VeloxDev)
or `<local:TreeView>`; a native root needs `<native:UserControl>`.

The usual namespaces:

```xml
xmlns:native="clr-namespace:System.Windows.Forms;assembly=System.Windows.Forms"
xmlns:local="clr-namespace:MyApp.Views"
xmlns:behaviors="clr-namespace:VeloxDev.WorkflowSystem.AttachedBehaviors;assembly=VeloxDev.WinForms"
```

## 2. Root and code-behind

A XAML view is a `partial` class whose base is a XAML `Control`:

```csharp
public sealed partial class MyView : VeloxDev.WinForms.Xaml.Controls.UserControl
{
    public MyView() => InitializeComponent();
}
```

Use the full namespace for the base type. `System.Windows.Forms.UserControl` and the XAML
`UserControl` share a short name; the full name states which one the file inherits. The generator emits
`InitializeComponent()` as a private ordinary method, not a designer partial.

## 3. Controls

All of these derive from the XAML `Control`, so their properties are dependency properties and
styles, triggers, bindings and animations work. Native controls written with `native:` do not get this
pipeline.

| Family | Types |
|---|---|
| Layout | `Panel`, `StackPanel`, `DockPanel`, `WrapPanel`, `Grid`, `Canvas`, `UniformGrid`, `Viewbox`, `GridSplitter`, `Border`, `ScrollViewer`, `ContentControl`, `ItemsControl`, `ContentPresenter`, `ItemsPresenter`, `Decorator`, `AdornerDecorator`, `BulletDecorator`, `VirtualizingStackPanel` |
| Text | `TextBlock`, `Label`, `TextBox`, `PasswordBox`, `AccessText`, `RichTextBox`, `FlowDocumentScrollViewer`, `DocumentViewer` |
| Selection | `Button`, `RepeatButton`, `ToggleButton`, `CheckBox`, `RadioButton`, `ListBox`, `ComboBox`, `TreeView`, `ListView`, `DataGrid`, `Calendar`, `DatePicker` |
| Range / media | `ProgressBar`, `Slider`, `Image`, `Separator` |
| Shapes | `Rectangle`, `Ellipse`, `Line`, `Path`, `Polygon`, `Polyline` |
| Chrome | `GroupBox`, `Expander`, `TabControl`/`TabItem`, `Thumb`, `ToolBar`/`ToolBarTray`, `StatusBar`/`StatusBarItem`, `HeaderedContentControl`, `HeaderedItemsControl`, `AccessText`, `Menu`, `ContextMenu`, `ToolTip`, `Popup` |
| Navigation | `Page`, `Frame`, `Window`, `NavigationWindow` |

`ListBox` and `ComboBox` wrap the native selectors for selection and scrolling, so they render native
item text and `DisplayMemberPath`; use `ItemsControl` with `ItemTemplate` when every item needs an
arbitrary control.

## 4. Dependency properties and layout

Every XAML `Control` has these DPs:

- `Width`, `Height`, `MinWidth`, `MinHeight`, `MaxWidth`, `MaxHeight` (pixels)
- `Margin`, `Padding` (`Thickness`)
- `Background`, `Foreground` (`Color`; aliases for `BackColor` / `ForeColor`)
- `Font`, `Visibility`, `IsEnabled`
- `HorizontalAlignment`, `VerticalAlignment`

Attached layout properties:

```xml
<StackPanel Spacing="4">
  <Button Content="OK" />
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

`GridLength` literals are `Auto`, `*`, `2*` and pixel counts. `Canvas` also supports `Right` and
`Bottom`; `Grid` also supports `RowSpan` / `ColumnSpan`. `Panel.ZIndex` orders siblings: a higher
value moves the control toward the front. Native children are laid out by the same panels; attached
coordinates work on them, but the native control itself has no DP pipeline.

An explicit `Width` / `Height` wins over `Stretch`. In a horizontal stack the cross axis is the
whole strip, so a fixed-height button stays at that height and centres; the same applies to a fixed
width in a vertical stack. Leave the size unset when the control should fill the cross axis.

Measurement passes the available width down to `TextBlock` and `ContentControl`, so wrapping text
reports its wrapped height. `Border` measures its single child plus `Padding`, which means a card
needs no fixed `Height`: leave it unset and the card grows with its content. Panels that override
`MeasureCore` apply their own `Width`/`Height`/`Min*`/`Max*` through `Control.ConstrainSize`.

Layout invalidation is metadata-driven: `PropertyMetadata.AffectsMeasure` (or `AffectsArrange`)
makes the change clear the measure cache up to the topmost XAML control and run one coalesced
layout pass. `Control.MeasureCached` reuses a result for the same available size until that
invalidation. Custom controls should declare the flag rather than calling `Parent.PerformLayout()`
inside a property callback.

## 5. Bindings and resources

```xml
<TextBlock Text="{Binding Title}" />
<TextBox Text="{Binding Name, Mode=TwoWay}" />
<ContentControl Content="{Binding Selected}" ContentTemplate="{StaticResource CardTemplate}" />
<Border Background="{StaticResource Accent}" />
```

- Forms: `{Binding Path}`, `{Binding Path, Mode=OneWay|TwoWay|OneTime}`. The path supports dotted
  properties and simple indexers (`Items[0].Name`).
- Source selection: `ElementName`, `RelativeSource` (`Self`, `TemplatedParent`,
  `FindAncestor, AncestorType={x:Type local:Foo}, AncestorLevel=2`) and `Source`.
- Value shaping: `Converter={StaticResource Key}`, `ConverterParameter`, `StringFormat`,
  `FallbackValue`, `TargetNullValue`. A converter that implements `IParameterizedValueConverter`
  receives the parameter; `IValueConverter` alone keeps its two-argument contract.
- Write-back timing: `UpdateSourceTrigger=PropertyChanged|LostFocus|Explicit`. `Explicit` waits for
  `control.UpdateSource(property)` on a DP binding or `control.UpdateSource("Text")` on a native
  binding.
- `{x:Static local:Foo.Bar}` assigns a public static field or property and can be used as a binding
  source or a setter value.
- `DataContext` is inherited through XAML `Control` parents.
- Bindings re-resolve when the source implements `INotifyPropertyChanged`; collection triggers can
  subscribe to `INotifyCollectionChanged`. A XAML element source is also watched for DP changes so
  `ElementName` and `TemplateBinding` follow a dependency property; a native element source is read
  when the binding attaches and does not raise a change pipeline of its own.
- Native controls use `Control.DataBindings`; TwoWay works for native CLR properties there, and the
  converter/format/trigger options apply through the native binding's `Format`/`Parse` events.
- Attached DP values on native controls are OneWay/OneTime only; a native write has no DP change pipeline.
- `{StaticResource Key}` resolves through the control tree at runtime. A resource object must be
  declared before the resource or control that references it.
- `{DynamicResource Key}` follows resource replacement/removal. The lookup follows the whole
  ancestor chain, including ancestors attached after the referencing view was built.
- `StringFormat` is a .NET format string. Start it with `{}` when it starts with a brace, for
  example `StringFormat='{}{0:N2}'`; the escape prefix is removed before formatting. `ResourceDictionary.MergedDictionaries`
  are searched after local entries (last-added-first).
- Validation: `Binding.ValidationRules`, `ValidatesOnExceptions`, `INotifyDataErrorInfo` and
  `IDataErrorInfo` update `Validation.HasError`/`Errors`; a style or trigger can react to
  `Validation.HasError`.
- `VisualStateManager.GoToState(control, state, useTransitions)` applies `VisualState` setters and
  enter/exit actions. The compiler has no state-group markup syntax yet.

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

An empty object resource is created and its properties are assigned, which is how a selector is
declared.

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

- A XAML-control style setter targets a **dependency property**. Native-control styles support
  setters only, by CLR property name; native triggers/templates are not part of this subset.
- `Setter.Value` accepts a literal, `{StaticResource Key}`, `{x:Static ...}` or a `{Binding ...}`.
  A binding setter follows its source while the style or trigger is active; native-control setters
  reject bindings.
- Value precedence is local > style trigger > style setter > default.
- `Trigger` watches one dependency property and compares it to `Value`.
- `DataTrigger` watches a binding path on the data context; `MultiTrigger` requires every `Condition`.
- Inline triggers are declared on the control: `<Owner.Triggers>…`.
- A trigger can also run an `EnterAction` / `ExitAction`; that is the animation seam below. An action
  element may contain several actions; they run as one `TriggerActionGroup`.
- `Style.BasedOn` merges a base style. An implicit style has `TargetType` and no `x:Key`.
- `Style.Template` applies a `ControlTemplate` while the style is active. A template with triggers
  declares `TargetType`; a setter that targets a named part also declares the part's `TargetType`.

## 7. Animations

Animations are per-dependency-property transitions through the WinForms adapter's
`VeloxDev.TransitionSystem`. There is no storyboard/timeline object: one action animates one DP to
one value with one effect.

### 7.1 From markup — trigger actions

```xml
<Button Content="Save">
  <Button.Triggers>
    <Trigger Property="IsPointerOver" Value="True">
      <Trigger.EnterAction>
        <TransitionAction Property="Background"
                            To="#FF2B6CFF"
                            Duration="0:0:0.18"
                            Ease="CubicOut" />
      </Trigger.EnterAction>
      <Trigger.ExitAction>
        <TransitionAction Property="Background"
                            To="#FF20242C"
                            Duration="0:0:0.24"
                            Ease="CubicOut" />
      </Trigger.ExitAction>
    </Trigger>
  </Button.Triggers>
</Button>
```

The same shape works inside `Style.Triggers`:

```xml
<Style x:Key="PulseToggle" TargetType="ToggleButton">
  <Style.Triggers>
    <Trigger Property="IsChecked" Value="True">
      <Trigger.EnterAction>
        <TransitionAction Property="CornerRadius" To="8" Duration="0:0:0.2" Ease="SineInOut" />
      </Trigger.EnterAction>
    </Trigger>
  </Style.Triggers>
</Style>
```

| Attribute | Meaning |
|---|---|
| `Property` | Required. A DP on the target control, or `Owner.Property` for an attached DP (`Canvas.Left`, `Grid.Row`, …) |
| `To` | End value, converted to the property type. Literal only (number, color, enum, `Thickness`, …); markup extensions are not resolved here |
| `Duration` | `TimeSpan` literal, for example `0:0:0.2` |
| `Ease` | `CubicOut`, `Cubic.Out`, `SineInOut`, `Sine.InOut`, `Linear`, `Default`; unknown names report `VELOX_XAML003` |
| `LoopTime` | Additional full passes; `0` runs once, `int.MaxValue` repeats forever |
| `IsAutoReverse` | `True` runs each pass backwards after the forward pass |

An `EnterAction` / `ExitAction` may contain more than one action element. The generator wraps them in
a `TriggerActionGroup`; all actions start in declaration order. Use this for a multi-property effect
that WPF would express as one storyboard.

Both actions are needed for a reversible hover/checked effect. `EnterAction` runs when the
condition becomes true; `ExitAction` runs when it becomes false. The action writes a local value, it
does not restore the previous value on its own.

**The default is only used when both attributes are omitted.** `XamlTransition.Animate` supplies
240 ms `Cubic.Out` only when the action has no effect. If you write `Ease` but no `Duration`, the
effect has `Duration = 0`; if you write `Duration` but no `Ease`, it uses the transition system's
default ease. Write both when you customize either.

### 7.2 From code-behind

```csharp
using VeloxDev.TransitionSystem;
using VeloxDev.WinForms.Xaml;

XamlTransition.Animate(
    this,
    MyControl.GlowProperty,
    1d,
    new TransitionEffect
    {
        Duration = TimeSpan.FromMilliseconds(180),
        Ease = Eases.Cubic.Out,
    });

XamlTransition.Stop(this);
```

- Target must be a XAML `Control`; native controls are not animatable through this bridge.
- `Stop` ends every mutual transition running on the target where it stands.
- A new `XamlTransition.Animate` on the same target supersedes the previous run, so a fast
  enter/leave pair restarts from the current value instead of queuing.
- Start the first animation from the UI thread. WinForms frames are posted through
  `Control.BeginInvoke`.

### 7.3 Keyframes

A `KeyFrameAnimation` is a chain of ordinary transitions, one segment per adjacent pair of frames:

```xml
<Trigger.EnterAction>
  <KeyFrameAnimation Property="Background" Duration="0:0:0.6" Ease="CubicInOut">
    <KeyFrame Offset="0" Value="#FF20242C" />
    <KeyFrame Offset="0.5" Value="#FF2B6CFF" />
    <KeyFrame Offset="1" Value="#FF20242C" />
  </KeyFrameAnimation>
</Trigger.EnterAction>
```

- `Offset` is normalized to `0..1`; frames are sorted before building the chain.
- A first frame above zero animates from the property's current value to that frame. A frame at zero applies immediately.
- Each segment's duration is its share of the total `Duration`; every segment uses the same `Ease` (per-keyframe easing is not in the subset yet).
- The whole chain is one mutual run: a new animation supersedes it, and `XamlTransition.Stop` ends it.
- `KeyFrameAnimation` defaults to 240 ms `Cubic.Out` when `Duration` / `Ease` are omitted — unlike a
  single `TransitionAction`, where specifying one attribute without the other changes the effect.
- `Value` is converted to the animated property's type, exactly like `TransitionAction.To`.

From code-behind:

```csharp
XamlTransition.AnimateKeyFrames(
    control,
    MyControl.GlowProperty,
    [
        new KeyFrame { Offset = 0d, Value = 0d },
        new KeyFrame { Offset = 0.5d, Value = 0.5d },
        new KeyFrame { Offset = 1d, Value = 1d },
    ],
    TimeSpan.FromMilliseconds(300),
    Eases.Cubic.Out);
```

### 7.4 What can be animated

The transition system interpolates these property types:

`double`, `float`, `int`, `long`, `Color`, `Point`, `PointF`, `Size`, `SizeF`, `Rectangle`,
`RectangleF`, `Vector2`, `Vector3`, `Vector4`, `Quaternion`, `Padding`, `Thickness`.

`Thickness` is especially useful for `Margin`, `Padding` and `BorderThickness`; the WinForms adapter
registers a `ThicknessSampler` in addition to Core's samplers. A custom type needs an `ISampler`
registered through `TransitionSystem` — see the animation skill. `decimal`, `bool`, `string` and enums
have no sampler and are silently skipped by the transition engine.

### 7.5 Custom actions

A custom action derives from `TriggerAction` and has a public parameterless constructor and public
settable properties:

```csharp
public sealed class ShakeAction : TriggerAction
{
    public int Times { get; set; } = 3;

    public override void Invoke(VeloxDev.WinForms.Xaml.Controls.Control control)
    {
        // your action
    }
}
```

Declare it under `EnterAction` / `ExitAction` with a `local:` prefix (the owner is your own type);
literal and
`{StaticResource}` property values are supported. Nested content is not.

## 8. Templates and selectors

`DataTemplate` is a factory that creates one control tree:

```xml
<DataTemplate x:Key="NodeTemplate">
  <local:NodeView />
</DataTemplate>
```

A selector derives from the generic `VeloxDev.WinForms.Xaml.TemplateSelector`, not from any
workflow-specific base:

```csharp
public sealed class CustomTemplateSelector : TemplateSelector
{
    public DataTemplate? NodeTemplate { get; set; }
    public DataTemplate? SlotTemplate { get; set; }
    public DataTemplate? LinkTemplate { get; set; }
    public DataTemplate? TreeTemplate { get; set; }

    public override DataTemplate? SelectTemplate(object? item) => item switch
    {
        ILinkComponent => LinkTemplate,
        ISlotComponent => SlotTemplate,
        INodeComponent => NodeTemplate,
        ITreeComponent => TreeTemplate,
        _ => null,
    };
}
```

```xml
<local:CustomTemplateSelector x:Key="WorkflowTemplateSelector"
                              NodeTemplate="{StaticResource NodeTemplate}"
                              LinkTemplate="{StaticResource LinkTemplate}" />
```

The tree points `behaviors:WorkflowSurfaceBehavior.TemplateSelector` at that resource. `null` from
the selector means the pool skips the item; the `ViewFactory` reports an unsupported item.

### 8.1 ControlTemplate

A `ControlTemplate` still creates one root control, but it now carries the template pieces WPF
authors expect:

- The template root is docked to fill the templated control, so a plain `Control` needs no layout
  code to size its template.
- `TargetType="local:MyControl"` checks the owning type and lets the template's triggers resolve
  their properties against it.
- `{TemplateBinding Property}` binds a template child to the templated parent.
- `x:Name` inside the template is registered in the template's own `NameScope`; the templated control
  reads a part with `GetTemplateChild<T>("PART_...")`.
- `ControlTemplate.Triggers` evaluate against the templated control. A trigger setter may name a
  part with `TargetName`; use `TargetType="TextBlock"` when the property belongs to the part rather
  than to the templated control.

### 8.2 ItemsControl panels, containers and views

### 8.3 Lightweight visual tree

For non-interactive content that needs composition, `VisualHost` hosts a `Visual` tree painted in
one pass with no child window per node:

```csharp
var host = new VisualHost { Dock = DockStyle.Fill };
var group = new ContainerVisual { Bounds = new RectangleF(20, 20, 400, 200), Opacity = 0.7 };
group.Add(new ShapeVisual { Bounds = new RectangleF(0, 0, 80, 80), Geometry = new EllipseGeometry(), Fill = new SolidColorBrush(Color.White) });
group.Transform.RotateAt(8f, new PointF(200, 100));
host.Root.Add(group);
```

- `Visual.Bounds` is parent coordinates; content paints in `0..Width/Height`.
- `Transform`, `Opacity`, `Clip` and `Effect` apply to the subtree. Group opacity and effects render
  through a temporary bitmap; very large visuals fall back to one direct pass.
- `VisualHost.HitTestVisual(point)` walks the visual tree. Visual nodes are not HWND controls: they
  have no `DataContext`, focus, keyboard routing or accessibility. Keep native/`Control` controls
  as HWNDs and use this layer for decoration, text, shapes and connector batches.
- `ContainerVisual`, `ShapeVisual`, `TextVisual` and `CurveBatchVisual` are the built-in nodes;
  derive from `Visual` and override `RenderContent` / `HitTestContent` for new content.

`ItemsControl` supports `ItemsPanel` (a XAML container such as `StackPanel`), `ItemsPanelTemplate`,
`ItemContainerStyle`, `ItemContainerStyleSelector`, `ItemTemplateSelector`, and `Virtualizing` +
`ItemHeight` for fixed-row virtualized lists inside a `ScrollViewer`. The visible range uses the
viewer's client height when `ViewportHeight` is left at zero, so the control may be taller than the
viewer. The viewer handles the mouse wheel itself (Shift for the horizontal axis) and raises
`ViewChanged` for both wheel and thumb moves; it does not move its content, so a host that scrolls
static content must apply the offset, while the virtualized `ItemsControl` re-realizes rows on every
change.
`ItemContainerGenerator` maps items to generated controls (`ContainerFromItem`,
`ItemFromContainer`, `ContainerFromIndex`).

`CollectionView`/`CollectionViewSource` provide a WPF-shaped local view: sorting, filtering, grouping,
current item and `Refresh`. They are not the WindowsBase `ICollectionView` interface; assign the
`View` (IEnumerable) to `ItemsSource` and reassign when `ViewChanged` fires.

### 8.4 HWND-free visual backend (opt-in)

`VeloxDev.WinForms.Xaml.Visuals` is a second, opt-in backend: one `VisualSurface` window hosts a
tree of `VisualElement` objects; the tree is measured, arranged, hit-tested and painted in one pass.
It is independent from the HWND `Xaml.Controls` library and shares `Theme`.

```csharp
var surface = new VisualSurface { Dock = DockStyle.Fill };
var root = new Border { Padding = new Thickness(16), Background = new SolidColorBrush(Theme.Current.Surface) };
root.Child = new StackPanel { Spacing = 8 }
    .Add(new TextBlock { Text = "Hello" })
    .Add(new Button { Text = "Run" });
surface.Root = root;
```

- Properties use the same WPF naming: `DependencyProperty.Register`, `PropertyMetadata`, attached
  properties, `GetValue`/`SetValue`/`ClearValue`; `VisualElement` derives from `WinformsObject` and
  implements `IWinformsObject` with priority layers (`Animation`, `LocalValue`, `StyleTrigger`,
  `Template`, `Style`, `Inherited`).
- Bindings: `element.SetBinding(Control.PropProperty, new Binding("Path") { Source = vm })` for
  `OneWay`/`OneTime`/`TwoWay`, `DataContext`, converters and formatting; `Bind`/`GetObservable` for
  observable streams. `RelativeSource.Self` and `TemplateBinding` resolve; `ElementName` and
  validation rules do not.
- Resources: `VisualElement.Resources` / `VisualSurface.Resources` plus
  `VisualResourceLookup.Get<T>(element, key)`, which searches the element, its ancestors and the
  surface. Styles: `VisualStyle` / `VisualSetter` / `VisualTrigger`, explicit `Style` or implicit
  `Styles` collections; setters write at `Style`, triggers at `StyleTrigger`.
- Templates: `ContentControl` shows `Content` directly or through a `VisualControlTemplate`;
  `ContentPresenter` displays the content, `GetTemplateChild<T>` resolves named parts, and
  `VisualStyle.Template` applies a template through a style (a direct `ContentControl.Template`
  wins). Template triggers write at `Template` and may target a part with `TargetName`.
- Overlay: `VisualSurface` paints and hit-tests an overlay layer after the root. `VisualPopup` hosts
  its child there (not clipped by a `ScrollViewer`) and closes on an outside press or Escape.
  `ComboBox` uses it for the drop-down and keeps focus on itself.
- Visual XAML: prefix every visual type with
  `xmlns:v="clr-namespace:VeloxDev.WinForms.Xaml.Visuals.Controls;assembly=VeloxDev.WinForms"`.
  The root code-behind derives from the visual root type; `x:Name`, CLR events, `<Resources>` with
  `<Style>` / `<ControlTemplate>`, `Style="{StaticResource}"` and `{Binding}` /
  `{TemplateBinding}` / `{StaticResource}` work. `{DynamicResource}` and `<DataTemplate>` are
  diagnosed.
- `NativeControlHost` is the compatibility island for native controls (IME, third-party hosts).
  It is always a rectangular child HWND above the visual tree and cannot be transformed, blended or
  clipped with its visual parent, and an overlay popup cannot cover it.
- `VisualSurface` routes pointer, wheel, keyboard and text input to the deepest hit element; focus is
  logical. `TextBox` uses a native editor overlay while focused for IME/accessibility.

## 9. Events and input

- `IsPointerOver` follows the subtree: hovering a XAML child or a native child keeps every XAML ancestor's
  hover state true (the runtime recomputes screen rectangles on Enter/Leave), so a hover trigger on a
  `UserControl` fires while its content is hovered.
- Native WinForms events (`Click`, `KeyDown`, `MouseDown`, …) are bound normally and win over the
  bridged names.
- XAML `Control` exposes the standard pointer events (`PointerPressed`, `PointerMoved`,
  `PointerReleased`, `PointerEntered`, `PointerExited`, `PointerWheelChanged`) and
  `KeyInputDown` / `KeyInputUp`.
- Native controls participate through `XamlInput.AddPointerPressed` etc.; the adapter's behaviours
  use that bridge. For workflow gestures, subscribe to the tree's `IInputEvents` instead of
  per-control native events.
- `x:Name` creates a code-behind field and registers the name in the view's `NameScope`, so
  `ElementName` and `FindName` work. Workflow parts still resolve the native `Control.Name`, so a
  workflow template writes both when both are needed.

## 9.1 Commands, input bindings and focus

`Button.Command` / `CommandParameter` are dependency properties. `Command="{Binding Save}"` works, and
`CanExecuteChanged` drives `IsEnabled`.

`InputBindings` is a per-control collection:

```xml
<Button Content="Save" Command="{Binding Save}">
  <Button.InputBindings>
    <KeyBinding Key="S" Modifiers="Control" Command="{Binding Save}" />
    <MouseBinding Action="LeftDoubleClick" Command="{Binding Save}" />
  </Button.InputBindings>
</Button>
```

`KeyBinding`/`MouseBinding` are non-control objects, so their `Command` binding is routed through
`XamlRuntime.SetObjectBinding`; the host control supplies the data context. `TabIndex` and `IsTabStop`
mirror the native tab order; `FocusManager` exposes the currently focused XAML control.

## 9.2 Declarative chrome

`ContextMenu`, `MenuItem` and `Menu` wrap the native `ContextMenuStrip` / `MenuStrip`; child
`MenuItem` elements are added to the parent's item collection. `ContextMenu` can be used on a XAML
control through the `ContextMenu` property and on a native control through `ContextMenuStrip`.

`ToolTipService.ToolTip="text"` attaches a tooltip to any control; a XAML control also has a
`ToolTip` property that accepts either a string or a `ToolTip` object.

`Popup` hosts one `Child` in a borderless top-level window. `IsOpen`, `PlacementTarget`,
`Placement` and the horizontal/vertical offsets control its position. A popup child is detached
while the popup is closed.

## 9.2 Default theme

`Theme` holds the default palette. `Theme.Mode` swaps between `Light` and `Dark`; `Theme.Current`
can be replaced with a custom `ThemePalette`. The built-in palettes follow VS Code Dark+/Light+
(editor `#1E1E1E`/`#FFFFFF`, side bar `#252526`/`#F3F3F3`, accent `#007ACC`, focus
`#007FD4`/`#0090F1`, selection `#264F78`/`#ADD6FF`). Interactive controls that do not set their own
colours receive an implicit style from `ThemeStyles`: neutral surface, one accent, 4px corners, 28px
control height. Containers stay transparent.

After changing the palette, call `Theme.ApplyTo(root)` (or `ThemeStyles.ApplyTo(root)`) to re-apply the
implicit styles over a live tree. Explicit values and explicit/implicit styles from the control tree
still win over the theme fallback.

## 10. Limits and diagnostics

Not in this subset:

- native attached-value TwoWay, native style triggers/templates
- designer and hot reload
- a full WPF measure/arrange engine or `Viewbox`; use the shipped panels and explicit sizes
- arbitrary markup extensions in `TransitionAction.To` / `KeyFrame.Value`; only the binding and
  setter positions accept markup
- `DataTemplate` content with mixed text and elements; one root control per template
- per-keyframe easing; `KeyFrameAnimation` shares one `Ease`
- virtualization with variable item heights; `Virtualizing` uses a fixed `ItemHeight`
- a built-in `Opacity` / `RenderTransform` / `Clip` / `Effect` DP on HWND controls; the opt-in
  `VisualHost` visual tree supports them, but HWND controls do not composite over siblings
- template triggers that target a native part's CLR property; template trigger setters use DPs

The generator reports `VELOX_XAML001–019` and `VELOX_XAMLP001–004`. The most useful ones here:

| ID | Meaning |
|---|---|
| `VELOX_XAML001` | unknown type |
| `VELOX_XAML002` | unknown property or event (error) |
| `VELOX_XAML003` | bad literal / ease / duration |
| `VELOX_XAML004` | unsupported construct |
| `VELOX_XAML013` | unknown attached property |
| `VELOX_XAML014` | invalid `Style.TargetType` |
| `VELOX_XAML019` | unprefixed type is not a VeloxDev type; write `native:` or `local:` |

## 11. Workflow mapping

For the workflow surface, read [winforms.md](winforms.md). The XAML pieces it uses are:

- `WorkflowSurfaceBehavior` on the tree root, part names in `Name`
- `WorkflowNodeViewBehavior` / `WorkflowNodeTitleBehavior` / `WorkflowNodeRowsBehavior` on a node view
- `WorkflowSlotConnectionBehavior` on the slot view's path child
- `WorkflowLinkLayer` on `PART_Canvas` for the single link pass
- `WorkflowTemplateSelector` resource using the generic selector and `DataTemplate`s
- `WorkflowSlotAttachment` only as a compatibility helper for old code-first slot views; the
  generated slot template uses `Path` + `ViewModel` instead

## 12. Verification

```bash
dotnet test Src/Test/Unit/WinFormsXaml/VeloxDev.WinForms.Xaml.Test/VeloxDev.WinForms.Xaml.Test.csproj
dotnet test Src/Test/Headless/VeloxDev.WinForms.Headless.Tests/VeloxDev.WinForms.Headless.Tests.csproj
powershell -NoProfile -ExecutionPolicy Bypass -File Src/Test/Harness/verify-workflow-item-templates-all.ps1 -Platform WinForms -Strict
```

The runtime tests need a real control handle and a pumped message queue: every animation frame is
posted through `Control.BeginInvoke`. The template harness generates and builds all seven items, but
it cannot prove drawing or animation; run the Trimmed demo for that.

`Examples/XamlGallery` is the full feature catalogue (layout, text, data, input, graphics, animation,
binding/validation, styles/templates, lightweight visuals, theme, navigation). Build it or run it with `--smoke` after
changing runtime features; `--tab N --scroll PX --light --capture` drives and captures one page.
