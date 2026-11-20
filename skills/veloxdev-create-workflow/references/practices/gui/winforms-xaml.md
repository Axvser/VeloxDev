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
| `v:Foo` | `VeloxDev.WinForms.Xaml.Controls.Foo`, then `.Controls.Shapes.Foo`, then `VeloxDev.WinForms.Xaml.Foo` |
| `local:Foo` | your own `clr-namespace` |
| `Foo` (no prefix) | **native WinForms only** (`System.Windows.Forms.Label`, `Button`, `Panel`, …) |

An unprefixed VeloxDev type is an error: `VELOX_XAML019`. The generator never silently prefers the
adapter over a same-named native control. The root element is strict too: write `<v:UserControl>` or
`<local:TreeView>`, never `<TreeView>`.

The usual namespaces:

```xml
xmlns:v="clr-namespace:VeloxDev.WinForms.Xaml;assembly=VeloxDev.WinForms"
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
styles, triggers, bindings and animations work. Native controls written without `v:` do not get this
pipeline.

| Family | Types |
|---|---|
| Layout | `Panel`, `StackPanel`, `DockPanel`, `WrapPanel`, `Grid`, `Canvas`, `Border`, `ScrollViewer`, `ContentControl`, `ItemsControl` |
| Text | `TextBlock`, `Label`, `TextBox`, `PasswordBox` |
| Selection | `Button`, `RepeatButton`, `ToggleButton`, `CheckBox`, `RadioButton`, `ListBox`, `ComboBox` |
| Range / media | `ProgressBar`, `Slider`, `Image`, `Separator` |
| Shapes | `Rectangle`, `Ellipse`, `Line`, `Path` |

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
<v:StackPanel Spacing="4">
  <v:Button Content="OK" />
  <v:TextBlock>text bodies work too</v:TextBlock>
</v:StackPanel>

<v:Grid>
  <v:Grid.RowDefinitions>
    <v:RowDefinition Height="Auto" />
    <v:RowDefinition Height="*" />
  </v:Grid.RowDefinitions>
  <v:Border v:Grid.Row="1" />
</v:Grid>

<v:Canvas>
  <v:Border v:Canvas.Left="10" v:Canvas.Top="20" Width="80" Height="40" />
</v:Canvas>

<v:DockPanel>
  <v:Border v:DockPanel.Dock="Top" Height="36" />
  <v:Border />
</v:DockPanel>
```

`GridLength` literals are `Auto`, `*`, `2*` and pixel counts. `Canvas` also supports `Right` and
`Bottom`; `Grid` also supports `RowSpan` / `ColumnSpan`. Native children are laid out by the same
panels; attached coordinates work on them, but the native control itself has no DP pipeline.

## 5. Bindings and resources

```xml
<v:TextBlock Text="{Binding Title}" />
<v:TextBox Text="{Binding Name, Mode=TwoWay}" />
<v:ContentControl Content="{Binding Selected}" ContentTemplate="{StaticResource CardTemplate}" />
<v:Border Background="{StaticResource Accent}" />
```

- Forms: `{Binding Path}`, `{Binding Path, Mode=OneWay|TwoWay|OneTime}`.
- `DataContext` is inherited through XAML `Control` parents.
- Bindings re-resolve when the source implements `INotifyPropertyChanged`; collection triggers can
  subscribe to `INotifyCollectionChanged`.
- Native controls use `Control.DataBindings`; TwoWay works for native CLR properties there.
- Attached DP values on native controls are OneWay only; a native write has no DP change pipeline.
- `{StaticResource Key}` resolves through the control tree at runtime. A resource object must be
  declared before the resource or control that references it.

Resources live in a `v:ResourceDictionary`:

```xml
<v:UserControl.Resources>
  <v:ResourceDictionary>
    <v:DataTemplate x:Key="CardTemplate">
      <local:CardView />
    </v:DataTemplate>
    <v:Style x:Key="CardStyle" TargetType="v:Border">
      <v:Style.Setters>
        <v:Setter Property="Background" Value="#FF20242C" />
      </v:Style.Setters>
    </v:Style>
  </v:ResourceDictionary>
</v:UserControl.Resources>
```

An empty object resource is created and its properties are assigned, which is how a selector is
declared.

## 6. Styles and triggers

```xml
<v:Style x:Key="CardStyle" TargetType="v:Border">
  <v:Style.Setters>
    <v:Setter Property="Background" Value="#FF20242C" />
    <v:Setter Property="CornerRadius" Value="6" />
  </v:Style.Setters>
  <v:Style.Triggers>
    <v:Trigger Property="IsPointerOver" Value="True">
      <v:Trigger.Setters>
        <v:Setter Property="BorderBrush" Value="#FF5B8CFF" />
      </v:Trigger.Setters>
    </v:Trigger>
  </v:Style.Triggers>
</v:Style>
```

- A XAML-control style setter targets a **dependency property**. Native-control styles support
  setters only, by CLR property name; native triggers/templates are not part of this subset.
- Value precedence is local > style trigger > style setter > default.
- `Trigger` watches one dependency property and compares it to `Value`.
- `DataTrigger` watches a binding path on the data context; `MultiTrigger` requires every `Condition`.
- Inline triggers are declared on the control: `<Owner.Triggers>…`.
- A trigger can also run an `EnterAction` / `ExitAction`; that is the animation seam below.
- `Style.BasedOn` merges a base style. An implicit style has `TargetType` and no `x:Key`.
- `Style.Template` applies a `ControlTemplate` (one root control) while the style is active.

## 7. Animations

Animations are per-dependency-property transitions through the WinForms adapter's
`VeloxDev.TransitionSystem`. There is no storyboard/timeline object: one action animates one DP to
one value with one effect.

### 7.1 From markup — trigger actions

```xml
<v:Button Content="Save">
  <v:Button.Triggers>
    <v:Trigger Property="IsPointerOver" Value="True">
      <v:Trigger.EnterAction>
        <v:TransitionAction Property="Background"
                            To="#FF2B6CFF"
                            Duration="0:0:0.18"
                            Ease="CubicOut" />
      </v:Trigger.EnterAction>
      <v:Trigger.ExitAction>
        <v:TransitionAction Property="Background"
                            To="#FF20242C"
                            Duration="0:0:0.24"
                            Ease="CubicOut" />
      </v:Trigger.ExitAction>
    </v:Trigger>
  </v:Button.Triggers>
</v:Button>
```

The same shape works inside `Style.Triggers`:

```xml
<v:Style x:Key="PulseToggle" TargetType="v:ToggleButton">
  <v:Style.Triggers>
    <v:Trigger Property="IsChecked" Value="True">
      <v:Trigger.EnterAction>
        <v:TransitionAction Property="CornerRadius" To="8" Duration="0:0:0.2" Ease="SineInOut" />
      </v:Trigger.EnterAction>
    </v:Trigger>
  </v:Style.Triggers>
</v:Style>
```

| Attribute | Meaning |
|---|---|
| `Property` | Required. A DP on the target control, or `Owner.Property` for an attached DP (`Canvas.Left`, `Grid.Row`, …) |
| `To` | End value, converted to the property type. Literal only (number, color, enum, `Thickness`, …); markup extensions are not resolved here |
| `Duration` | `TimeSpan` literal, for example `0:0:0.2` |
| `Ease` | `CubicOut`, `Cubic.Out`, `SineInOut`, `Sine.InOut`, `Linear`, `Default`; unknown names report `VELOX_XAML003` |

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
<v:Trigger.EnterAction>
  <v:KeyFrameAnimation Property="Background" Duration="0:0:0.6" Ease="CubicInOut">
    <v:KeyFrame Offset="0" Value="#FF20242C" />
    <v:KeyFrame Offset="0.5" Value="#FF2B6CFF" />
    <v:KeyFrame Offset="1" Value="#FF20242C" />
  </v:KeyFrameAnimation>
</v:Trigger.EnterAction>
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

Declare it under `EnterAction` / `ExitAction` with a `local:`/`v:` prefix; literal and
`{StaticResource}` property values are supported. Nested content is not.

## 8. Templates and selectors

`DataTemplate` is a factory that creates one control tree:

```xml
<v:DataTemplate x:Key="NodeTemplate">
  <local:NodeView />
</v:DataTemplate>
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
        IWorkflowLinkViewModel => LinkTemplate,
        IWorkflowSlotViewModel => SlotTemplate,
        IWorkflowNodeViewModel => NodeTemplate,
        IWorkflowTreeViewModel => TreeTemplate,
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

## 9. Events and input

- Native WinForms events (`Click`, `KeyDown`, `MouseDown`, …) are bound normally and win over the
  bridged names.
- XAML `Control` exposes the standard pointer events (`PointerPressed`, `PointerMoved`,
  `PointerReleased`, `PointerEntered`, `PointerExited`, `PointerWheelChanged`) and
  `KeyInputDown` / `KeyInputUp`.
- Native controls participate through `XamlInput.AddPointerPressed` etc.; the adapter's behaviours
  use that bridge. For workflow gestures, subscribe to the tree's `IInputEvents` instead of
  per-control native events.
- `x:Name` creates a code-behind field. Workflow parts resolve the native `Control.Name`, so a
  workflow template writes both when both are needed.

## 10. Limits and diagnostics

Not in this subset:

- WPF templated parent, template parts, `ElementName`, `RelativeSource`, `x:Static`
- native attached-value TwoWay, native style triggers/templates
- designer and hot reload
- a full WPF measure/arrange engine or `Viewbox`; use the shipped panels and explicit sizes
- arbitrary markup extensions in `TransitionAction.To` / setter values
- `DataTemplate` content with mixed text and elements; one root control per template
- a built-in `Opacity` DP; add a custom DP if your control paints from it (WinForms child windows do not composite over siblings)

The generator reports `VELOX_XAML001–019` and `VELOX_XAMLP001–004`. The most useful ones here:

| ID | Meaning |
|---|---|
| `VELOX_XAML001` | unknown type |
| `VELOX_XAML002` | unknown property or event |
| `VELOX_XAML003` | bad literal / ease / duration |
| `VELOX_XAML004` | unsupported construct |
| `VELOX_XAML013` | unknown attached property |
| `VELOX_XAML014` | invalid `Style.TargetType` |
| `VELOX_XAML019` | unprefixed type is not native; write `v:` or `local:` |

## 11. Workflow mapping

For the workflow surface, read [winforms.md](winforms.md). The XAML pieces it uses are:

- `WorkflowSurfaceBehavior` on the tree root, part names in `Name`
- `WorkflowNodeViewBehavior` / `WorkflowNodeTitleBehavior` / `WorkflowNodeRowsBehavior` on a node view
- `WorkflowSlotConnectionBehavior` on the slot view's path child
- `WorkflowLinkLayer` on `PART_Canvas` for the single link pass
- `WorkflowTemplateSelector` resource using the generic selector and `DataTemplate`s
- `WorkflowSlotAttachment` only as a compatibility helper for old code-first slot views; the
  generated slot template uses `v:Path` + `ViewModel` instead

## 12. Verification

```bash
dotnet test Src/Test/Unit/WinFormsXaml/VeloxDev.WinForms.Xaml.Test/VeloxDev.WinForms.Xaml.Test.csproj
dotnet test Src/Test/Headless/VeloxDev.WinForms.Headless.Tests/VeloxDev.WinForms.Headless.Tests.csproj
powershell -NoProfile -ExecutionPolicy Bypass -File Src/Test/Harness/verify-workflow-item-templates-all.ps1 -Platform WinForms -Strict
```

The runtime tests need a real control handle and a pumped message queue: every animation frame is
posted through `Control.BeginInvoke`. The template harness generates and builds all seven items, but
it cannot prove drawing or animation; run the Trimmed demo for that.
