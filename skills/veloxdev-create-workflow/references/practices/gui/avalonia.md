# Avalonia

`VeloxDev.Avalonia` · targets `netstandard2.0;net6.0;net8.0` · built against Avalonia 11.1, runs on 12.x

⚙ **Reference implementation:** `Examples/Workflow/Avalonia Trimmed/Demo/` — the project sits at `Demo/Demo/`, so the views are at `Demo/Demo/Views/Workflow/` and the ViewModels at `Demo/Demo/ViewModels/Workflow/`. `TreeView.axaml` is the surface and carries the compiled-binding `x:DataType` the template ships as a placeholder.

⚙ **The "Trimmed" in that path means *minimal demo*.** It is a demo-size choice, not a publish setting — see [the rule in SKILL.md](../../../SKILL.md#checking-your-work).

## Writing the surface

The attached properties and the `PART_` names are the same as WPF — see [wpf.md](wpf.md#writing-the-surface) for the markup and the meaning of each property; only the XML namespace differs:

```xml
xmlns:behaviors="clr-namespace:VeloxDev.WorkflowSystem.AttachedBehaviors;assembly=VeloxDev.Avalonia"
```

The canvas is a `ScrollViewer` + `Canvas`, and node and link views bind their `RenderTransform` to `WorkflowCanvasTransformBehavior.Transform`.

⚙ **Keep that binding on the `DataTemplate` root.** With compiled bindings on, an ancestor type is resolved at compile time; without them the lookup is deferred and the resulting exception is swallowed by the view manager. Either way the symptom is a canvas with no nodes and no error.

⚙ **Use an assembly-qualified namespace when the ancestor type sits inside a binding path.** `xmlns:views="using:MyApp.Views"` resolves elements in the same file, but `{Binding $parent[views:MyView].(...)}` can still throw `Unable to resolve type views:MyView ...` while the template is being built. Write `clr-namespace:MyApp.Views;assembly=MyApp` for that prefix — and the same for an attached-property segment: `(clr-namespace:VeloxDev.WorkflowSystem.AttachedBehaviors;assembly=VeloxDev.Avalonia)WorkflowCanvasTransformBehavior.Transform`. The view manager catches the build exception and only logs it; the symptom is again a blank canvas.

## Surface chrome — the grid and the floating rulers

⚙ **Use the template's whole `WorkflowGridDecorator`, not just its grid lines.** The generated surface wraps the scroll viewer in that panel and translates the canvas by the ruler band:

```xml
<local:WorkflowGridDecorator x:Name="PART_GridDecorator">
  <ScrollViewer x:Name="PART_ScrollViewer">...</ScrollViewer>
</local:WorkflowGridDecorator>

<Canvas x:Name="PART_Canvas" ...>
  <Canvas.RenderTransform>
    <TranslateTransform X="{Binding RulerThickness, ElementName=PART_GridDecorator}"
                        Y="{Binding RulerThickness, ElementName=PART_GridDecorator}" />
  </Canvas.RenderTransform>
</Canvas>
```

The decorator has two hit-test-transparent layers: the grid at the bottom and the ruler at `ZIndex="100"` — translucent top/left bands, ticks and world-value labels. Its `RulerBand => RulerThickness` is read by `WorkflowSurfaceBehavior` and forwarded to the virtualizer; a decorator that returns `0` while still covering the top/left with a band culls nodes that are visibly under it. The grid maths use the same `RulerThickness` as the content offset (`GridWorldLeft` / `GridX`), so ticks and nodes stay in one coordinate space while panning.

## Adding your own content to a node

`<Viewbox Stretch="Uniform">` around content pinned to the node's design size, same as WPF — put your controls inside the design canvas and give them design-size coordinates.

```xml
<!-- The DataTemplate sizes this view to the node's collapsed Size; keep the inner Grid fixed. -->
<Viewbox Stretch="Uniform" ClipToBounds="False">
  <Grid Width="260" Height="180" ClipToBounds="False" Background="Transparent">
    <!-- Your content, at design-size coordinates. -->
    <!-- Ports: ZIndex="6" so the half that overlaps the card edge is not covered. -->
  </Grid>
</Viewbox>
```

⚙ **Pin the inner grid to the design size.** The `DataTemplate` already sizes the outer view to the node's **collapsed** `Size`; a `Viewbox` child that also binds `Width="{Binding Size.Width}"` squeezes the design a second time and clips its text when the canvas zooms out.

⚙ **Slots live inside that fixed grid**, with `ZIndex="6"` so the half that overlaps the card is not covered by the card surface. The slot-layout behaviour measures them with `TranslatePoint`, so the `Viewbox` scale is accounted for. Which controls it measures is the name correspondence described in [view-layer.md](../view-layer.md#ports-the-model--view-correspondence); the item template registers its standard input/output pair.

⚙ **The `Viewbox` is the zoom mechanism.** Lay out at design size; the links follow the measured anchors.

## Compiled bindings

⚙ **Set `AvaloniaUseCompiledBindingsByDefault=true`** in your project (the shipped demos do). Without it you must write `x:DataType` explicitly on every binding scope or you get `AVLN2100`.

⚙ **A `DataTemplate` needs a concrete `x:DataType`, not an interface.** The canvas binds `Helper.VisibleItems`, and `IWorkflowTreeViewModel` exposes only `GetHelper()` with no `Helper` property, so an interface `DataType` fails with `AVLN2000`. Point it at your tree ViewModel type.

⚙ The **node** view in the template ships `x:CompileBindings="False"` with an inline note — replace it with your concrete `x:DataType` once your ViewModel exists. The **tree** view ships a placeholder `x:DataType="vm:TreeViewModel"` with `xmlns:vm="using:MyApp.Views"`, which **fails to compile until you repoint it** at your ViewModels namespace. That failure is deliberate: a clear error beats a canvas that silently renders nothing. See [templates.md](../templates.md#checking-the-generated-suite).

## Zoom and pan

⚙ Avalonia has no `PreviewMouseWheel`. The gesture is registered on the scroll viewer with `AddHandler(PointerWheelChangedEvent, handler, RoutingStrategies.Tunnel)`.

⚙ Wheel-up is `Scale *= 1 / 1.1` — `Scale` is a collapse factor.

⚙ **A plain wheel is routed and then applied by the surface**, not by the platform's scroll container: `OnPointerWheel` feeds `WorkflowInput.PointerWheelChanged`, and unless a subscriber sets `PreventDefault` the surface calls its registered `IWorkflowSurfaceScroller` (`WorkflowInput.Scroller`). Ctrl+wheel is the zoom gesture.

⚙ **During a connection drag, `OnPointerMoved` feeds the candidate receiver** with `StandardSetPreviewReceiver(SlotHitTestEx.HitTest(viewModel, x, y))` while `VirtualLink.IsVisible`, and does not route the move as hover.

⚙ **On mobile targets, leave the adapter's gesture handling alone.** It reflects into `GestureRecognizers` to remove the built-in `ScrollGestureRecognizer`; without that, the platform gesture and the zoom fight each other.

## Links

Retained views overriding `Render(DrawingContext)`, with the render-ready gate as the first line.

⚙ Slot anchors are measured in **screen space** and converted with `SlotAnchorFromVisualCenter`, the same frame contract as WPF.

## Focus

⚙ Avalonia's focus API is not WPF's: `IFocusManager.ClearFocus()` **was removed in Avalonia 12**, `IsFocused` exists but there is no `IsKeyboardFocused`, and a control that takes focus can make the surrounding `ScrollViewer` jump — `BringIntoViewOnFocusChange` has to be turned off around any programmatic focus. The adapter is compiled against 11.1 and the demos run on 12.x, so code that touches focus has to be valid on both.

## Item templates — `VeloxDev.Avalonia.Templates`

**Generates** `.axaml` + `.axaml.cs` for node, slot, link and tree (note the extension — `.axaml`, not `.xaml`), and a single `.cs` for selector, decorator and minimap.

⚙ Siblings resolve by default class name in one namespace (`xmlns:local="using:<your -ns>"`), as on WPF.

⚙ **The tree's link context menu is a resource plus a key.** The generated `TreeView.axaml` declares `<ContextMenu x:Key="WorkflowTreeMenu">` (entries use `{ReflectionBinding …}` because the resource has no `x:DataType`) and points `behaviors:WorkflowSurfaceBehavior.LinkMenuKey="WorkflowTreeMenu"` at it; the adapter owns the right-press wiring. Add or remove `<MenuItem>`s in that resource.

⚙ **Two slot parameters are inert on this pack:** `slotColor` (`-sc`) and `slotBorderColor` (`-bc`) are accepted for cross-GUI CLI parity, and the pack's `template.json` says so — the slot view takes its colour from the control's `Foreground`, and it draws a filled path with no border element. Edit the generated slot view when you need either.
