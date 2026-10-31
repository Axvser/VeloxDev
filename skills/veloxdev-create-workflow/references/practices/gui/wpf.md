# WPF

`VeloxDev.WPF` · targets `net461;net5.0-windows;netcoreapp3.0;net8.0-windows`

⚙ **Reference implementation:** `Examples/Workflow/WPF Trimmed/Demo/Views/Workflow/` — `TreeView.xaml` (the surface and both templates), `NodeView.xaml`, `SlotView.xaml`, `LinkView.xaml`, `CustomTemplateSelector.cs`, `WorkflowGridDecorator.cs`, `MinimapOverlay.cs`. ViewModels beside it at `Demo/ViewModels/Workflow/`. The demo names its selector and decorator `CustomTemplateSelector` / `WorkflowGridDecorator`; the templates use `TemplateSelector` / `GridDecorator`.

⚙ **The "Trimmed" in that path means *minimal demo*.** It is a demo-size choice, not a publish setting — see [the rule in SKILL.md](../../../SKILL.md#checking-your-work).

## Writing the surface

```xml
<UserControl x:Class="MyApp.Views.TreeView"
             xmlns:local="clr-namespace:MyApp.Views"
             xmlns:workflowViews="clr-namespace:MyApp.Views"
             xmlns:behaviors="clr-namespace:VeloxDev.WorkflowSystem.AttachedBehaviors;assembly=VeloxDev.WPF"
             behaviors:WorkflowSurfaceBehavior.IsEnabled="True"
             behaviors:WorkflowSurfaceBehavior.ZoomEnabled="True"
             behaviors:WorkflowSurfaceBehavior.ScrollViewerName="PART_ScrollViewer"
             behaviors:WorkflowSurfaceBehavior.CanvasName="PART_Canvas"
             behaviors:WorkflowSurfaceBehavior.GridDecoratorName="PART_GridDecorator"
             behaviors:WorkflowSurfaceBehavior.PointerPressSourceName="PART_SurfaceBorder"
             behaviors:WorkflowSurfaceBehavior.MinimapOverlayName="PART_MinimapOverlay"
             behaviors:WorkflowSurfaceBehavior.LinkMenuKey="LinkContextMenu">
```

The attached properties are the surface's contract: `IsEnabled` is the master switch; `ScrollViewerName` and `CanvasName` name the two controls that define the viewport; `GridDecoratorName` and `MinimapOverlayName` name the two decorator implementations; `PointerPressSourceName` names the element whose blank-area press starts panning; `ZoomEnabled` enables the Ctrl+wheel gesture; `LinkMenuKey` names the link menu resource.

The tree's two `DataTemplate`s are where node positioning is declared:

```xml
<DataTemplate x:Key="NodeTemplate">
    <workflowViews:NodeView Width="{Binding Size.Width}"
                            Height="{Binding Size.Height}"
                            Canvas.Left="{Binding Anchor.Horizontal}"
                            Canvas.Top="{Binding Anchor.Vertical}"
                            Panel.ZIndex="{Binding Anchor.Layer}"
                            RenderTransform="{Binding RelativeSource={RelativeSource AncestorType={x:Type local:TreeView}},
                                                      Path=(behaviors:WorkflowCanvasTransformBehavior.Transform)}" />
</DataTemplate>
```

⚙ **`Width`/`Height`/`Canvas.Left`/`Canvas.Top` bind to `Size` and `Anchor` — the collapsed values**, so the template never applies zoom itself.

⚙ **The `RenderTransform` binding sits on the `DataTemplate` root.** An ancestor lookup from inside `NodeView` resolves against the item's own tree and finds nothing; the canvas then pans in the model and stays still on screen.

`WorkflowNodeDragBehavior` exposes `CoordinateHostName` / `CoordinateHostType` (the coordinate host, default type `Canvas`); `WorkflowSlotLayoutBehavior` exposes `SlotNames` / `SlotEnumeratorNames` (the name-based port correspondence) and the same coordinate-host pair. See [view-layer.md](../view-layer.md#ports-the-model--view-correspondence).

## Adding your own content to a node

`NodeView.xaml` is a `<Viewbox Stretch="Uniform">` wrapping content pinned to the node's **design size** (260 × 180). Put your controls inside that design canvas and give them design-size coordinates — the `Viewbox` scales the whole thing by `1/Scale`.

```xml
<!-- The template sizes this view to the node's collapsed Size; the inner Grid stays at design size. -->
<Viewbox Stretch="Uniform" ClipToBounds="False">
  <Grid Width="260" Height="180" ClipToBounds="False" Background="Transparent">
    <!-- Your content, at design-size coordinates. -->
  </Grid>
</Viewbox>
```

⚙ **Keep the inner Grid at design size.** A `Viewbox` child whose `Width`/`Height` follow the collapsed `Size` squeezes the design a second time and clips its text when the canvas zooms out.

⚙ **The `Viewbox` is the zoom mechanism.** Lay out at design size and let the container apply the factor.

⚙ **Ports are measured controls.** Slot positions come from `WorkflowSlotLayoutBehavior` measuring the `SlotView`; the links follow the measured anchors.

## Zoom and pan

⚙ The gesture is `PreviewMouseWheel` on the scroll viewer, gated on `Keyboard.Modifiers == ModifierKeys.Control` — without Ctrl it scrolls normally.

⚙ Wheel-up is `Scale *= 1 / 1.1`. `Scale` is a collapse factor, so zoom-in *decreases* it.

⚙ **The surface owns both wheel jobs.** Ctrl+wheel zooms; a plain wheel is routed as `PointerWheelChanged` and then applied by the surface itself (`state.Scroller?.ScrollBy(0, e.Delta)`) unless a subscriber set `PreventDefault`. The scroller is registered at `WorkflowInput.For(tree).Scroller` while the surface is attached.

⚙ **While `VirtualLink.IsVisible`, `OnPreviewMouseMove` reports the candidate receiver** with `StandardSetPreviewReceiver(SlotHitTestEx.HitTest(viewModel, x, y))` and returns before hover routing. See [model.md](../model.md#the-connection-preview).

## Links

⚙ WPF has no surface limit, so the shipped link view draws directly in canvas coordinates from `OnRender(DrawingContext)`. Keep the render-ready gate as the first line:

```csharp
protected override void OnRender(DrawingContext dc)
{
    if (DataContext is IWorkflowLinkViewModel link && !link.IsRenderReady()) return;
    // cubic Bézier from Sender.Anchor to Receiver.Anchor, both control points pulled along
    // each port's own outward normal
}
```

⚙ Slot anchors on this adapter are measured in **screen space** and converted with `SlotAnchorFromVisualCenter`. The canvas itself is not translated for the child views, so the visual centre genuinely is a screen coordinate here.

⚙ **Link input is Core's, forwarded by this adapter.** The surface translates the platform's pointer and keys into the standard input and routes them through `WorkflowInput.For(tree)`, resolving which link is under the pointer with `HitTestVisibleLinks` and taking keyboard focus when one is hovered. A host subscribes the routed `KeyDown`, checks the link, and runs `DeleteCommand`: the trimmed demo subscribes on the link view itself and reads its own `DataContext`; the full demo uses the tree-level shape (`Input.KeyDown`, then `e.Target is IWorkflowLinkViewModel`). The right-press link menu is wired through `WorkflowSurfaceBehavior.LinkMenuKey` plus a `ContextMenu` resource (closed when `GetHelper().LinkRemoved` names that link). Hover appearance is the host's — subscribe the link's own `IInputEvents` (see [view-layer.md](../view-layer.md#making-a-link-interactive)).

⚙ `System.Windows.Shapes.Path` versus `System.IO.Path`: this demo does not need an alias, but code-first views that use both namespaces will.

## Node drag-in — `NodeDropSurface`

Drag-in previews do not go through the view pool or a data template. Put a subclass of `NodeDropSurface` inside the decorator (as the last child, above the content) and override one method:

```xml
<local:WorkflowGridDecorator x:Name="PART_GridDecorator">
    <ScrollViewer ...>...</ScrollViewer>
    <local:MyNodeDropSurface />
</local:WorkflowGridDecorator>
```

```csharp
public sealed class MyNodeDropSurface : NodeDropSurface
{
    protected override void DrawNode(NodeDropDrawingContext context)
    {
        // Viewport coordinates: context.Bounds is the draft's collapsed rectangle.
        context.DrawingContext.DrawRectangle(null, _outlinePen, context.Bounds);
        // context.Node, context.Decorator and context.ScaleX/Y are available for richer previews.
    }
}
```

Start a drag with the base's own entry:

```csharp
var drop = WorkflowNodeDrop.For(tree);
var draft = new NodeDefaultViewModel { Size = new VeloxDev.WorkflowSystem.Size(260, 180) };
nodeDropSurface.BeginDrop(drop, draft, pointerWorld);
```

⚙ **The base owns the geometry.** It finds the nearest `IWorkflowGridDecorator` ancestor, maps the draft's collapsed `Anchor` through `ScrollOffsetX/Y` and `ContentOffsetX/Y` plus `RulerBand`, and divides the draft's world `Size` by the tree's `Scale` before handing you `Bounds`. The tree comes from `Tree` when set, otherwise from `DataContext`.

⚙ **It is a hit-test transparent overlay.** `IsHitTestVisible` is false so the preview never steals the pointer; keep it that way. The base invalidates itself on pointer moves, on the decorator's layout (scroll) and on the tree's layout property changes (zoom).

⚙ **Core still owns the gesture.** `BeginDrop` is only a convenience over `WorkflowNodeDrop.Begin`; release, leave-to-cancel and the create command stay in `WorkflowInput`, exactly as described in [input.md](../input.md#dragging-a-node-in).

## Item templates — `VeloxDev.WPF.Templates`

**Generates** a markup + code-behind pair for node, slot, link and tree, and a single `.cs` for selector, decorator and minimap — eleven files.

⚙ **Siblings resolve by default class name through a markup namespace alias.** Generate all seven items into **one** namespace: the tree maps `xmlns:workflowViews="clr-namespace:<your -ns>"` and refers to `workflowViews:NodeView`, `workflowViews:LinkView`, `workflowViews:GridDecorator`, `workflowViews:TemplateSelector`, `workflowViews:MinimapOverlay`.

⚙ **The tree's link context menu is a resource plus a key.** The generated `TreeView.xaml` declares an **empty** `<ContextMenu x:Key="LinkContextMenu">` and points `behaviors:WorkflowSurfaceBehavior.LinkMenuKey="LinkContextMenu"` at it; the adapter owns the right-press wiring. Add or remove `<MenuItem>`s in that resource — each is bound to the pressed link, so `<MenuItem Header="Delete" Command="{Binding DeleteCommand}"/>` (or your own command) is the whole entry.

⚙ **`slotBorderColor` (`-bc`) is accepted and discarded** on this pack. The slot template declares it for cross-GUI CLI parity and says so in the pack's own `template.json`; the slot view draws a filled path and has no border element. Edit the generated file when you need a border.

⚙ **The tree's inline `LinkTemplate` hardcodes `LineColor="#DDFFFFFF"`.** The `linkColor` symbol reaches `LinkView`, so `-lc` changes the generated link view but not the tree's inline template — change both, or reference `LinkView`'s own default.

⚙ **Colours are substituted into frozen-pen string literals** (`CreateFrozenPen("TemplateMinorGridColor", 1)`) and into `FrameworkPropertyMetadata` defaults. Keep those as string literals when you hand-edit a generated decorator; a later regeneration expects that form.
