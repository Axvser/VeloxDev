# Avalonia

`VeloxDev.Avalonia` · targets `netstandard2.0;net6.0` · built against Avalonia 11.1, runs on 12.x

⚙ **Reference implementation:** `Examples/Workflow/Avalonia Trimmed/Demo/` — note the extra nesting, the project sits at `Demo/Demo/`, so the views are at `Demo/Demo/Views/Workflow/` and the ViewModels at `Demo/Demo/ViewModels/Workflow/`. `TreeView.axaml` is the surface and carries the compiled-binding `x:DataType` the template ships as a placeholder.

⚙ **The "Trimmed" in that path means *minimal demo*, not trim configuration** — the library is **not** AOT- or trim-safe (`IsTrimmable=false`, and the animation path compiles expression trees at runtime). Do not read publish-time safety into the folder name.

## Writing the surface

The attached properties and the `PART_` names are the same as WPF — see [wpf.md](wpf.md#writing-the-surface) for the markup; only the XML namespace differs:

```xml
xmlns:behaviors="clr-namespace:VeloxDev.WorkflowSystem.AttachedBehaviors;assembly=VeloxDev.Avalonia"
```

The canvas is a `ScrollViewer` + `Canvas`, and node and link views bind their `RenderTransform` to `WorkflowCanvasTransformBehavior.Transform` exactly as on WPF.

⚙ **Keep that binding on the `DataTemplate` root.** On Avalonia the failure is worse than on WPF: with compiled bindings on, an ancestor type is resolved at compile time; without them the lookup is deferred and the resulting exception is swallowed by the view manager. Either way you get a canvas with no nodes and no error.

## Compiled bindings — the thing that actually bites

⚙ **Set `AvaloniaUseCompiledBindingsByDefault=true`** in your project (the shipped demos do). Without it you must write `x:DataType` explicitly on every binding scope or you get `AVLN2100`.

⚙ **A `DataTemplate` needs a concrete `x:DataType`**, not an interface. The canvas binds `Helper.VisibleItems`, and `IWorkflowTreeViewModel` exposes only `GetHelper()` with no `Helper` property, so an interface `DataType` fails with `AVLN2000`. Point it at your tree ViewModel type.

⚙ The **node** view in the template ships `x:CompileBindings="False"` with an inline note — replace it with your concrete `x:DataType` once your ViewModel exists. The **tree** view instead ships a placeholder `x:DataType="vm:TreeViewModel"` with `xmlns:vm="using:MyApp.Views"`, which **fails to compile until you repoint it** at your ViewModels namespace. That failure is deliberate: a clear error beats a canvas that silently renders nothing. See [templates.md](../templates.md#checking-the-generated-suite).

## Adding your own content to a node

`<Viewbox Stretch="Uniform">` around content pinned to the node's design size, same as WPF — put your controls inside the design canvas and give them design-size coordinates.

⚙ **Do not compensate for zoom in your markup**, and do not move the port visuals; the links follow the measured anchors.

## Zoom and pan

⚙ Avalonia has no `PreviewMouseWheel`. The gesture is registered on the scroll viewer with `AddHandler(PointerWheelChangedEvent, handler, RoutingStrategies.Tunnel)`.

⚙ If you write your own zoom, wheel-up is `Scale *= 1 / 1.1` — `Scale` is a collapse factor.

⚙ **On mobile targets, leave the adapter's gesture handling alone.** It reflects into `GestureRecognizers` to remove the built-in `ScrollGestureRecognizer`; without that, the platform gesture and the zoom fight each other.

## Links

Retained views overriding `Render(DrawingContext)`, with the render-ready gate as the first line.

⚙ Slot anchors are measured in **screen space** and converted with `SlotAnchorFromVisualCenter`, the same frame contract as WPF.

## Focus, if you build on it

⚙ Avalonia's focus API is not WPF's: `IFocusManager.ClearFocus()` **was removed in Avalonia 12**, `IsFocused` exists but there is no `IsKeyboardFocused`, and a control that takes focus can make the surrounding `ScrollViewer` jump — `BringIntoViewOnFocusChange` has to be turned off around any programmatic focus. The adapter is compiled against 11.1 and the demos run on 12.x, so code that touches focus has to be valid on both.

## Item templates — `VeloxDev.Avalonia.Templates`

**Generates** `.axaml` + `.axaml.cs` for node, slot, link and tree (note the extension — `.axaml`, not `.xaml`), and a single `.cs` for selector, decorator and minimap.

⚙ Siblings resolve by default class name in one namespace (`xmlns:local="using:<your -ns>"`), as on WPF.

⚙ **Two slot parameters are silently inert on this pack:** `slotColor` (`-sc`) and `slotBorderColor` (`-bc`) are declared without being wired. This is the only pack where two slot colours both do nothing — edit the generated slot view directly.
