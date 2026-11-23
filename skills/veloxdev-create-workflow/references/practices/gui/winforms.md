# Windows Forms

`VeloxDev.WinForms` · targets `netframework4.6.1;net5.0-windows;netcoreapp3.0;net8.0-windows` · version 12.0.0

There is a long-form adapter README at `Src/Adapters/VeloxDev.WinForms/README.md`. This is the summary a consumer needs.

⚙ **Reference implementation:** `Examples/Workflow/WinForms Trimmed/Demo/Views/Workflow/` — the tree, node, slot and link views are markup; the tree is a whole visual tree hosted in a `VisualHost`, and one camera matrix scales the graph. `GridDecorator`, `MinimapOverlay` and `CustomTemplateSelector` are code-only. The Full demo lives at `Examples/Workflow/WinForms/Demo/Views/Workflow/` and uses the same visual surface; its shell is ordinary WinForms.

⚙ **The "Trimmed" in that path means *minimal demo*.** It is a demo-size choice, not a publish setting — see [the rule in SKILL.md](../../../SKILL.md#checking-your-work).

## The adapter is visual XAML

⚙ **The complete runtime reference is [winforms-xaml.md](winforms-xaml.md).** Read it before writing
styles, triggers, animations, custom controls, or a `DataTemplate`; this page only summarizes the
workflow surface.

WinForms has no platform XAML, so this adapter ships its own visual runtime (`Src/Adapters/VeloxDev.WinForms/Xaml/`) and a Roslyn generator (`Src/Generators/VeloxDev.WinForms.Generator/`). The generated `InitializeComponent()` builds a tree of `VisualElement`s hosted by one `VisualHost`.

- **Unprefixed means the visual backend**: `VeloxDev.WinForms.Xaml.Visuals.Controls` / `.Shapes` / `.Xaml`, plus the shared primitives in `.Xaml.Controls` (`UserControl`, `Canvas`, `Grid`, `Border`, `StackPanel`, `DockPanel`, `ScrollViewer`, `ItemsControl`, `TextBlock`, `Button`, `TextBox`, `TreeView`, `Path`, `Style`, `DataTemplate`, …).
- `local:` is your own namespace; the item templates use it for sibling views. The root follows the same rule: `<UserControl …>` and `<local:TreeView …>` resolve by the same table.
- `native:` means `System.Windows.Forms` and is **rejected inside the visual tree**; native content is only possible through an explicit `NativeControlHost` island.
- `v:` is still accepted as a legacy alias for unprefixed, but is not required.
- Dependency properties, attached properties, `{Binding}`, `{StaticResource}`, `Style`, `Trigger`, `DataTrigger`, `MultiTrigger`, `DataTemplate` and `ControlTemplate` are supported.
- A **selector** derives from `VisualTemplateSelector` and returns visual elements; no `DataTemplate` selector is used for workflow items.

```xml
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

**Code-behind names the adapter base with its full namespace:**

```csharp
public sealed partial class TreeView : VeloxDev.WinForms.Xaml.Visuals.Controls.UserControl
```

## Base control library

Every type derives from `VisualElement`, so its properties are dependency properties and styles,
triggers and bindings work. None of them creates a child window.

| Family | Controls |
| --- | --- |
| Layout | `UserControl`, `ContentControl`, `Border`, `Panel`, `StackPanel`, `DockPanel`, `WrapPanel`, `Grid`, `Canvas`, `UniformGrid`, `Viewbox`, `Decorator`, `ScrollViewer`, `GridSplitter`, `GroupBox`, `Expander`, `TabControl` |
| Items | `ItemsControl`, `ListBox`, `VirtualList`, `TreeView`, `ComboBox`, `Calendar`, `DatePicker`, `ContentPresenter` |
| Text | `TextBlock`, `TextBox`, `RichTextBox`, `Image` |
| Selection / range | `Button`, `RepeatButton`, `ToggleButton`, `CheckBox`, `RadioButton`, `Slider`, `ProgressBar` |
| Shapes | `Rectangle`, `Ellipse`, `Line`, `Polygon`, `Polyline`, `Path` |
| Overlay | `VisualPopup`, `VisualMenu`, `VisualContextMenu`, `VisualPresenter` |

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
  <Button Text="OK" Click="OnOk" />
  <TextBlock>a text body works too</TextBlock>
</StackPanel>
```

- `Width` / `Height` / `Min*` / `Max*`, `Margin`, `Padding`, `Background`, `CornerRadius`, `Visibility`, `IsEnabled`, `HorizontalAlignment` and `VerticalAlignment` are dependency properties on every visual element.
- `Canvas.Left/Top/Right/Bottom`, `Grid.Row/Column/RowSpan/ColumnSpan` and `DockPanel.Dock` are attached dependency properties.
- `Button`, `RepeatButton`, `ToggleButton`, `CheckBox` and `RadioButton` share `ButtonBase.Command` and `CommandParameter`; activation runs the command only when `CanExecute` allows it, and a refusing command disables the control until it allows again.
- `ItemsControl` is the template host: `ItemsSource` + `ItemTemplate` creates one element per item. `ListBox` and `VirtualList` build on it.

## Animations

Markup trigger actions animate one dependency property: `TransitionAction` for one segment,
`KeyFrameAnimation` for a keyed chain, and `TriggerActionGroup` for several actions. A custom action
derives `VeloxDev.WinForms.Xaml.Visuals.VisualTriggerAction`. Code-behind can also use
`VeloxDev.TransitionSystem.Transition<T>` directly on the element's CLR property wrappers:

```csharp
using VeloxDev.TransitionSystem;

Transition<VeloxDev.WinForms.Xaml.Visuals.VisualElement>.Create()
    .Property(v => v.CornerRadius, 8f)
    .Effect(new TransitionEffect
    {
        Duration = TimeSpan.FromMilliseconds(180),
        Ease = Eases.Cubic.Out,
    })
    .Execute(element);
```

- The code-behind transition writes the local priority layer each frame; markup actions write `BindingPriority.Animation`. A new animation on the same element supersedes the previous one.
- Animate a DP-backed property (`Margin`, `Width`, `CornerRadius`, `Background`, a custom `[XamlProperty]`); the plain `Visual.Opacity` does not invalidate, so feed it through a DP.
- Sampled types include `double`, `float`, `int`, `long`, `Color`, `Point`, `PointF`, `Size`, `SizeF`, `Rectangle`, `RectangleF`, `Vector2/3/4`, `Quaternion`, `Padding` and `Thickness`.
- Start the first animation on the UI thread; frames are posted through `Control.BeginInvoke`.
- `XamlTransition` belongs to the HWND control layer; the visual bridge is `VisualTransition.Animate` / `AnimateKeyFrames` / `Stop`.

## Writing the surface

The whole workflow tree is visual. The tree template is a visual `UserControl` (`GridDecorator`/`ScrollViewer`/`Canvas`, never a workflow base class); the demo hosts it in a generic `VisualHost` and attaches `WorkflowSurfaceBehavior` to that host. `PART_*` are visual element names.

```csharp
var treeView = new TreeView();                       // visual UserControl (the generated tree item)
var host = new VisualHost { Dock = DockStyle.Fill, Root = treeView };
shell.Controls.Add(host);                             // HUD/minimap can be siblings here
WorkflowSurfaceBehavior.SetCanvasName(host, "PART_Canvas");
WorkflowSurfaceBehavior.SetScrollViewerName(host, "PART_ScrollViewer");
WorkflowSurfaceBehavior.SetGridDecoratorName(host, "PART_GridDecorator");
// The tree declares CustomTemplateSelector x:Key="WorkflowTemplateSelector"; the surface resolves it by default.
WorkflowSurfaceBehavior.SetZoomEnabled(host, true);
WorkflowSurfaceBehavior.SetIsEnabled(host, true);
WorkflowSurfaceBehavior.SetWorkflowTree(host, tree);
```

The tree markup is the part layout:

```xml
<local:TreeView x:Class="MyApp.Views.TreeView">
  <local:GridDecorator x:Name="PART_GridDecorator">
    <ScrollViewer x:Name="PART_ScrollViewer" IsScrollInputEnabled="False">
      <Canvas x:Name="PART_Canvas" UnboundedHitTest="True"
              behaviors:ViewPool.ItemsSource="{Binding Helper.VisibleItems}"
              behaviors:ViewPool.TemplateSelector="{StaticResource WorkflowTemplateSelector}" />
    </ScrollViewer>
  </local:GridDecorator>
</local:TreeView>
```

`CustomTemplateSelector : VisualTemplateSelector` returns a visual `NodeView` / `SlotView` / `LinkView`, usually through the tree's `DataTemplate`s. Node is a visual `UserControl` with `Viewbox > Grid 260×180`, title, `PART_InputSlot` and `PART_OutputSlots` (`ItemsControl` + `DataTemplate` row); Slot is a visual `UserControl` with `Viewbox > Path`; Link is a visual `UserControl` with endpoint DPs and `OnRender`. The surface writes the camera matrix to `PART_Canvas.Transform`; the canvas is sized to the viewport and hit-tests unbounded, so its children may live at any world coordinate while the `ScrollViewer` supplies the clip.

⚙ **`x:Name` is the visual name.** On a visual root the generator writes the code-behind field and `VisualElement.Name`; the behavior resolves the visual `Name`. Write `x:Name="PART_*"` once.

⚙ **The tree arrives through `SetWorkflowTree`.** `ViewPool` gives each visible item a view and its `DataContext`; the `DataTemplate` binds position/size (and link endpoints); `WorkflowSlotLayoutBehavior` measures the named slot parts and writes anchors; each `LinkView` publishes its own curve. Node drag is visual: no per-frame `Refresh`/`SyncNow` pull is required.

## Links — one LinkView per link

⚙ `LinkTemplate` builds one `LinkView` per link; the template binds `StartLeft` / `StartTop` / `EndLeft` / `EndTop` to `Sender.Anchor.*` / `Receiver.Anchor.*` and `CanRender` to `IsVisible`, the surface writes `LineColor` / `HighlightColor` / `IsVirtual`, and the view draws the cubic and calls `PublishCurve` in `OnRender`. Hover sets `IsHighlighted`; the template maps it to `HighlightColor`.

⚙ **Curves are world-space.** `Start/End` are world coordinates and the published curve is the same object Core hit-tests; the camera transform is applied only while painting. Do not publish screen coordinates.

⚙ **The hit radius is 6 DIPs and does not scale with zoom.** It is the painted stroke, not the view's box; a link view sets `IsHitTestVisible = false` so it never swallows canvas gestures.

## Input

⚙ **All input goes through Core's route**, `WorkflowInput.For(tree)`. Do not subscribe per-control mouse events for workflow gestures; subscribe `((IInputEvents)vm.GetHelper()).Input.*` instead.

- `VisualHost` routes pointer/wheel/key to the visual root; `WorkflowSurfaceBehavior` converts the pointer through the camera inverse and calls `WorkflowInput`. Drag deltas are divided by zoom. Ctrl+wheel zooms around the pointer; a plain wheel goes through `WorkflowInput.Scroller` unless a subscriber sets `PreventDefault`.
- `VisualHost` turns `WM_MOUSEWHEEL` into the visual `PointerWheel`; `WorkflowSurfaceBehavior.HandleWheel` routes it to Core before deciding zoom or scroll.
- **Target resolution** walks the hit visual up to a node/slot view, then falls back to Core's link hit-test.
- **Enter/leave are synthesized by Core** from target changes; the visual root raises exit when the pointer leaves the surface.
- **Keys** are routed with `PointerTarget` as their target; the visual root is focusable and receives keyboard input after a press.

## Adding your own content to a node

⚙ **The node template is the place to add content.** Put it inside the node card's visual tree and let the camera matrix scale it; the template's `Viewbox` only fits its fixed design grid into the card box. Ports are visual children whose anchors `WorkflowSlotLayoutBehavior` publishes.

## Zoom and pan

⚙ **The visual path zooms one matrix.** Wheel-up is `zoom *= 1.1`, zoom-out is `/ 1.1`, clamped to `0.1..10`; the world point under the pointer stays fixed. At 68% the entire graph — cards, text, glyphs and links — is 68%. The camera matrix is the only zoom transform; there is no per-card `Collapse`. (The node template's `Viewbox` fits a fixed design grid into the card box; it does not scale with zoom.)

⚙ **The visual path runs per-monitor DPI.** `VisualHost` lays the tree out in logical DIPs and paints at `DeviceDpi/96`; the surface converts physical client sizes and decoration offsets through `DpiScale`, so a 260-unit card is 260 DIPs (390 physical at 150%) like WPF. The grid/ruler/minimap/scroll-bar metrics are scaled by the same factor.

⚙ **The camera reports its world viewport to the tree**, so Core's visible set follows pan/zoom: `Viewport = (-(pan+content)/zoom, size/zoom)`.

⚙ **A self-hosted canvas sets the virtualize inset before writing `Viewport`.** The tree has no decorator-hosted ruler to ask, so the reserve is passed explicitly (`RulerReserve` on both axes); skipping it culls nodes still visible under the ruler.

## The link context menu

⚙ **The menu is built in code, not markup.** In the tree's code-behind:

```csharp
WorkflowSurfaceBehavior.SetLinkMenuBuilder(this, static (menu, link) =>
    menu.Items.Add("Delete", null, (_, _) => link.DeleteCommand.Execute(null)));
```

The surface opens the strip on a right-press on a link, positions it, and suspends input routing while it is open. **The generated template adds no entries** — including Delete.

## Item templates — `VeloxDev.WinForms.Templates`

**Generates** `.xaml` + `.xaml.cs` for node, slot, link and tree, and a `.cs` for selector, decorator and minimap.

| Item | Shape | You supply |
|---|---|---|
| tree | visual `UserControl` with visual `GridDecorator > ScrollViewer > Canvas`; hosted in a generic `VisualHost` | the palette, the visual selector resource, the link colours/width |
| node | visual `UserControl` root: `Viewbox > Grid 260×180`, chrome `Border`, title, `PART_InputSlot`, `PART_OutputSlots` ItemsControl | the card shell, title and port-row template |
| slot | visual `UserControl` root + `Viewbox` + `Shapes.Path PART_Glyph` | the glyph path (the template binds `Fill` through `SlotStateBrushConverter`) |
| link | visual `UserControl` with endpoint DPs, `OnRender` publishes/draws the cubic | the curve shape, colours and hit-test publish |
| minimap | `WorkflowMinimapOverlay` | the four palette colours |
| grid decorator | `VisualElement, IWorkflowGridDecorator` with world grid, content and viewport-fixed ruler layers | grid/ruler palette, spacing, major-line frequency |
| template selector | `CustomTemplateSelector : VisualTemplateSelector` | which visual each item kind gets (`NodeView` / `SlotView` / `LinkView`) |

⚙ **The tree references siblings by type name**, so generate all seven items into one namespace. The tree's `DataTemplate`s reference `GridDecorator`, `CustomTemplateSelector`, `NodeView`, `SlotView` and `LinkView`. Missing one is a compile error, not a runtime fallback.

⚙ **The tree's visual selector is a resource.** Declare `<local:CustomTemplateSelector x:Key="WorkflowTemplateSelector" …>` in the tree's resources; `WorkflowSurfaceBehavior` resolves that key by default (an explicit `VisualTemplateSelector` / `SetVisualTemplateSelector` overrides it).

⚙ **Shared helpers:** `XamlRuntime.ParseColor` for palette strings. Each template draws its own rounded rectangle.

⚙ **The decorator is the floating ruler.** `GridDecorator.RulerBand` feeds the virtualizer and its own viewport-fixed ruler layer; there is no separate ruler overlay form.

⚙ **The fully-wired pack.** Every declared parameter, including all four slot colours and `slotPath`, has a consumer here — the pack to read when you want to know what a parameter does.

## Pitfalls

⚙ **No transparent sibling overlay for HWND islands.** A WinForms child window does not composite over its siblings; a viewport-sized transparent layer either covers the grid or hides behind the scroll host. Draw links in the visual tree.

⚙ **Keep `WS_CLIPCHILDREN`/`WS_EX_COMPOSITED` in mind.** The surface applies them through `NativeWindowStyleHelper`; the composited path is skipped above roughly a hundred descendant windows. A surface that worked at demo size can flicker at graph size.

⚙ **Do not hand-roll a second link geometry path.** The view publishes the curve Core hit-tests; a host-side highlight must draw from `ILinkHitTestable.Curve`, or the glow and the click target disagree.

⚙ **Do not simulate zoom by resizing children.** A per-card `Viewbox` or `layout.Scale` collapse double-scales and breaks the inverse-matrix pointer mapping. One camera `Transform` for the whole tree is the contract.

⚙ **The grid does not scale with the camera.** The decorated grid/ruler stay at their screen spacing; only the graph content is matrix-scaled. That matches the 100% baseline and keeps grid lines crisp.
