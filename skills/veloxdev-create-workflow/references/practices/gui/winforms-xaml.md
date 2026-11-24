# WinForms XAML — the workflow surface

`VeloxDev.WinForms` renders the workflow tree with **Easel**, its own visual XAML runtime: one `VisualHost`
(HWND), `VisualElement` controls, a Roslyn generator for `.xaml`, and the adapter's attached behaviors binding
that view layer to the workflow model.

**For the runtime itself — controls, layout, styles, resources, bindings, themes, visuals, 3D, animation
contract, diagnostics and pitfalls — read the Easel skill:
[`Easel/skills/easel-build-winforms-xaml-ui/SKILL.md`](../../../../../Easel/skills/easel-build-winforms-xaml-ui/SKILL.md).**
This page only covers the workflow-specific parts: the surface behavior, node/slot/link templates, `ViewPool`,
connection previews and the template pack.

## Required project shape

The consuming project needs the generator and its XAML files:

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

The item-template pack generates the tree, node, slot and link views plus the code-only selector/decorator/
minimap. See [templates.md](../templates.md) for the commands; this page explains how the markup connects to
the model.

## Surface

The whole workflow tree is visual. The demo hosts a generated visual `UserControl` in a generic `VisualHost`
and attaches `WorkflowSurfaceBehavior` to the host:

```csharp
var treeView = new TreeView();                       // visual UserControl (generated tree item)
var host = new VisualHost { Dock = DockStyle.Fill, Root = treeView };
shell.Controls.Add(host);                             // HUD/minimap are visual siblings inside the tree

WorkflowSurfaceBehavior.SetCanvasName(host, "PART_Canvas");
WorkflowSurfaceBehavior.SetScrollViewerName(host, "PART_ScrollViewer");
WorkflowSurfaceBehavior.SetGridDecoratorName(host, "PART_GridDecorator");
WorkflowSurfaceBehavior.SetZoomEnabled(host, true);
WorkflowSurfaceBehavior.SetIsEnabled(host, true);
WorkflowSurfaceBehavior.SetWorkflowTree(host, tree);
```

When the root also carries a visual workbench shell, keep one `VisualHost` and name the surface subtree:

```csharp
var host = new VisualHost { Dock = DockStyle.Fill, Root = new WorkbenchView() };
WorkflowSurfaceBehavior.SetSurfaceName(host, "WorkflowSurface");   // x:Name on the surface UserControl
WorkflowSurfaceBehavior.SetIsEnabled(host, true);
```

Parts, input and the viewport are then resolved inside that subtree; the public camera API still speaks host
coordinates, so `SurfaceToWorld` / `WorldToSurface` remain the conversion points.

`WorkflowSurfaceBehavior` owns the camera (one matrix on `PART_Canvas`), routes input into Core's
`WorkflowInput`, materializes items through `ViewPool`, and resolves the template selector. `PART_*` are
visual element names; `x:Name` writes `VisualElement.Name`.

The tree template is the WPF-shaped surface:

```xml
<local:TreeView x:Class="MyApp.Views.TreeView">
  <local:GridDecorator x:Name="PART_GridDecorator"
                       GridSpacing="40" MajorLineEvery="5" RulerThickness="28"
                       DrawSurface="False" ClipToBounds="False">
    <ScrollViewer x:Name="PART_ScrollViewer" IsScrollInputEnabled="False">
      <Canvas x:Name="PART_Canvas" UnboundedHitTest="True" ClipToBounds="False"
              behaviors:ViewPool.ItemsSource="{Binding Helper.VisibleItems}"
              behaviors:ViewPool.TemplateSelector="{StaticResource WorkflowTemplateSelector}" />
    </ScrollViewer>
  </local:GridDecorator>
</local:TreeView>
```

The surface writes the camera matrix to `PART_Canvas.Transform`; the canvas is sized to the viewport and
hit-tests unbounded, so children may live at any world coordinate while the `ScrollViewer` supplies the clip.

## Selector and resources

```xml
<local:TreeView.Resources>
  <DataTemplate x:Key="NodeTemplate"><local:NodeView /></DataTemplate>
  <DataTemplate x:Key="SlotTemplate"><local:SlotView /></DataTemplate>
  <DataTemplate x:Key="LinkTemplate"><local:LinkView /></DataTemplate>
  <local:CustomTemplateSelector x:Key="WorkflowTemplateSelector"
                                NodeTemplate="{StaticResource NodeTemplate}"
                                SlotTemplate="{StaticResource SlotTemplate}"
                                LinkTemplate="{StaticResource LinkTemplate}" />
</local:TreeView.Resources>
```

A custom selector derives from `VisualTemplateSelector` (not a workflow base) and returns a visual element:

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

`WorkflowSurfaceBehavior.VisualTemplateSelectorKey` defaults to `WorkflowTemplateSelector` and resolves it from
the tree root's resources, then the host's.

## Node, slot and link

The generated node is an elastic card: `Viewbox > Grid` at a fixed design size, with a title and the two default
parts. A node that needs extra slots names them and registers them with the same behavior:

```xml
<local:NodeView
    xmlns:behaviors="clr-namespace:VeloxDev.WorkflowSystem.AttachedBehaviors;assembly=VeloxDev.WinForms"
    behaviors:WorkflowSlotLayoutBehavior.IsEnabled="True"
    behaviors:WorkflowSlotLayoutBehavior.SlotNames="PART_InputSlot"
    behaviors:WorkflowSlotLayoutBehavior.SlotEnumeratorNames="PART_OutputSlots"
    behaviors:WorkflowSlotLayoutBehavior.CoordinateHostName="PART_Canvas"
    ClipToBounds="False">
  <Grid>
    <Grid behaviors:WorkflowNodeDragBehavior.IsEnabled="True"
          behaviors:WorkflowNodeDragBehavior.CoordinateHostName="PART_Canvas" />
    <local:SlotView x:Name="PART_InputSlot"
                    behaviors:WorkflowSlotConnectionBehavior.IsEnabled="True" />
    <ItemsControl x:Name="PART_OutputSlots" />
  </Grid>
</local:NodeView>
```

- `SlotNames` / `SlotEnumeratorNames` are comma-separated **visual element names**; the default is
  `PART_InputSlot` / `PART_OutputSlots`.
- A node drags only when some element marks `WorkflowNodeDragBehavior.IsEnabled="True"`; without it the card
  does not drag. Only that element and its descendants start the drag.
- A slot connects only when its view root marks `WorkflowSlotConnectionBehavior.IsEnabled="True"`.
- `WorkflowSlotLayoutBehavior` measures the named parts and writes their anchors. A slot whose anchor is still
  `NaN` is not measured yet; its link draws nothing.
- The slot view's code-behind is only `InitializeComponent()`. Its glyph fill and outline are the template's
  slot colour symbols.

The link view is one visual per link. The template binds `StartLeft` / `StartTop` / `EndLeft` / `EndTop` to
`Sender.Anchor.*` / `Receiver.Anchor.*` and `CanRender` to `IsVisible`; the surface writes `LineColor` /
`LineThickness` / `IsVirtual`; the view draws the cubic and calls `PublishCurve` in `OnRender`. Hover
highlighting is demo-only (`Examples/Workflow/WinForms Trimmed/.../LinkView.xaml.cs`). Curves are
world-space; the camera transforms only while painting. The hit radius is 6 DIPs and does not scale with zoom;
the link view sets `IsHitTestVisible = false` so it does not swallow canvas gestures.

## Connection preview

`WorkflowInput.For(tree)` carries `ConnectionSender`, `PreviewReceiver`, `PreviewState`, `PreviewCheck` and
the tree's `VirtualLink` (`IConnectionPreviewLink`). The adapters feed the candidate slot on every pointer move;
the virtual link publishes its stage so a link view can paint `ValidTarget` / `InvalidTarget`.
`StandardCheckConnection(sender, receiver)` is the pure rule set. `PreviewValidating` may turn an accepted
candidate red for one drag; the release-time decision is unchanged. Structural changes go through
`tree.SubmitCommand` / `tree.GetHelper().Submit` so undo and observers stay in step.

## Input route

All input goes through Core's route: `((IInputEvents)vm.GetHelper()).Input.*`. Do not subscribe per-control
mouse handlers for workflow gestures. `VisualHost` routes pointer/wheel/key to the visual root;
`WorkflowSurfaceBehavior` converts through the camera inverse, divides drag deltas by zoom, and routes to
`WorkflowInput`. Ctrl+wheel zooms around the pointer; a plain wheel goes through `WorkflowInput.Scroller`
unless a subscriber sets `PreventDefault`. Keys route with `PointerTarget` as their target; the visual root is
focusable and receives keyboard input after a press.

Target resolution walks the hit visual up to a node/slot view, then falls back to Core's link hit-test.
Enter/leave are synthesized by Core from target changes; the root raises exit when the pointer leaves.

## Node content, zoom and DPI

- Add content inside the node template's fixed design grid; the template `Viewbox` fits it into the card box.
- The visual path zooms one camera matrix: wheel-up `×1.1`, down `/1.1`, clamped `0.1..10`, keeping the world
  point under the pointer fixed. There is no per-card collapse.
- The visual path lays out in logical DIPs and paints at `DeviceDpi/96`; the 260-unit card is 260 DIPs at any
  monitor scale. Grid/ruler/minimap metrics scale with the same DpiScale.
- The camera reports its world viewport to the tree so Core's visible set follows pan/zoom. Set the
  virtualize inset with `tree.SetVirtualizeInset(left: band, top: band)` before writing `Viewport`, or it culls
  nodes still visible under the ruler.

## Link context menu

The menu is built in code, not markup:

```csharp
WorkflowSurfaceBehavior.SetLinkMenuBuilder(this, static (menu, link) =>
    menu.Items.Add("Delete", null, (_, _) => link.DeleteCommand.Execute(null)));
```

The surface opens the strip on a right-press on a link and suspends input while it is open. The generated
template adds no entries, including Delete.

## Verification

```bash
dotnet test Src/Test/Headless/VeloxDev.WinForms.Headless.Tests/VeloxDev.WinForms.Headless.Tests.csproj
powershell -NoProfile -ExecutionPolicy Bypass -File Src/Test/Harness/verify-workflow-item-templates-all.ps1 -Platform WinForms -Strict
dotnet build "Examples/Workflow/WinForms Trimmed/Demo/Demo.csproj"
```

The template harness proves the seven item templates generate and build, not that they draw; run the Trimmed
demo for the visual path. For the runtime's own tests and the feature catalogue, use
`Easel/Src/Test/Unit/Easel.Test` and `Easel/Examples/XamlGallery` as described in the Easel skill.
