# WPF

`VeloxDev.WPF` · targets `net461;net5.0-windows;netcoreapp3.0`

⚙ **Reference implementation:** `Examples/Workflow/WPF Trimmed/Demo/Views/Workflow/` — `TreeView.xaml` (the surface and both templates), `NodeView.xaml`, `SlotView.xaml`, `LinkView.xaml`, `CustomTemplateSelector.cs`, `WorkflowGridDecorator.cs`, `MinimapOverlay.cs`. ViewModels beside it at `Demo/ViewModels/Workflow/`. The demo names its selector and decorator `CustomTemplateSelector` / `WorkflowGridDecorator`; the templates use `TemplateSelector` / `GridDecorator`, so do not be thrown by the difference.

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
             behaviors:WorkflowSurfaceBehavior.MinimapOverlayName="PART_MinimapOverlay">
```

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

⚙ **`Width`/`Height`/`Canvas.Left`/`Canvas.Top` bind to `Size` and `Anchor` — the collapsed values**, so you never apply the zoom yourself.

⚙ **The `RenderTransform` binding must be on the `DataTemplate` root**, exactly as above. Move it onto an element inside `NodeView` and it resolves against the item's own tree and finds nothing — the canvas will pan in the model and not on screen.

`WorkflowNodeDragBehavior` adds `CoordinateHostName` / `CoordinateHostType` (default `Canvas`); `WorkflowSlotLayoutBehavior` adds `SlotNames` / `SlotEnumeratorNames`. Set them only if you rename the parts.

## Adding your own content to a node

`NodeView.xaml` is a `<Viewbox Stretch="Uniform">` wrapping content pinned to the node's **design size** (the demo uses 260 × 180). Put your controls inside that design canvas and give them design-size coordinates — the `Viewbox` scales the whole thing by `1/Scale` for you.

⚙ **Do not compensate for zoom in your markup.** The `Viewbox` is why the factor is exactly `1/Scale` and why you can lay out at design size.

⚙ **Do not move the port visuals.** Slot positions come from `WorkflowSlotLayoutBehavior` measuring the `SlotView`; the links follow the measured anchors, not your layout.

## Zoom and pan

⚙ The gesture is `PreviewMouseWheel` on the scroll viewer, gated on `Keyboard.Modifiers == ModifierKeys.Control` — without Ctrl it scrolls normally.

⚙ If you write your own zoom, wheel-up is `Scale *= 1 / 1.1`. `Scale` is a collapse factor, so zoom-in *decreases* it.

## Links

⚙ WPF has no surface limit, so the shipped link view draws directly in canvas coordinates from `OnRender(DrawingContext)` — the simplest of the seven. Keep the render-ready gate as the first line:

```csharp
protected override void OnRender(DrawingContext dc)
{
    if (DataContext is IWorkflowLinkViewModel link && !link.IsRenderReady()) return;
    // four-point golden-stub polyline from Sender.Anchor to Receiver.Anchor
}
```

⚙ Slot anchors on this adapter are measured in **screen space** and converted with `SlotAnchorFromVisualCenter`. That is why links stay aligned through a pan: the canvas itself is *not* translated for the child views, so the visual centre genuinely is a screen coordinate here. If you rewrite the layout behaviour and it starts offsetting every link by the pan amount, this is the line you changed.

⚙ **There is no link-interaction helper in this adapter.** Per-link pointer and key handling was built, tested and then reverted — the shipped tree has no link hit-testing, so plan any "click a link" feature into the node or the surface.

⚙ `System.Windows.Shapes.Path` versus `System.IO.Path`: unlike WinUI, the WPF demo does not need an alias, but code-first views that use both namespaces will.

## Item templates — `VeloxDev.WPF.Templates`

**Generates** a markup + code-behind pair for node, slot, link and tree, and a single `.cs` for selector, decorator and minimap — eleven files.

⚙ **Siblings resolve by default class name through a markup namespace alias**, so all seven items must be generated into **one** namespace: the tree maps `xmlns:workflowViews="clr-namespace:<your -ns>"` and refers to `workflowViews:NodeView`, `workflowViews:LinkView`, `workflowViews:GridDecorator`, `workflowViews:TemplateSelector`, `workflowViews:MinimapOverlay`.

⚙ **`slotBorderColor` (`-bc`) is accepted and discarded** on this pack — the slot template declares it without wiring it. If a slot border colour refuses to appear, edit the generated file rather than the command line.

⚙ **The tree's inline `LinkTemplate` hardcodes `LineColor="#DDFFFFFF"`.** The `linkColor` symbol reaches `LinkView`, so passing `-lc` changes the generated link view but not the tree's inline template — change both, or drop the inline one and reference `LinkView`'s own default.

⚙ Colours are substituted into frozen-pen string literals (`CreateFrozenPen("TemplateMinorGridColor", 1)`) and into `FrameworkPropertyMetadata` defaults. If you hand-edit a generated decorator, keep those as string literals rather than refactoring them into constants — nothing else depends on the form, but it is what a later regeneration will expect.
