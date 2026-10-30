# Your views

The canvas you put on screen is assembled from seven view roles, and the item templates generate all seven for you — see [templates.md](templates.md). This file is what you need in order to **customize and debug** them: how they are composed, which parts are yours to change, and the handful of rules that make them silently not work when broken.

⚙ **Default: generate all seven roles from the item templates.** The decorator is the floating ruler; the minimap is the thumbnail. Do not hand-write a tree + node + link subset. The generated node view is the elastic container (`Viewbox` on WPF/Avalonia/WinUI/Jalium, layout-only scaling on MAUI/WinForms, the CSS transform on Razor); keep that container when you add content — each `gui/<gui>.md` has the code.

Open your GUI's reference alongside this one — `gui/<gui>.md` carries that framework's specifics, and most surprises live in the difference between the seven.

## The seven roles

| Role | Class | Job |
|---|---|---|
| Surface host | `WorkflowSurfaceBehavior` | resolves the named child controls, feeds scroll and viewport, starts panning, hooks zoom, forwards link interaction and shows the link menu, and registers `WorkflowInput.Scroller` |
| Canvas transform | `WorkflowCanvasTransformBehavior` — **Jalium spells it `WorkflowSurfaceBehavior.CanvasTransform`**; MAUI has no carrier (see below) | owns the pan offset your node and link views bind to |
| View pool | `ViewPool` / `ViewManager` | object-pooled views over the visible-items collection |
| Node drag | `WorkflowNodeDragBehavior` | a drag on a node executes `MoveCommand` |
| Slot connection | `WorkflowSlotConnectionBehavior` | press one slot, release on another, connect them; on the frameworks without a surface pointer-move hook it also reports the candidate receiver |
| Slot layout | `WorkflowSlotLayoutBehavior` | measures each port and writes its anchor back |
| Grid decorator / minimap | `IWorkflowGridDecorator` / `IWorkflowMinimapOverlay` | grid, ruler and thumbnail |

⚙ **Only the first six do anything on their own; the decorator and minimap you write or generate.** `IWorkflowGridDecorator` and `IWorkflowMinimapOverlay` are small interfaces defined in Core (namespace `VeloxDev.WorkflowSystem`) — the surface pushes scroll and content offsets into them each pass, and the minimap's interface derives from the decorator's because it needs the same numbers. `RulerBand` is the one member that affects more than drawing: the surface forwards it to the virtualizer so nodes under the floating ruler are not culled.

⚙ **Grid lines alone are not a grid decorator.** The floating top/left ruler bands are part of the expected surface chrome: draw them in a hit-test-transparent top layer (the templates use `ZIndex="100"`), return the band thickness from `RulerBand`, and offset the world canvas by that band (the XAML adapters do it with `Canvas.RenderTransform`; the grid maths use the same band as their content offset). The templates ship the full decorator — copy it rather than reimplementing only the lines.

## Wiring the surface

The surface is the control that owns the scroll viewer and the canvas. Everything it needs is a **name**, resolved out of your markup.

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

| Property | Default | Meaning |
|---|---|---|
| `IsEnabled` | `false` | master switch |
| `ScrollViewerName` / `CanvasName` | `null` | the inner scroll viewer and canvas |
| `GridDecoratorName` / `MinimapOverlayName` | `null` | the elements implementing the two interfaces |
| `PointerPressSourceName` | `null` | the element whose press starts panning |
| `ZoomEnabled` | `false` | enable the wheel-zoom gesture |
| `LinkMenuKey` | `null` | resource key of the link's context menu, resolved after the resource dictionary exists |

⚙ **Nothing happens until `IsEnabled` is true**, and every other setting is a name looked up in your markup. Renaming a `PART_` element without updating the attached property is a silent no-op — the canvas just does nothing.

⚙ The names are a convention, not a requirement. The templates emit `PART_ScrollViewer`, `PART_Canvas`, `PART_SurfaceBorder`, `PART_GridDecorator` and `PART_MinimapOverlay`; keep them unless you have a reason not to.

⚙ Not every framework has attached properties. WinForms uses `WorkflowSurfaceBehavior.SetIsEnabled(control, true)` and friends; Blazor uses component parameters. Your GUI's reference has the exact spelling.

### The binding rule that bites

Node and link views bind their pan offset to the host rather than being moved one by one:

```xml
RenderTransform="{Binding RelativeSource={RelativeSource AncestorType={x:Type local:TreeView}},
                          Path=(behaviors:WorkflowCanvasTransformBehavior.Transform)}"
```

⚙ **This binding must sit on the `DataTemplate` root, never on an element inside an item.** An ancestor lookup made from inside an item resolves against that item's own visual tree and silently finds nothing — which looks exactly like "the canvas does not pan".

⚙ **Jalium carries the same value under a different name** — `WorkflowSurfaceBehavior.CanvasTransform`, republished as a CLR property by the tree view's own class, because this platform's bindings cannot read an attached property. Same value, same notifications; only the way it is spelled differs, so a `RenderTransform` bound to it works exactly as above. ⚙ MAUI is the one that ships no carrier at all: it pools links into a single overlay, so there is no per-view transform to set. Your GUI's reference says which applies.

## What a node view is made of

Three layers, and where you put your own content depends on which one you mean:

```
outer collapsed box      positioned at the node's Anchor, sized to its Size
  └── scale container    scales its child by 1 / Scale
       └── design canvas chrome, text and ports, laid out at design coordinates
```

⚙ **Put your content in the design canvas**, and give it design-size coordinates. The scale container handles zoom for you — that is what makes the factor exactly `1/Scale`, and why ports can be positioned by model arithmetic instead of by hit-testing.

⚙ **The inner content is laid out once and never re-laid-out.** Do not recompute it per zoom; the container scales it. A framework without a `Viewbox` does this by resizing in code (MAUI) or with a CSS transform (Blazor) — your GUI's reference says which, and whether adding content means anything extra.

⚙ **Do not touch the ports.** Slot positions come from the layout behaviour measuring your `SlotView` and writing `slot.Anchor`. If you move port visuals, the links follow the model, not your markup — they will disconnect visually.

### Ports: the model ↔ view correspondence

The model never computes a port's position. A slot's `Anchor` begins as `(NaN, NaN, 0)`; the only writer is `WorkflowSlotLayoutBehavior`, which measures a named control and stores its visual centre. The behaviour finds that control through the name lists on the node view, so the correspondence is:

| Model side | View side | Registered through |
|---|---|---|
| one slot property | one named `SlotView` whose `DataContext` is that slot | `SlotNames` |
| a collection of slots | a named `ItemsControl` whose item container is a `SlotView` | `SlotEnumeratorNames` |

⚙ **The correspondence is name-based, and it is the only path by which a slot becomes measured.** A slot whose control is not on the list keeps a `NaN` anchor forever; there is no fallback that derives it from the node rectangle, and no diagnostic that says a name is missing.

⚙ **The item templates carry the standard pair, not the model's full surface.** The generated node view draws one input and one output (`PART_InputSlot` / `PART_OutputSlot`, with `SlotNames` already filled for those two). That is the template's default surface. A node type whose ViewModel exposes other or more slots establishes the same correspondence for each slot it wants measured; renaming an existing part is only the narrowest case of that rule.

## Links

A link view binds its endpoints to `Sender.Anchor` / `Receiver.Anchor` and draws the shared cubic Bézier — each control point leaves along its own port's outward normal (`LinkCurve.LinkCurvePoints` / `BuildLinkCubic`). The geometry is in [canvas-math.md](canvas-math.md#links).

⚙ **Every link view starts with the render-ready gate:**

```csharp
if (DataContext is IWorkflowLinkViewModel link && !link.IsRenderReady()) return;
```

A link whose endpoints have not been measured must draw nothing. Some adapters spell the same predicate as a `CanRender` property bound to `IsVisible` plus a NaN check; the meaning is identical. **If your links are invisible, suspect an unmeasured slot before anything else.**

⚙ **`CanRender` bound to `IsVisible` is not the gate by itself.** Without the finite check, an unmeasured endpoint still reaches the renderer, and `NaN` does not hide the curve — it degenerates into long, arbitrary geometry. The symptom is therefore not "missing link" but "link wired to the wrong place", and it sends a reader looking at the camera, the layout or the zoom instead of the slot correspondence. When a link looks attached to the wrong point, check the correspondence and the gate before anything else.

⚙ **Never hand a renderer an unbounded coordinate or a whole-world canvas size.** If you customize a link view on a framework with a size limit, keep the geometry local — your GUI's reference describes the technique its adapter uses (viewport-sized overlay, offset frame, or self-bounding).

### Making a link interactive

Links are hit-testable, highlight on hover and delete on Delete **by default** — you get that by drawing your curve and handing it over, not by writing hit-testing code. Two lines in the view:

```csharp
if (DataContext is IWorkflowLinkViewModel link)
    link.PublishCurve(curve, this);   // the curve you just drew, and the control that drew it
```

Then, if you want the hover to be *visible*, subscribe to the routed pointer events on that link's own helper:

```csharp
// _lit is a field on the link view; h is that link's own routed input.
var h = link.GetHelper() as IInputEvents;
if (h is null) return;
h.Input.PointerEntered += (_, _) => { _lit = true;  InvalidateVisual(); };
h.Input.PointerExited  += (_, _) => { _lit = false; InvalidateVisual(); };
```

⚙ **Core routes the input, the component hears it, the host draws.** `WorkflowInput.For(tree).Route(...)` is the one input route — one instance per tree. The adapter translates the platform's pointer and keys into the standard arguments and hands them over with the component it hit; the route raises them on that component's helper and then bubbles up its ancestors (`link → tree`, `slot → node → tree`, `node → tree`). Subscribe `((IInputEvents)link.GetHelper()).Input` and you hear your own hover — the route sends `PointerExited` to the one you left before `PointerEntered` to the one you arrived at. **Do not write your own distance-to-curve test** — that was per-platform copies before and is now one implementation.

⚙ **The hit area is the painted stroke, not the view's box.** Publish the curve you actually draw, and keep the view's own hit region on the stroke (a `Path`/geometry with no background, or `pointer-events: stroke`) — a link that answers over its whole canvas-sized box swallows every canvas gesture.

⚙ **Retract when you draw nothing.** Call `PublishCurve(null)` on the same paths that draw nothing (hidden, endpoints not measured, pooled view rebound to another link). A published curve *is* the answer to "is there something here", so a stale one makes a link hittable where nothing is painted.

⚙ **The curve is runtime geometry — never serialize it.** It belongs to the view, not to the model.

### How the surface picks a link

Publishing the curve is the whole opt-in. The framework answers the rest, and these rules are what decide a click near a line — or which of two links it picks:

⚙ **Six canvas units either side, and the radius does not scale with zoom.** `LinkHitTestEx.DefaultHitRadius` is 6; a pointer is a physical thing, and the demos never scaled it. An adapter passes `WorkflowInput.HitRadius` (same default) when it resolves a target, so a surface can widen it for touch without touching the curve.

⚙ **`ILinkHitTestable` is a capability of the link's Helper, not of the view model.** `LinkHelper` — the helper every generated link uses — implements it, so a normal link is hittable. A hand-written helper that does not derive from `LinkHelper` must implement `ILinkHitTestable` itself, or the link is never hit; there is no fallback to the view's box.

⚙ **The gate is visibility plus a published curve, not anchor measurement.** A published curve is the evidence that something was painted there, so a link whose endpoints were measured but that draws nothing does not answer. That is also why retracting with `PublishCurve(null)` matters.

⚙ **Topmost wins, and the search runs back to front.** `HitTestVisibleLinks` walks `VisibleItems` from the end — the last drawn link is on top — and returns the first match. It skips `tree.VirtualLink`: the rubber band sits under the pointer by construction and must never answer for the link you are trying to hit.

⚙ **A node card occludes the link under it — except at the link's own ports.** A link that passes underneath a node is not drawn there, so it does not answer there either; a point within the hit radius of `Sender.Anchor` or `Receiver.Anchor` still counts, because ports are drawn on the card's edge.

⚙ **Coordinates must be in the space the surface queries.** `PublishCurve` stores the curve by reference and does not transform it. Publish the same geometry you draw, in the surface's coordinates; rebuilding it on every render is fine.

⚙ **`visual` is what gives Delete a route.** Pass the control that drew the link, or `null` where one surface draws every link. The surface focuses it while the pointer is on the link, so the key can bubble through it; an immediate-mode layer reads it to skip a link it has already painted.

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

⚙ **An invalid candidate is still a candidate.** `WorkflowInput.PreviewReceiver` stays set and `PreviewCheck` explains why it is red (`SenderCannotSend`, `ReceiverCannotSend`, `SameNode`, `RejectedByRule`, `RejectedByHost`). Do not clear your highlight when the check fails.

⚙ **A host can only tighten the preview.** Subscribers of `WorkflowInput.PreviewValidating` see candidates the framework already accepts and may call `e.Reject(message)`; that paints this candidate red for this drag and changes nothing at release time. The release-time `IWorkflowTreeEvents.Connecting` veto and `ValidateConnection` remain the verdict.

⚙ **The candidate is found by geometry, not hit-testing.** The rubber band covers the surface, so `SlotHitTestEx.HitTest(tree, x, y)` compares against measured slot anchors (radius 12 by default, `NaN` anchors skipped). The adapter does this for you on every pointer move; do not add a visual-tree hit test of your own.

### The link context menu

Right-pressing a link shows the menu the surface declares: the adapter resolves the link, and (unless a subscriber on the link itself set `Handle.PreventDefault`) pops the resource named by `WorkflowSurfaceBehavior.LinkMenuKey`. Adding an entry is one declaration in your markup — or one line in WinForms' `OnBuildLinkMenu` override — not gesture code.

| GUI | How the menu is declared |
|---|---|
| WPF / WinUI / MAUI | `behaviors:WorkflowSurfaceBehavior.LinkMenuKey="LinkContextMenu"` on the tree root, plus a menu resource with that key |
| Avalonia | the same property with the key its template emits (`WorkflowTreeMenu`) |
| Razor | a `<LinkMenu Context="link">…</LinkMenu>` fragment parameter on the surface component |
| WinForms | override `OnBuildLinkMenu(menu, link)` on the generated tree subclass |
| Jalium | the same `LinkMenuKey` + menu resource the markup platforms use |

⚙ **An entry binds the link it acts on.** On the markup platforms the pressed link is the menu's data context for that press (WinUI feeds each item individually, since `MenuFlyout` has none), so a new action is one declaration: `<MenuItem Header="Delete" Command="{Binding DeleteCommand}"/>` (WPF; Avalonia spells it `{ReflectionBinding DeleteCommand}` because the resource has no `x:DataType`), `<MenuFlyoutItem Text="Delete" Command="{Binding DeleteCommand}"/>` (WinUI/MAUI), or `<button @onclick="() => link.DeleteCommand.Execute(null)">Delete</button>` inside `<LinkMenu Context="link">` (Razor). WinForms adds or removes items in the `OnBuildLinkMenu` override; Jalium declares them in the menu resource, exactly like the other markup platforms.

⚙ **The generated menus are empty, and WinForms' base override adds nothing.** The packs declare the resource/fragment/override and wire it, but every entry is yours — including Delete. That is deliberate: the library supplies the event and the timing, the host decides the actions.

⚙ **The surface owns the open/closed bookkeeping.** It sets `WorkflowInput.IsSuspended` while its menu is up, so the pointer travelling onto the menu cannot clear the link the menu acts on, and releases it on close. When the link an open menu was about leaves the tree (Delete, Undo, an agent edit) the tree reports it through `GetHelper().LinkRemoved` — close your popup when it names yours. **There is no menu event in Core any more; the surface opens it off the routed right-press.**

⚙ **To refuse a menu, veto the press on the link itself.** Subscribe that link's `Input.PointerPressed` and set `e.Handle.PreventDefault = true`; the surface's own handler runs later (the route reaches the target before its ancestors) and skips the popup. Ordering is fixed by construction, so a refusal cannot race the popup.

⚙ **The same veto covers the framework's own four gestures.** Subscribe the tree's or the component's `Input.PointerPressed` and set `e.Handle.PreventDefault = true`, and that one gesture does not start — canvas pan, node drag, slot connection, and Ctrl+wheel zoom (`PointerWheel`) each read the handle before they act. That is the whole of "Shift-drag on empty canvas draws my own selection rectangle": refuse the press when `e.Target is null` and the modifiers say Shift, and the canvas stays still under your own handler. The gestures do not tell you which one you just refused — you refuse the press, and whatever would have started is what stands down. Blazor is the one place the refusal costs a round trip, because the gesture runs in JS and the route in .NET: the gesture starts one SignalR message later, and the move deltas are not lost.

### Wheel and scrolling

⚙ **A plain wheel is the adapter's gesture, and it reads your verdict.** Every adapter routes a non-Ctrl wheel through `PointerWheelChanged` and then applies the platform's default scroll itself — the platform's scroll container never scrolls the workflow surface. Set `e.Handle.PreventDefault` on that event to take the notch over: the adapter then does not scroll, and you scroll through `WorkflowInput.For(tree).Scroller?.ScrollBy(deltaX, deltaY)`.

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

⚙ **`Scroller` is a capability, not route state.** The attached surface registers an `IWorkflowSurfaceScroller` when it binds to a tree and clears it on detach, so a host reads it as nullable. The deltas are **wheel deltas** — the same values as `PointerWheelEventArgs.DeltaX` / `DeltaY`, not scroll offsets: positive X is towards the content on the right, positive Y is away from the user. The adapter converts them to its platform's step (a WPF line count, a DOM scroll, a translated canvas), clamps to the reachable range, and leaves an axis alone when its delta is zero. That is the whole point — one subscriber looks the same on all seven.

⚙ **`Navigator` is the absolute twin of `Scroller`.** The same surface object implements `IWorkflowSurfaceNavigator` and registers it at `WorkflowInput.Navigator` on attach (clearing only its own on detach). `CenterOn(worldX, worldY)` puts that world point at the viewport center; Core's `WorkflowNavigationEx` turns a component into the point:

```csharp
using VeloxDev.WorkflowSystem.StandardEx;

node.Locate();   // the node's own rectangle center
slot.Locate();   // its parent node's rectangle center
link.Locate();   // the midpoint of its two endpoint nodes' centers
```

⚙ **`Locate()` reads model data only.** A slot targets its parent node (not its measured anchor), and a link targets the midpoint of its endpoints (not its published curve), so an element that is off-screen, unmeasured or not yet drawn still reaches the center. It pans only — the zoom is untouched — and returns `false` without changing anything when the tree has no navigator or the target has no finite position.

⚙ **A target outside the current extent grows the canvas.** Five surfaces (WPF / Avalonia / WinUI / MAUI / Razor) apply `ClampScrollOffset` with `extendRatio: 0`, then re-read the extent before scrolling; Jalium's `NavigateToWorld` grows by the same rule; WinForms' pan engine accepts signed targets and has no extent ceiling. An implementation that cannot grow clamps, so the point lands as close to the center as it allows.

⚙ **Ctrl+wheel is the zoom gesture, not a scroll.** It is routed first, so `PreventDefault` on `PointerWheelChanged` suppresses the zoom as well; without Ctrl the wheel never zooms.

⚙ **Keys are a different contract.** A routed `KeyDown`/`KeyUp` reaches your subscription, but the surface only keeps the key from travelling further when it is the key the surface itself acts on (`Delete`) — the keys the scroll container uses (arrows, PageDown, Space) were never the surface's to swallow.

### Which way the data goes

A settled link carries a **travelling highlight**, so its direction is read from the motion rather than from a mark that is a few pixels wide and invisible at 40% zoom. Every full demo does this; the Trimmed suites deliberately do not, because it is decoration rather than part of the editor.

```csharp
// three stops ARE the band: the middle carries the lit colour, the two either side are its shoulders
.Property(v => v.FlowBrush.GradientStops[0].Offset, BandFormed - BandHalfWidth)
.Property(v => v.FlowBrush.GradientStops[1].Offset, BandFormed)
.Property(v => v.FlowBrush.GradientStops[2].Offset, BandFormed + BandHalfWidth)
.Property(v => v.FlowBrush.GradientStops[1].Color, Lit)
```

⚙ **The line rests dim and the band is the same colour at full strength — do not paint the band a different colour.** The obvious reading of "highlight" is the link's colour pushed towards white, and it is invisible: cyan lifted 75% towards white differs from cyan in one channel out of three, on a 2px line, against a dark canvas. Dimming the *resting* line by alpha (about three fifths) keeps the hue and puts the contrast where the eye finds it, and the same rule works on the white links the non-Avalonia demos draw.

⚙ **The band is a phase of the cycle, not a colour swap.** It comes up from the resting colour over the first third of its travel, travels fully lit and unchanged for the middle third, and settles back over the last third — the last phase is also what makes the loop seam invisible, since the line is uniformly dim at both ends of a cycle.

⚙ **A virtual link (the rubber band under the pointer) and a selected link keep a flat pen.** One is not a settled connection and the other is already highlighted.

⚙ Start the animation when the view attaches and `Transition.Exit(...)` it when the view detaches: views are pooled, and a released view that is handed a different link must not keep animating the previous one.

⚙ The three phases are a **`Then()` chain ending in `Repeat(...)`** — the band forms as it enters, travels fully lit, and settles back, forever. The animated object is the view that owns the brush (or the component, or the surface that draws the links), with the paths going into `GradientStops[i]` rather than into a carrier value: see [the animation skill's segments note](../../veloxdev-create-animation/SKILL.md#segments).

⚙ **A link move must not write the band's position.** Aiming the gradient and re-colouring the shoulders happens whenever the link's endpoints change — which is every frame of a zoom or a node drag — while the cycle owns the offsets and writes them from its captured endpoints. A path that re-seats them fights the animation for a frame, which reads as a band that stutters while the canvas moves.

## Virtualization

Handled for you, and you normally never touch it.

⚙ `WorkflowSurfaceBehavior` keeps `tree.GetHelper().Viewport` up to date, and the tree realizes and releases views from it. If you host the canvas yourself instead of using the behaviour, you must write the viewport yourself — in collapsed coordinates — after setting the virtualize inset for your ruler band.

⚙ **The item templates pool views; do not hold references to them.** A node's view can be released and reused for a different node, so all state belongs on the ViewModel.

## Two ways the surface can work

The adapters fall into two camps, and which one you are in decides whether you must refresh by hand:

| | Notification-driven | Pull-driven |
|---|---|---|
| Frameworks | WPF, Avalonia, WinUI, MAUI, Jalium, Blazor | WinForms |
| Node position | bound to the collapsed `Anchor`; the binding re-evaluates | repositioned in code, every pass |
| After a mutation | automatic | you must call `Refresh(host)` |

⚙ **On WinForms, `Refresh` is not optional.** Nothing binds, so a node that moved without a refresh is still drawn where it was — and a slot anchor written after a deferred refresh is a frame stale, which is why that adapter also exposes a synchronous re-sync. Your GUI's reference has the details.

## Checking your views

⚙ Generate, build, run — then **drag a node, make a connection, and zoom until the pointer stays under what you grabbed.** Those three exercise the surface, the drag behaviour, the connection protocol, the slot measurement and the zoom chain. While the connection drag is live, watch the candidate slot and the rubber band: the slot's `PreviewReceiver` state and the link's `Preview` stage must follow the pointer, and an invalid candidate must be red rather than unmarked.

⚙ **Zoom to about 40% and look at the links.** Almost every view-layer problem — links that vanish, links that detach from their port, a camera that drifts — is invisible at 100%.

⚙ `Examples/Workflow/<GUI> Trimmed/Demo` is a complete, minimal view suite for your GUI and the fastest way to answer "what is this supposed to look like". `Examples/Workflow/<GUI>/Demo` is the same with every optional feature on. If you installed from NuGet they are on GitHub. (Blazor's folder is `Examples/Workflow/Blazor Trimmed/Demo` — named after the platform, not the adapter — and Avalonia's demo is nested one level deeper: `Examples/Workflow/Avalonia Trimmed/Demo/Demo`.)
