# Windows Forms — workflow surface

`VeloxDev.WinForms` · targets `netframework4.6.1;net5.0-windows;netcoreapp3.0;net8.0-windows`

⚙ **Reference implementation:** `Examples/Workflow/WinForms Trimmed/Demo/Views/Workflow/` — the tree, node, slot
and link views are markup; the tree is a whole visual tree hosted in a `VisualHost`, and one camera matrix
scales the graph. `GridDecorator`, `MinimapOverlay` and `CustomTemplateSelector` are code-only. The Full demo at
`Examples/Workflow/WinForms/Demo/` keeps the same surface subtree inside a visual workbench shell: one
`VisualHost`, `SurfaceName="WorkflowSurface"`, and the title strip, sidebar, inspector, dialogs and status bar are
all Easel XAML too.

⚙ **"Trimmed" means *minimal demo*, not a publish setting:** [the rule in SKILL.md](../../../SKILL.md#checking-your-work).

## Runtime reference is external

The visual XAML runtime is **Easel**, not a WinForms platform XAML. For the full runtime guide — controls,
layout, resources/styles, bindings, themes, visuals, 3D, animation contract, diagnostics and pitfalls — read:

[`Easel/skills/easel-build-winforms-xaml-ui/SKILL.md`](../../../../../Easel/skills/easel-build-winforms-xaml-ui/SKILL.md)

The workflow-specific markup (behaviors, node/slot/link parts, `ViewPool`, connection previews) is in
[winforms-xaml.md](winforms-xaml.md). This page summarizes the workflow surface.

## Project shape

The consuming project needs the Easel generator and its XAML files:

```xml
<ItemGroup>
  <ProjectReference Include="…/Easel.Generator.csproj"
                    OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
  <AdditionalFiles Include="**/*.xaml" />
</ItemGroup>
```

```xml
xmlns:local="clr-namespace:MyApp.Views"
xmlns:behaviors="clr-namespace:VeloxDev.WorkflowSystem.AttachedBehaviors;assembly=VeloxDev.WinForms"
```

A visual view's code-behind names the base with its full namespace:

```csharp
public sealed partial class TreeView : Easel.Visuals.Controls.UserControl
```

Unprefixed XAML types resolve to Easel (`Easel.Visuals.Controls`, `.Controls.Shapes`, `.Visuals`, then the
shared primitives); `local:` is your namespace. `native:`/`System.Windows.Forms` controls are rejected; native
content is only an explicit `NativeControlHost` island.

## Writing the surface

The whole workflow tree is visual. The tree template is a visual `UserControl` (`GridDecorator` /
`ScrollViewer` / `Canvas`, never a workflow base class); the demo hosts it in a generic `VisualHost` and attaches
`WorkflowSurfaceBehavior` to that host. `PART_*` are visual element names.

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

`CustomTemplateSelector : VisualTemplateSelector` returns a visual `NodeView` / `SlotView` / `LinkView`, usually
through the tree's `DataTemplate`s. Node is a visual `UserControl` with `Viewbox > Grid 260×180`, title,
`PART_InputSlot` and `PART_OutputSlots` (`ItemsControl` + `DataTemplate` row); Slot is a visual `UserControl`
with an `Ellipse`; Link is a visual `UserControl` with endpoint DPs and `OnRender`. The surface writes the
camera matrix to `PART_Canvas.Transform`; the canvas is sized to the viewport and hit-tests unbounded, so its
children may live at any world coordinate while the `ScrollViewer` supplies the clip.

⚙ **`x:Name` is the visual name.** On a visual root the generator writes the code-behind field and
`VisualElement.Name`; the behavior resolves the visual `Name`. Write `x:Name="PART_*"` once.

⚙ **The tree arrives through `SetWorkflowTree`.** `ViewPool` gives each visible item a view and its
`DataContext`; the `DataTemplate` binds position/size (and link endpoints); `WorkflowSlotLayoutBehavior`
measures the named slot parts and writes anchors; each `LinkView` publishes its own curve. Node drag is visual:
no per-frame `Refresh`/`SyncNow` pull is required.

## Links — one LinkView per link

⚙ `LinkTemplate` builds one `LinkView` per link. The view sets `UnboundedHitTest = true` so Easel's `Bounds`-based damage cull does not drop it in the negative-world quadrants (the box is canvas-sized and starts at 0,0); hit-testing stays off. The template binds `StartLeft` / `StartTop` / `EndLeft` /
`EndTop` to `Sender.Anchor.*` / `Receiver.Anchor.*` and `CanRender` to `IsVisible`, the surface writes
`LineColor` / `LineThickness` / `IsVirtual`, and the view draws the cubic and calls `PublishCurve` in
`OnRender`. Hover is the demo's: `Examples/Workflow/WinForms Trimmed/Demo/Views/Workflow/LinkView.xaml.cs`
adds `HighlightColor` / `IsHighlighted` and drives them from `IInputEvents`. The generated template ships the
passive view only.

⚙ **Curves are world-space.** `Start/End` are world coordinates and the published curve is the same object Core
hit-tests; the camera transform is applied only while painting. Do not publish screen coordinates.

⚙ **The hit radius is 6 world units; the camera makes its on-screen reach scale with zoom.** It is the painted stroke, not the view's box; a
link view sets `IsHitTestVisible = false` so it never swallows canvas gestures.

## Input

⚙ **All input goes through Core's route**, `WorkflowInput.For(tree)`. Do not subscribe per-control mouse events
for workflow gestures; subscribe `((IInputEvents)vm.GetHelper()).Input.*` instead.

- `VisualHost` routes pointer/wheel/key to the visual root; `WorkflowSurfaceBehavior` converts the pointer
  through the camera inverse and calls `WorkflowInput`. Drag deltas are divided by zoom. Ctrl+wheel zooms
  around the pointer; a plain wheel goes through `WorkflowInput.Scroller` unless a subscriber sets
  `PreventDefault`.
- `VisualHost` turns `WM_MOUSEWHEEL` into the visual `PointerWheel`; `WorkflowSurfaceBehavior.HandleWheel`
  routes it to Core before deciding zoom or scroll.
- **Target resolution** walks the hit visual up to a node/slot view, then falls back to Core's link hit-test.
- **Enter/leave are synthesized by Core** from target changes; the visual root raises exit when the pointer
  leaves the surface.
- **Keys** are routed with `PointerTarget` as their target; the visual root is focusable and receives keyboard
  input after a press.

## Adding your own content to a node

⚙ **The node template is the place to add content.** Put it inside the node card's visual tree and let the
camera matrix scale it; the template's `Viewbox` only fits its fixed design grid into the card box. Ports are
visual children whose anchors `WorkflowSlotLayoutBehavior` publishes.

## Zoom and pan

⚙ **The visual path zooms one matrix.** Wheel-up is `zoom *= 1.1`, zoom-out is `/ 1.1`, clamped to `0.1..10`;
the world point under the pointer stays fixed. At 68% the entire graph — cards, text, glyphs and links — is
68%. The camera matrix is the only zoom transform; there is no per-card `Collapse`. (The node template's
`Viewbox` fits a fixed design grid into the card box; it does not scale with zoom.)

⚙ **The visual path runs per-monitor DPI.** `VisualHost` lays the tree out in logical DIPs and paints at
`DeviceDpi/96`; the surface converts physical client sizes and decoration offsets through `DpiScale`, so a
260-unit card is 260 DIPs (390 physical at 150%) like WPF. Grid/ruler/minimap/scroll-bar metrics scale by the
same factor.

⚙ **The camera reports its world viewport to the tree**, so Core's visible set follows pan/zoom:
`Viewport = (-(pan+content)/zoom, size/zoom)`.

⚙ **Set the virtualize inset before writing `Viewport`.** `tree.SetVirtualizeInset(left: band, top: band)` takes
its band from the decorator's `RulerBand`; a self-hosted surface with no decorator passes the reserve explicitly.
Skipping it culls nodes still visible under the ruler.

## The link context menu

⚙ **The trimmed demo fills the menu in code.** In the form that wires the surface:

```csharp
WorkflowSurfaceBehavior.SetLinkMenuBuilder(this, static (menu, link) =>
    menu.Items.Add("Delete", null, (_, _) => link.DeleteCommand.Execute(null)));
```

The generated tree also declares an empty `VisualContextMenu` resource keyed `LinkContextMenu`; point the
surface at it with `SetLinkMenuKey` when you want the menu in markup. The surface opens the strip on a
right-press on a link, positions it, and suspends input routing while it is open. **The generated template adds
no entries** — including Delete.

## Item templates — `VeloxDev.WinForms.Templates`

**Generates** `.xaml` + `.xaml.cs` for node, slot, link and tree, and a `.cs` for selector, decorator and
minimap.

| Item | Shape | You supply |
| --- | --- | --- |
| tree | visual `UserControl` with visual `GridDecorator > ScrollViewer > Canvas`; hosted in a generic `VisualHost` | the palette, the visual selector resource, the link colours/width |
| node | visual `UserControl` root: `Viewbox > Grid 260×180`, chrome `Border`, title, `PART_InputSlot`, `PART_OutputSlots` ItemsControl | the card shell, title and port-row template |
| slot | visual `UserControl` with an `Ellipse`; its fill and stroke use the slot colour symbols | glyph and colours |
| link | visual `UserControl` with endpoint DPs and `OnRender`; publishes its curve | curve drawing and colors |
| selector | code-only `VisualTemplateSelector` | item type → view mapping |
| decorator | code-only `GridDecorator` | grid/ruler metrics |
| minimap | code-only visual overlay | the minimap frame |

The XAML runtime rules (generator setup, root rules, resources, bindings, styles, themes, 3D, animation
contract) are in the Easel skill; the workflow markup contract (behaviors, parts, `ViewPool`, preview) is in
[winforms-xaml.md](winforms-xaml.md).
