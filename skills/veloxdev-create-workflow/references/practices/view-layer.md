# Your views

The canvas you put on screen is assembled from seven view roles, and the item templates generate all seven for you — see [templates.md](templates.md). This file is what you need in order to **customize and debug** them: how they are composed, which attached properties and interfaces carry the contract, and where each extension point is.

⚙ **Default: generate all seven roles from the item templates.** The decorator is the floating ruler; the minimap is the thumbnail. The generated node view is the elastic container (`Viewbox` on WPF/Avalonia/WinUI/Jalium, layout-only scaling on MAUI/WinForms, the CSS transform on Razor); keep that container when you add content. Each `gui/<gui>.md` has the code.

Open your GUI's reference alongside this one — it carries that framework's attached properties, parameters and template defaults.

## The seven roles

| Role | Class | Job |
|---|---|---|
| Surface host | `WorkflowSurfaceBehavior` | resolves the named child controls, feeds scroll and viewport, starts panning, hooks zoom, forwards link interaction and shows the link menu, and registers `WorkflowInput.Scroller` |
| Canvas transform | `WorkflowCanvasTransformBehavior` — **Jalium spells it `WorkflowSurfaceBehavior.CanvasTransform`**; MAUI has no carrier | owns the pan offset your node and link views bind to |
| View pool | `ViewPool` / `ViewManager` | object-pooled views over the visible-items collection |
| Node drag | `WorkflowNodeDragBehavior` | a drag on a node executes `MoveCommand` |
| Slot connection | `WorkflowSlotConnectionBehavior` | press one slot, release on another, connect them; on the frameworks without a surface pointer-move hook it also reports the candidate receiver |
| Slot layout | `WorkflowSlotLayoutBehavior` | measures each port and writes its anchor back |
| Grid decorator / minimap | `IWorkflowGridDecorator` / `IWorkflowMinimapOverlay` | grid, ruler and thumbnail |

⚙ **The first six roles are supplied by the adapter. The decorator and minimap implement Core interfaces and are written or generated for the host.** `IWorkflowGridDecorator` and `IWorkflowMinimapOverlay` live in Core (namespace `VeloxDev.WorkflowSystem`); the surface pushes scroll and content offsets into them each pass, and the minimap's interface derives from the decorator's because it needs the same numbers. `RulerBand` is the one member that affects more than drawing: the surface forwards it to the virtualizer so nodes under the floating ruler are not culled.

⚙ **A grid decorator draws the floating ruler bands as part of the surface chrome.** Draw them in a hit-test-transparent top layer (the templates use `ZIndex="100"`), return the band thickness from `RulerBand`, and offset the world canvas by that band. The XAML adapters do it with `Canvas.RenderTransform`; the grid maths use the same band as their content offset.

## Wiring the surface

The surface is the control that owns the scroll viewer and the canvas. It resolves everything it needs by **name** out of your markup.

```xml
<UserControl xmlns:behaviors="clr-namespace:VeloxDev.WorkflowSystem.AttachedBehaviors;assembly=VeloxDev.WPF"
             behaviors:WorkflowSurfaceBehavior.IsEnabled="True"
             behaviors:WorkflowSurfaceBehavior.ZoomEnabled="True"
             behaviors:WorkflowSurfaceBehavior.ScrollViewerName="PART_ScrollViewer"
             behaviors:WorkflowSurfaceBehavior.CanvasName="PART_Canvas"
             behaviors:WorkflowSurfaceBehavior.GridDecoratorName="PART_GridDecorator"
             behaviors:WorkflowSurfaceBehavior.PointerPressSourceName="PART_SurfaceBorder"
             behaviors:WorkflowSurfaceBehavior.MinimapOverlayName="PART_MinimapOverlay"
             behaviors:WorkflowSurfaceBehavior.LinkMenuKey="LinkContextMenu">
```

| Attached property | Meaning | Default |
|---|---|---|
| `IsEnabled` | Master switch. The surface reads every other property only while this is `true`. | `false` |
| `ScrollViewerName` | Name of the scroll viewer whose offsets define the measured viewport. | `null` |
| `CanvasName` | Name of the canvas/panel the view pool fills. | `null` |
| `GridDecoratorName` | Name of the element implementing `IWorkflowGridDecorator`. | `null` |
| `MinimapOverlayName` | Name of the element implementing `IWorkflowMinimapOverlay`. | `null` |
| `PointerPressSourceName` | Name of the element whose blank-area press starts panning. | `null` |
| `ZoomEnabled` | Enables the Ctrl+wheel zoom gesture. | `false` |
| `LinkMenuKey` | Resource key of the link context menu, resolved after the resource dictionary exists. | `null` |

⚙ **The names are the contract.** Rename a `PART_` element and update the attached property in the same edit; the surface performs a name lookup and a missing name disables that part silently. The templates emit `PART_ScrollViewer`, `PART_Canvas`, `PART_SurfaceBorder`, `PART_GridDecorator` and `PART_MinimapOverlay`; keep them unless you have a reason not to.

⚙ **Use the attached-property API the platform gives you.** WinForms uses `WorkflowSurfaceBehavior.SetIsEnabled(control, true)` and friends; Blazor uses component parameters. Your GUI's reference has the exact spelling.

### The binding rule

Node and link views bind their pan offset to the host rather than being moved one by one:

```xml
RenderTransform="{Binding RelativeSource={RelativeSource AncestorType={x:Type local:TreeView}},
                          Path=(behaviors:WorkflowCanvasTransformBehavior.Transform)}"
```

⚙ **Put this binding on the `DataTemplate` root.** An ancestor lookup made from inside an item resolves against that item's own visual tree and finds nothing — the canvas then pans in the model and stays still on screen.

⚙ **Jalium carries the same value under a different name** — `WorkflowSurfaceBehavior.CanvasTransform`, republished as a CLR property by the tree view's own class, because this platform's bindings cannot read an attached property. Same value, same notifications; a `RenderTransform` bound to it works exactly as above.

⚙ **MAUI ships no carrier.** It pools links into a single overlay, so there is no per-view transform to set. Your GUI's reference says which applies.

## What a node view is made of

Three layers, and where you put your own content depends on which one you mean:

```text
outer collapsed box      positioned at the node's Anchor, sized to its Size
  └── scale container    scales its child by 1 / Scale
       └── design canvas chrome, text and ports, laid out at design coordinates
```

⚙ **Put your content in the design canvas**, and give it design-size coordinates. The scale container handles zoom for you — that is what makes the factor exactly `1/Scale`, and why ports can be positioned by model arithmetic instead of by hit-testing.

⚙ **The inner content is laid out once.** Recompute it only when the design changes; the container scales it. A framework without a `Viewbox` does this by resizing in code (MAUI) or with a CSS transform (Blazor) — your GUI's reference says which, and whether adding content means anything extra.

⚙ **Ports are measured controls.** Slot positions come from the layout behaviour measuring your `SlotView` and writing `slot.Anchor`; the links follow the model. Moving port visuals moves the measured centre and keeps the correspondence.

### Ports: the model ↔ view correspondence

The model never computes a port's position. A slot's `Anchor` begins as `(NaN, NaN, 0)`; the only writer is `WorkflowSlotLayoutBehavior`, which measures a named control and stores its visual centre. The behaviour finds that control through the name lists on the node view:

| Model side | View side | Registered through |
|---|---|---|
| one slot property | one named `SlotView` whose `DataContext` is that slot | `SlotNames` |
| a collection of slots | a named `ItemsControl` whose item container is a `SlotView` | `SlotEnumeratorNames` |

⚙ **`SlotNames` is an attached property whose value is a comma-separated list of control names.** Each named control's `DataContext` is an `IWorkflowSlotViewModel`; the behaviour measures that control and writes its centre into the slot's `Anchor`.

⚙ **`SlotEnumeratorNames` is the collection counterpart, with the same comma-separated syntax.** Each name identifies an `ItemsControl`; the behaviour reads the realized item containers, takes each container's slot from its `DataContext`, and measures each one. An enumerator's items are generated by the selector, so the measurement follows the current selector's containers.

⚙ **`CoordinateHostName` / `CoordinateHostType` name the coordinate host.** On the attached-property adapters, both `WorkflowNodeDragBehavior` and `WorkflowSlotLayoutBehavior` expose them; the default host type is `Canvas`. Razor expresses the same choice through component parameters. The measured point is converted into that host's coordinate space before it becomes a slot anchor. WPF names its canvas; Jalium names `PART_Canvas`; WinForms uses the canvas client area.

⚙ **The correspondence is name-based and is the only path by which a slot becomes measured.** A slot whose control is not registered keeps a `NaN` anchor forever; there is no fallback that derives it from the node rectangle, and no diagnostic that says a name is missing.

⚙ **`InputSlot` and `OutputSlots` are the item template's two default implementations.** On the attached-property packs the template registers them as its standard pair so a new node view works with the default node ViewModel; Razor exposes the same pair through its `InputSlots` / `OutputSlots` render fragments. They are ordinary model properties: remove them, rename them, or add more, and establish the same `SlotNames` / `SlotEnumeratorNames` correspondence for each slot the model exposes. A node type with a different port surface replaces the default pair in its own view.

## Links

A link view binds its endpoints to `Sender.Anchor` / `Receiver.Anchor` and draws the shared cubic Bézier — each control point leaves along its own port's outward normal (`LinkCurve.LinkCurvePoints` / `BuildLinkCubic`). The geometry is in [canvas-math.md](canvas-math.md#links).

⚙ **Every link view starts with the render-ready gate:**

```csharp
if (DataContext is IWorkflowLinkViewModel link && !link.IsRenderReady()) return;
```

A link whose endpoints have not been measured draws nothing. Some adapters spell the same predicate as a `CanRender` property that combines `IsVisible` with a finite-anchor check; the meaning is identical. The finite check is part of the gate: an unmeasured endpoint carries `NaN`.

⚙ **Keep the geometry local.** Every coordinate stays bounded; if you customize a link view on a framework with a size limit, use the technique its adapter uses — a viewport-sized overlay (MAUI), an offset frame (WinUI), or self-bounding geometry (Jalium).

### Making a link interactive

Links are hit-testable, highlight on hover and delete on Delete **by default**. Two lines in the view:

```csharp
if (DataContext is IWorkflowLinkViewModel link)
    link.PublishCurve(curve, this);   // the curve you just drew, and the control that drew it
```

Then subscribe to the routed pointer events on that link's own helper:

```csharp
// _lit is a field on the link view; h is that link's own routed input.
var h = link.GetHelper() as IInputEvents;
if (h is null) return;
h.Input.PointerEntered += (_, _) => { _lit = true;  InvalidateVisual(); };
h.Input.PointerExited  += (_, _) => { _lit = false; InvalidateVisual(); };
```

⚙ **Core routes the input, the component hears it, the host draws.** `WorkflowInput.For(tree).Route(...)` is the one input route — one instance per tree. The adapter translates the platform's pointer and keys into the standard arguments and hands them over with the component it hit; the route raises them on that component's helper and then bubbles up its ancestors (`link → tree`, `slot → node → tree`, `node → tree`). Subscribe `((IInputEvents)link.GetHelper()).Input` and you hear your own hover — the route sends `PointerExited` to the one you left before `PointerEntered` to the one you arrived at.

⚙ **The hit area is the painted stroke, not the view's box.** Publish the curve you actually draw, and keep the view's own hit region on the stroke (a `Path`/geometry with no background, or `pointer-events: stroke`). A link that answers over its whole canvas-sized box swallows every canvas gesture.

⚙ **Retract when you draw nothing.** Call `PublishCurve(null)` on the same paths that draw nothing (hidden, endpoints not measured, pooled view rebound to another link). A published curve is the answer to "is there something here"; a stale one makes a link hittable where nothing is painted.

⚙ **The curve is runtime geometry.** It belongs to the view, not to the model, and is never serialized.

### How the surface picks a link

Publishing the curve is the whole opt-in. The framework answers the rest, and these rules decide what a click near a line hits:

⚙ **Six canvas units either side, and the radius does not scale with zoom.** `LinkHitTestEx.DefaultHitRadius` is 6; a pointer is a physical thing. An adapter passes `WorkflowInput.HitRadius` (same default) when it resolves a target, so a surface can widen it for touch without touching the curve.

⚙ **`ILinkHitTestable` is a capability of the link's Helper.** `LinkHelper` — the helper every generated link uses — implements it, so a normal link is hittable. A hand-written helper that does not derive from `LinkHelper` implements `ILinkHitTestable` itself.

⚙ **The gate is visibility plus a published curve.** A published curve is the evidence that something was painted there, so a link whose endpoints were measured but that draws nothing does not answer. Retract with `PublishCurve(null)` on the paths that draw nothing.

⚙ **Topmost wins, and the search runs back to front.** `HitTestVisibleLinks` walks `VisibleItems` from the end — the last drawn link is on top — and returns the first match. It skips `tree.VirtualLink`: the rubber band sits under the pointer by construction and never answers for the link you are trying to hit.

⚙ **A node card occludes the link under it, except at the link's own ports.** A link that passes underneath a node is not drawn there, so it does not answer there either; a point within the hit radius of `Sender.Anchor` or `Receiver.Anchor` still counts, because ports are drawn on the card's edge.

⚙ **Publish in the space the surface queries.** `PublishCurve` stores the curve by reference and does not transform it. Publish the same geometry you draw, in the surface's coordinates; rebuilding it on every render is fine.

⚙ **`visual` is what gives Delete a route.** Pass the control that drew the link, or `null` where one surface draws every link. The surface focuses it while the pointer is on the link, so the key can bubble through it.

⚙ **`Contains(x, y, radius)` is the only test.** The helper measures against the flattened curve it was given; a custom helper applies the same distance rule in the space it published in.

### Painting the connection preview

While a connection drag is in progress, the tree's `VirtualLink` is the rubber band, and it carries the drag's stage as an optional capability:

```csharp
if (DataContext is IConnectionPreviewLink link)
{
    var pen = link.Preview switch
    {
        ConnectionPreviewState.ValidTarget   => validPen,
        ConnectionPreviewState.InvalidTarget => invalidPen,
        _                                    => startedPen,   // None / Started
    };
    // draw the same cubic Bézier, with that pen
}
```

⚙ **The view paints a stage; it does not decide the connection.** `Preview` is `None`, `Started` (sender fixed, pointer over no candidate), `ValidTarget` or `InvalidTarget`. `WorkflowInput` writes it through `IConnectionPreviewLink.SetPreview`; a link view subscribes the link's `INotifyPropertyChanged` for `Preview` (what the demos do) or `WorkflowInput.PreviewChanged`, which carries the same snapshot.

⚙ **An invalid candidate is still a candidate.** `WorkflowInput.PreviewReceiver` stays set and `PreviewCheck` explains why it is red (`SenderCannotSend`, `ReceiverCannotReceive`, `SameNode`, `RejectedByRule`, `RejectedByHost`). Keep your highlight while the check fails.

⚙ **A host tightens the preview for one drag.** Subscribers of `WorkflowInput.PreviewValidating` see candidates the framework already accepts and may call `e.Reject(message)`; that paints this candidate red for this drag and changes nothing at release time. The release-time `IWorkflowTreeEvents.Connecting` veto and `ValidateConnection` remain the verdict.

⚙ **The candidate is found by geometry.** The rubber band covers the surface, so `SlotHitTestEx.HitTest(tree, x, y)` compares against measured slot anchors (radius 12 by default, `NaN` anchors skipped). The adapter does this for you on every pointer move; a host does not add a visual-tree hit test of its own.

### The link context menu

Right-pressing a link shows the menu the surface declares: the adapter resolves the link, and (unless a subscriber on the link itself set `Handle.PreventDefault`) pops the resource named by `WorkflowSurfaceBehavior.LinkMenuKey`. Adding an entry is one declaration in your markup — or one line in WinForms' `OnBuildLinkMenu` override.

| GUI | How the menu is declared |
|---|---|
| WPF / WinUI / MAUI | `behaviors:WorkflowSurfaceBehavior.LinkMenuKey="LinkContextMenu"` on the tree root, plus a menu resource with that key |
| Avalonia | the same property with the key its template emits (`WorkflowTreeMenu`) |
| Razor | a `<LinkMenu Context="link">…</LinkMenu>` fragment parameter on the surface component |
| WinForms | override `OnBuildLinkMenu(menu, link)` on the generated tree subclass |
| Jalium | the same `LinkMenuKey` + menu resource the markup platforms use |

⚙ **An entry binds the link it acts on.** On the markup platforms the pressed link is the menu's data context for that press (WinUI feeds each item individually, since `MenuFlyout` has none), so a new action is one declaration: `<MenuItem Header="Delete" Command="{Binding DeleteCommand}"/>` (WPF; Avalonia spells it `{ReflectionBinding DeleteCommand}` because the resource has no `x:DataType`), `<MenuFlyoutItem Text="Delete" Command="{Binding DeleteCommand}"/>` (WinUI/MAUI), or `<button @onclick="() => link.DeleteCommand.Execute(null)">Delete</button>` inside `<LinkMenu Context="link">` (Razor). WinForms adds or removes items in the `OnBuildLinkMenu` override; Jalium declares them in the menu resource.

⚙ **The generated menus are empty, and WinForms' base override adds nothing.** The packs declare the resource/fragment/override and wire it; every entry is yours, including Delete. The library supplies the event and the timing; the host decides the actions.

⚙ **The surface owns the open/closed bookkeeping.** It sets `WorkflowInput.IsSuspended` while its menu is up, so the pointer travelling onto the menu cannot clear the link the menu acts on, and releases it on close. When the link an open menu was about leaves the tree (Delete, Undo, an agent edit) the tree reports it through `GetHelper().LinkRemoved` — close your popup when it names yours. The surface opens the menu off the routed right-press.

⚙ **To refuse a menu, veto the press on the link itself.** Subscribe that link's `Input.PointerPressed` and set `e.Handle.PreventDefault = true`; the surface's own handler runs later (the route reaches the target before its ancestors) and skips the popup.

⚙ **The same veto covers the framework's own gestures.** Subscribe the tree's or the component's `Input.PointerPressed` and set `e.Handle.PreventDefault = true`, and that one gesture does not start — canvas pan, node drag, slot connection, and Ctrl+wheel zoom (`PointerWheel`) each read the handle before they act. "Shift-drag on empty canvas draws my own selection rectangle" is one refusal: refuse the press when `e.Target is null` and the modifiers say Shift, and the canvas stays still under your own handler. Blazor is the one place the refusal costs a round trip, because the gesture runs in JS and the route in .NET.

### Wheel and scrolling

⚙ **A plain wheel routes through `PointerWheelChanged`, and the adapter applies the platform's default scroll.** Set `e.Handle.PreventDefault` on that event to take the notch over: the adapter then does not scroll, and you scroll through `WorkflowInput.For(tree).Scroller?.ScrollBy(deltaX, deltaY)`.

```csharp
var input = WorkflowInput.For(tree);
var relay = ((IInputEvents)tree.GetHelper()).Input;
relay.PointerWheelChanged += (_, e) =>
{
    if ((e.Modifiers & InputModifiers.Shift) == 0) return;
    e.Handle.PreventDefault = true;           // this wheel does not scroll
    input.Scroller?.ScrollBy(-e.DeltaY, 0d);  // …it scrolls sideways instead; the axes' signs differ
};
```

⚙ **`Scroller` is a capability, not route state.** The attached surface registers an `IWorkflowSurfaceScroller` when it binds to a tree and clears it on detach, so a host reads it as nullable. The deltas are **wheel deltas** — the same values as `PointerWheelEventArgs.DeltaX` / `DeltaY`, not scroll offsets: positive X is towards the content on the right, positive Y is away from the user. The adapter converts them to its platform's step (a WPF line count, a DOM scroll, a translated canvas), clamps to the reachable range, and leaves an axis alone when its delta is zero.

⚙ **`Navigator` is the absolute twin of `Scroller`.** The same surface object implements `IWorkflowSurfaceNavigator` and registers it at `WorkflowInput.Navigator` on attach (clearing only its own on detach). `CenterOn(worldX, worldY)` puts that world point at the viewport centre; Core's `WorkflowNavigationEx` turns a component into the point:

```csharp
using VeloxDev.WorkflowSystem.StandardEx;

node.Locate();   // the node's own rectangle center
slot.Locate();   // its parent node's rectangle center
link.Locate();   // the midpoint of its two endpoint nodes' centers
```

⚙ **`Locate()` reads model data only.** A slot targets its parent node (not its measured anchor), and a link targets the midpoint of its endpoints (not its published curve), so an element that is off-screen, unmeasured or not yet drawn still reaches the centre. It pans only — the zoom is untouched — and returns `false` without changing anything when the tree has no navigator or the target has no finite position.

⚙ **A target outside the current extent grows the canvas.** Five surfaces (WPF / Avalonia / WinUI / MAUI / Razor) apply `ClampScrollOffset` with `extendRatio: 0`, then re-read the extent before scrolling; Jalium's `NavigateToWorld` grows by the same rule; WinForms' pan engine accepts signed targets and has no extent ceiling. An implementation that cannot grow clamps, so the point lands as close to the centre as it allows.

⚙ **Ctrl+wheel is the zoom gesture, not a scroll.** It is routed first, so `PreventDefault` on `PointerWheelChanged` suppresses the zoom as well; without Ctrl the wheel never zooms.

⚙ **Keys are a different contract.** A routed `KeyDown`/`KeyUp` reaches your subscription, and the surface keeps only the key it acts on (`Delete`) from travelling further. The keys the scroll container uses (arrows, PageDown, Space) reach it as usual.

### Which way the data goes

A settled link carries a **travelling highlight**, so its direction is read from motion rather than from a mark a few pixels wide. Every full demo does this; the Trimmed suites leave it out because it is decoration rather than part of the editor.

⚙ **Animate a scalar phase on the flow owner, and derive the drawing from it.** The WPF/Avalonia/WinUI link views read `BandHead` / `BandIntensity` from the tree's shared `WorkflowFlowClock`; the WinForms canvas and the Jalium surface own the same pair (Jalium uses `FlowPhase`) and animate it once per surface; the Blazor link is drawn by `flow.js`. Give the flow owner an animatable scalar that triggers a repaint, and compute geometry and colour from it in the draw pass. See [the animation skill's flow reference](../../../veloxdev-create-animation/references/practices/flow.md).

⚙ **The line rests dim and the band is the same colour at full strength.** Dim the resting line by alpha (about three fifths) and keep the hue; the contrast then sits where the eye finds it. Pushing the link's colour towards white changes one channel of three on a 2px line and is invisible.

⚙ **The band is a phase of the cycle, not a colour swap.** It comes up from the resting colour over the first third of its travel, travels fully lit and unchanged for the middle third, and settles back over the last third — the last phase also makes the loop seam invisible, since the line is uniformly dim at both ends of a cycle.

⚙ **A virtual link (the rubber band under the pointer) and a selected link keep a flat pen.** One is not a settled connection and the other is already highlighted.

⚙ **Subscribe on attach and unsubscribe on detach.** Views are pooled; a released view handed a different link must stop sampling the previous flow. The shared clock starts on its first subscriber and stops on its last.

⚙ **A link move does not write the band's position.** Re-aiming the gradient and re-colouring the shoulders happens whenever the link's endpoints change; the cycle owns the offsets and writes them from its captured endpoints. A second writer fights the animation for a frame, which reads as a band that stutters while the canvas moves.

## Virtualization

Handled for you, and you normally never touch it.

⚙ `WorkflowSurfaceBehavior` keeps `tree.GetHelper().Viewport` up to date, and the tree realizes and releases views from it. If you host the canvas yourself instead of using the behaviour, write the viewport yourself — in collapsed coordinates — after setting the virtualize inset for your ruler band.

⚙ **The item templates pool views.** A node's view can be released and reused for a different node, so all state belongs on the ViewModel.

## Two ways the surface can work

The adapters fall into two camps, and which one you are in decides whether you must refresh by hand:

| | Notification-driven | Pull-driven |
|---|---|---|
| Frameworks | WPF, Avalonia, WinUI, MAUI, Jalium, Blazor | WinForms |
| Node position | bound to the collapsed `Anchor`; the binding re-evaluates | repositioned in code, every pass |
| After a mutation | automatic | you must call `Refresh(host)` |

⚙ **On WinForms, `Refresh` is part of the contract.** Nothing binds, so a node that moved without a refresh is still drawn where it was — and a slot anchor written after a deferred refresh is a frame stale, which is why that adapter also exposes a synchronous re-sync. Your GUI's reference has the details.

## Checking your views

⚙ Generate, build, run — then **drag a node, make a connection, and zoom until the pointer stays under what you grabbed.** Those three exercise the surface, the drag behaviour, the connection protocol, the slot measurement and the zoom chain. While the connection drag is live, watch the candidate slot and the rubber band: the slot's `PreviewReceiver` state and the link's `Preview` stage follow the pointer, and an invalid candidate is red rather than unmarked.

⚙ **Zoom to about 40% and look at the links.** Almost every view-layer problem — links that vanish, links that detach from their port, a camera that drifts — is invisible at 100%.

⚙ `Examples/Workflow/<GUI> Trimmed/Demo` is a complete, minimal view suite for your GUI and the fastest way to answer "what is this supposed to look like". `Examples/Workflow/<GUI>/Demo` is the same with every optional feature on. (Blazor's folder is `Examples/Workflow/Blazor Trimmed/Demo` — named after the platform, not the adapter — and Avalonia's demo is nested one level deeper: `Examples/Workflow/Avalonia Trimmed/Demo/Demo`.)

When a view misbehaves, read [references/pitfalls/view-and-input.md](../pitfalls/view-and-input.md) for the failure the symptom maps to.
