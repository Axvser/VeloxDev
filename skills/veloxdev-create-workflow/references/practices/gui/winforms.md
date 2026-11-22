# Windows Forms

`VeloxDev.WinForms` · targets `netframework4.6.1;net5.0-windows;netcoreapp3.0;net8.0-windows` · version 11.2.0

There is a long-form adapter README at `Src/Adapters/VeloxDev.WinForms/README.md`. This is the summary a consumer needs.

⚙ **Reference implementation:** `Examples/Workflow/WinForms Trimmed/Demo/Views/Workflow/` — the four markup items are the tree, node, slot and link views (`*.xaml` + `*.xaml.cs`); `GridDecorator`, `MinimapOverlay` and `CustomTemplateSelector` are code-only. The Full demo lives at `Examples/Workflow/WinForms/Demo/Views/Workflow/`.

⚙ **The "Trimmed" in that path means *minimal demo*.** It is a demo-size choice, not a publish setting — see [the rule in SKILL.md](../../../SKILL.md#checking-your-work).

## The adapter is XAML-first

⚙ **The complete runtime reference is [winforms-xaml.md](winforms-xaml.md).** Read it before writing
styles, triggers, animations, custom controls, or a non-trivial `DataTemplate`; this page only
summarizes the workflow surface.

WinForms has no platform XAML, so this adapter ships its own small runtime (`Src/Adapters/VeloxDev.WinForms/Xaml/`) and a Roslyn generator (`Src/Generators/VeloxDev.WinForms.Generator/`). The generated `InitializeComponent()` builds ordinary WinForms controls:

- **Unprefixed means VeloxDev**: `VeloxDev.WinForms.Xaml.Controls` / `.Controls.Shapes` / `.Xaml` (`UserControl`, `Panel`, `StackPanel`, `DockPanel`, `WrapPanel`, `Grid`, `Canvas`, `Border`, `ScrollViewer`, `Button`, `TextBox`, `ListBox`, `ComboBox`, `TextBlock`, `Rectangle`, `Path`, `Style`, `Trigger`, `DataTemplate`, …).
- `native:` means `System.Windows.Forms` (`Label`, `TableLayoutPanel`, `Button`, …); declare `xmlns:native="clr-namespace:System.Windows.Forms;assembly=System.Windows.Forms"`.
- An unprefixed name that is not a VeloxDev type is `VELOX_XAML019`; write `native:` for a WinForms type or `local:` for your own.
- `local:` is your own namespace; the item templates use it for sibling views. **The root view follows the same rule**: `<UserControl …>` and `<local:TreeView …>` resolve by the same table; a native root is `<native:UserControl …>`. (`v:` is still accepted as a legacy alias for VeloxDev.)
- Dependency properties, attached properties, `{Binding}`, `{StaticResource}`, `Style`, `Trigger`, `DataTrigger`, `MultiTrigger`, `TransitionAction`, `DataTemplate` and `ControlTemplate` are supported.
- A **selector** is an object resource whose `DataTemplate` properties point at template resources:
  `<local:CustomTemplateSelector x:Key="WorkflowTemplateSelector" NodeTemplate="{StaticResource NodeTemplate}" />`.
- Every DP change on a XAML `Control` calls `Invalidate()` automatically. Mark configuration properties with `new PropertyMetadata(...) { AffectsRender = false }` if they never paint.

```xml
xmlns:native="clr-namespace:System.Windows.Forms;assembly=System.Windows.Forms"
xmlns:local="clr-namespace:MyApp.Views"
xmlns:behaviors="clr-namespace:VeloxDev.WorkflowSystem.AttachedBehaviors;assembly=VeloxDev.WinForms"
```

⚙ **Add the generator to the consuming project**, not only the adapter package:

```xml
<ItemGroup>
  <ProjectReference Include="…/VeloxDev.WinForms.Generator.csproj"
                    OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
  <AdditionalFiles Include="**/*.xaml" />
</ItemGroup>
```

**Code-behind names the adapter base with its full namespace.** This is the one place where the prefix rules cannot help: `System.Windows.Forms.UserControl` and the XAML one share a short name. Write the base explicitly so the file states which world it is in:

```csharp
public sealed partial class TreeView : VeloxDev.WinForms.Xaml.Controls.UserControl
```

## Base control library

`VeloxDev.WinForms` ships a WPF/Avalonia-shaped control set whose types are written unprefixed. Every type derives from the XAML `Control`, so its properties are dependency properties and styles, triggers, transitions and bindings work; a native WinForms element written with `native:` keeps native behaviour and only supports attached values.

| Family | Controls |
| --- | --- |
| Layout | `Panel`, `StackPanel`, `DockPanel`, `WrapPanel`, `Grid`, `Canvas`, `Border`, `ScrollViewer`, `ContentControl`, `ItemsControl` |
| Text | `TextBlock` (WPF naming), `Label` (Content alias), `TextBox`, `PasswordBox` |
| Selection | `Button`, `RepeatButton`, `ToggleButton`, `CheckBox`, `RadioButton`, `ListBox`, `ComboBox` |
| Range / media | `ProgressBar`, `Slider`, `Image`, `Separator` |
| Shapes | `Rectangle`, `Ellipse`, `Line`, `Path` |

Attached coordinates follow the same `Owner.Property` spelling:

```xml
<Grid x:Name="layout">
  <Grid.RowDefinitions>
    <RowDefinition Height="Auto" />
    <RowDefinition Height="*" />
  </Grid.RowDefinitions>
  <Border Grid.Row="1" />
</Grid>

<Canvas>
  <Border Canvas.Left="10" Canvas.Top="20" />
</Canvas>

<StackPanel Spacing="4">
  <Button Content="OK" Click="OnOk" />
  <TextBlock>a text body works too</TextBlock>
</StackPanel>
```

- `Width` / `Height` / `Min*` / `Max*`, `Margin`, `Padding`, `Background`, `Foreground`, `Font`, `Visibility`, `IsEnabled`, `HorizontalAlignment` and `VerticalAlignment` are dependency properties on every XAML control.
- `Canvas.Left/Top/Right/Bottom`, `Grid.Row/Column/RowSpan/ColumnSpan` and `DockPanel.Dock` are attached dependency properties.
- Native children are still laid out by the VeloxDev panels; attach VeloxDev coordinates (`Grid.Row`, `Canvas.Left`, …) when a native control needs a cell.
- `ItemsControl` is the template host: `ItemsSource` + `ItemTemplate` creates one control per item. `ListBox` and `ComboBox` wrap the native selectors for selection and scrolling, so they render the native item text and `DisplayMemberPath`, not an arbitrary `ItemTemplate`.

## Animations

Animations are dependency-property transitions: one action animates one DP to one value with one effect.
From markup, put a `TransitionAction` under a trigger's `EnterAction` / `ExitAction`:

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

For more than two stops, use a `KeyFrameAnimation`; it is the same transition chain, one segment per
adjacent pair of frames:

```xml
<Trigger.EnterAction>
  <KeyFrameAnimation Property="Background" Duration="0:0:0.6" Ease="CubicInOut">
    <KeyFrame Offset="0" Value="#FF20242C" />
    <KeyFrame Offset="0.5" Value="#FF2B6CFF" />
    <KeyFrame Offset="1" Value="#FF20242C" />
  </KeyFrameAnimation>
</Trigger.EnterAction>
```

From code-behind, call `XamlTransition.Animate(control, property, to, effect)` for one segment or
`XamlTransition.AnimateKeyFrames(control, property, frames, duration, ease)` for a keyed chain;
`Stop` ends the run where it stands. A new animation supersedes the previous one on the same target.

- The default effect (240 ms `Cubic.Out`) applies only when **both** `Duration` and `Ease` are omitted.
  If you set one, set the other: an `Ease` without `Duration` is a zero-length effect.
- Only XAML `Control` DPs can be animated; native controls cannot. Supported value types include
  `double`, `float`, `int`, `long`, `Color`, `Point`, `PointF`, `Size`, `SizeF`, `Rectangle`,
  `RectangleF`, `Vector2/3/4`, `Quaternion`, `Padding` and `Thickness`.
- `Property` can be an attached DP (`Canvas.Left`, `Grid.Row`) using `Owner.Property`.
- For reversible hover/checked effects, write both `EnterAction` and `ExitAction`; the action writes a
  local value and does not restore the previous one by itself.

The full attribute table, custom `TriggerAction` shape and failure cases are in
[winforms-xaml.md § Animations](winforms-xaml.md#7-animations).

## Writing the surface

The tree template is a `UserControl`; the parts are resolved by native `Control.Name`, and the attached properties are declared on the root:

```xml
<UserControl x:Class="MyApp.Views.TreeView"
               behaviors:WorkflowSurfaceBehavior.IsEnabled="True"
               behaviors:WorkflowSurfaceBehavior.ZoomEnabled="True"
               behaviors:WorkflowSurfaceBehavior.TemplateSelector="{StaticResource WorkflowTemplateSelector}"
               behaviors:WorkflowSurfaceBehavior.ScrollViewerName="PART_ScrollViewer"
               behaviors:WorkflowSurfaceBehavior.CanvasName="PART_Canvas"
               behaviors:WorkflowSurfaceBehavior.GridDecoratorName="PART_GridDecorator"
               behaviors:WorkflowSurfaceBehavior.MinimapOverlayName="PART_MinimapOverlay"
               behaviors:WorkflowSurfaceBehavior.PointerPressSourceName="PART_Surface">
  <UserControl.Resources>
    <ResourceDictionary>
      <DataTemplate x:Key="NodeTemplate">
        <local:NodeView />
      </DataTemplate>
      <local:CustomTemplateSelector x:Key="WorkflowTemplateSelector"
                                    NodeTemplate="{StaticResource NodeTemplate}" />
    </ResourceDictionary>
  </UserControl.Resources>
  <Panel Name="PART_Surface" Dock="Fill">
    <local:GridDecorator Name="PART_GridDecorator" Dock="Fill">
      <ScrollViewer Name="PART_ScrollViewer" Dock="Fill">
        <local:SurfaceCanvas Name="PART_Canvas" Dock="Fill" />
      </ScrollViewer>
    </local:GridDecorator>
  </Panel>
</UserControl>
```

⚙ **`x:Name` and `Name` are different things.** `x:Name` creates a code-behind field; the behavior resolves native `Control.Name`, so the `PART_*` contract lives in `Name`. The tree template writes both when the field is useful.

⚙ **The tree arrives through `SetWorkflowTree`** — typically set from your code-behind (`ViewModel` setter). `Refresh(host)` is part of the contract: a node that moved without a refresh is still drawn where it was.

⚙ **A synchronous escape hatch exists.** `WorkflowSlotLayoutBehavior.SyncNow(control)` re-measures a node's slots in the same frame; node drag and zoom use it. A deferred refresh is a frame stale and the link visibly detaches from its port.

## Links — one layer paints the whole graph

⚙ **Links are not pooled.** `ViewLinkLayer.IsActive=true`, and `WorkflowLinkLayer` draws every link in the tree's **visible set** inside `PART_Canvas`'s `Paint` pass. One canvas paint replaces one child window per link; the Full benchmark drops from 13.9 s to 4.14 s (2000 nodes / 3000 links).

```xml
<local:SurfaceCanvas Name="PART_Canvas" Dock="Fill"
                     behaviors:WorkflowLinkLayer.IsEnabled="True"
                     behaviors:WorkflowLinkLayer.LineColor="#DDFFFFFF"
                     behaviors:WorkflowLinkLayer.Thickness="2" />
```

The layer publishes each curve to Core, so hit-testing, `Delete` and the link context menu use the same curve it drew. It also exposes `HoveredLineColor` / `HoveredThickness`, `VirtualLineColor` / `VirtualValidTargetColor` / `VirtualInvalidTargetColor`, `Glow`, and `FlowHead` / `FlowIntensity` for the travelling highlight.

⚙ **Use the node-local anchor for endpoints.** `WorkflowSlotLayoutBehavior.TryGetLocalAnchor` returns the port's centre in the node's client coordinates; it is independent of pan, scroll and measurement timing. Do not derive the curve endpoint from `slot.Anchor − current projection`: a node leaving the visible set freezes its measurement while the projection keeps moving, and the bend direction flips.

⚙ **The canvas repaint must come after the geometry refresh.** `ApplyPanCore` writes the scroll offset first, measures the slots, refreshes the link layer, then calls `canvas.Update()` during a gesture. Reversing that order shows the previous frame's curves while dragging.

⚙ **A per-link view is still possible, but opt-in.** `WorkflowLinkAttachment.Attach(control)` owns endpoints, window-region carving, curve publishing and pointer events; your `OnPaint` draws. A carved link window must use the surface background as `BackColor`, has no stroke to widen at zero length, and needs `Invalidate()+Update()` to avoid drag trails. For a graph with more than a handful of links, use the single layer.

## Input

⚙ **All input goes through Core's route**, `WorkflowInput.For(tree)`. Do not subscribe per-control mouse events for workflow gestures; subscribe `((IInputEvents)vm.GetHelper()).Input.*` instead.

- **Move:** the surface's `IMessageFilter` translates `WM_MOUSEMOVE` and routes it. This is the only way a move over a **node or slot child window** reaches Core; the surface root never receives those `MouseMove` messages.
- **Press / release:** the node and slot behaviours route every button (left, right, middle) on components; the surface routes presses/releases on empty canvas and links. A right-press on a node/slot therefore reaches that component's `PointerPressed`, not the surface's link menu.
- **Wheel:** the same message filter routes `PointerWheelChanged` first; a subscriber's `PreventDefault` hands the scroll to `WorkflowInput.Scroller`, otherwise the adapter applies its own step. Ctrl+wheel is zoom.
- **Target resolution prefers `Tag`:** the attachments write the node/slot model to `Tag`; an inherited `DataContext` may be the tree, so it must not mask the Tag.
- **Enter/leave are synthesized by Core** from target changes. Leaving the surface from a node is covered by a 50 ms pointer watch, and the explicit `PointerExited` carries the previous `PointerTarget`.
- **Keys** are routed with `PointerTarget` as their target; focus moves to the canvas on the press (unless the press landed in an editable control).

## Adding your own content to a node

⚙ **No `Viewbox`.** `WorkflowNodeViewBehavior` attaches `WorkflowNodeAttachment`, points drag and slot layout at the canvas, and keeps the card backdrop composited. `WorkflowNodeTitleBehavior` binds the title and scales the header with the collapse factor; `WorkflowNodeRowsBehavior` rebuilds the input/output rows from `node.Slots`.

⚙ **Content you add joins the same layout.** Put it inside the node card's `PART_*` shell and scale it in the node view's own layout code; a render transform is not part of this adapter's measurement contract.

⚙ **Ports are measured controls.** The slot layout measures each slot view and writes `slot.Anchor` in canvas-local space through `SlotAnchorFromCanvasLocal`, because the canvas client area *is* the link rendering space and a node's `Location` already includes the pan and `ActualOffset`. `SlotAnchorFromVisualCenter` subtracts `ActualOffset` a second time and shifts every link by `−ActualOffset`.

## Zoom and pan

⚙ The zoom gesture is `MouseWheel` **plus** an `Application.AddMessageFilter` on `WM_MOUSEWHEEL`, which swallows the message so no scrollable child scrolls instead. Wheel-up is `Scale *= 1 / 1.1`; `Scale` is a collapse factor.

⚙ **The scroll is clamped to the reachable range; this adapter does not grow the canvas.** A plain wheel is routed as `PointerWheelChanged` and applied through `WorkflowInput.Scroller` unless a subscriber set `PreventDefault`; Ctrl+wheel is the zoom gesture and can be refused the same way.

⚙ **A self-hosted canvas sets the virtualize inset before writing `Viewport`.** The tree has no decorator-hosted ruler to ask, so the reserve is passed explicitly (`RulerReserve` on both axes); skipping it culls nodes still visible under the ruler.

## The link context menu

⚙ **The menu is built in code, not markup.** In the tree's code-behind:

```csharp
WorkflowSurfaceBehavior.SetLinkMenuBuilder(this, static (menu, link) =>
    menu.Items.Add("Delete", null, (_, _) => link.DeleteCommand.Execute(null)));
```

The surface opens the strip on a right-press on a link, positions it, and suspends input routing while it is open. **The generated template adds no entries** — including Delete.

## Item templates — `VeloxDev.WinForms.Templates`

**Generates** `.xaml` + `.xaml.cs` for node, slot, link and tree, and a `.cs` for selector, decorator and minimap. All seven target the HWND-free visual backend (`VeloxDev.WorkflowSystem.VisualHosting`).

| Item | Shape | You supply |
| --- | --- | --- |
| tree | `UserControl` hosting one `VisualWorkflowSurface`; code-behind sets the palette and exposes `ViewModel` / `TemplateSelector` | the palette, selector wiring, link menu |
| node | derived `VisualWorkflowNodeView`; XAML self-root sets the card chrome, `BuildContent` gives title/body | the card chrome and title layout |
| slot | derived `VisualWorkflowSlotView`; `OnRender` draws the glyph | the glyph and its state colours |
| link | derived `VisualElement` — **per-link opt-in**; the default surface draws every link in one `VisualWorkflowLinkLayer` | the resting curve and palette |
| minimap | derived `VisualWorkflowMinimap` | the four palette colours |
| grid decorator | derived `VisualWorkflowGridDecorator` | grid palette and spacing |
| template selector | derived `VisualTemplateSelector` returning `VisualElement` views | a `VisualDataTemplate` per item kind |

⚙ **The items no longer reference each other at compile time.** The tree only references the adapter's `VisualWorkflowSurface`; wire the selector yourself through `VisualWorkflowSurface.TemplateSelector`. The surface asks the selector first, then `NodeViewFactory`, then its default card — a selection that is not a `VisualWorkflowNodeView` falls through.

⚙ **The surface composes the grid, minimap and link layer itself.** The `workflow-grid-decorator` and `workflow-minimap-overlay` items are standalone extension points (or palette references); changing the tree's look means touching `workflowHost.GridDecorator` / `workflowHost.Minimap` / `workflowHost.LinkLayer`, not adding controls to the tree.

⚙ **Give the tree a virtualised helper.** `LinkHitTestEx` walks `VisibleItems`; `new TreeHelper(200)` (or a subclass) is required for link hit testing. The surface writes `Viewport(-Pan.X, -Pan.Y, w, h)` on every arrange / pan / zoom.

⚙ **The visual surface handles Delete, Ctrl+Z/Y, Ctrl+wheel zoom and Shift+wheel scroll.** A host that also handles them must set `PreventDefault`, or a link is deleted twice.

## Pitfalls

⚙ **The visual backend is one HWND.** Cards, links and the grid are `VisualElement`s painted in one pass; do not nest native controls over the surface to get effects — subclass the visual element instead.

⚙ **Do not hand-roll a second link geometry path.** The layer publishes the curve Core hit-tests; a host-side highlight must draw from `ILinkHitTestable.Curve`, or the glow and the click target disagree.
