# Your views

The canvas you put on screen is assembled from seven view roles, and the item templates generate all seven for you — see [templates.md](templates.md). This file is what you need in order to **customize and debug** them: how they are composed, which parts are yours to change, and the handful of rules that make them silently not work when broken.

Open your GUI's reference alongside this one — `gui/<gui>.md` carries that framework's specifics, and most surprises live in the difference between the seven.

## The seven roles

| Role | Class | Job |
|---|---|---|
| Surface host | `WorkflowSurfaceBehavior` | resolves the named child controls, feeds scroll and viewport, starts panning, hooks zoom |
| Canvas transform | `WorkflowCanvasTransformBehavior` | owns the pan offset your node and link views bind to |
| View pool | `ViewPool` / `ViewManager` | object-pooled views over the visible-items collection |
| Node drag | `WorkflowNodeDragBehavior` | a drag on a node executes `MoveCommand` |
| Slot connection | `WorkflowSlotConnectionBehavior` | press one slot, release on another, connect them |
| Slot layout | `WorkflowSlotLayoutBehavior` | measures each port and writes its anchor back |
| Grid decorator / minimap | `IWorkflowGridDecorator` / `IWorkflowMinimapOverlay` | grid, ruler and thumbnail |

⚙ **Only the first six do anything on their own; the decorator and minimap you write or generate.** `IWorkflowGridDecorator` and `IWorkflowMinimapOverlay` are small interfaces defined in Core (namespace `VeloxDev.WorkflowSystem`) — the surface pushes scroll and content offsets into them each pass, and the minimap's interface derives from the decorator's because it needs the same numbers. `RulerBand` is the one member that affects more than drawing: the surface forwards it to the virtualizer so nodes under the floating ruler are not culled.

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
             behaviors:WorkflowSurfaceBehavior.MinimapOverlayName="PART_MinimapOverlay">
```

| Property | Default | Meaning |
|---|---|---|
| `IsEnabled` | `false` | master switch |
| `ScrollViewerName` / `CanvasName` | `null` | the inner scroll viewer and canvas |
| `GridDecoratorName` / `MinimapOverlayName` | `null` | the elements implementing the two interfaces |
| `PointerPressSourceName` | `null` | the element whose press starts panning |
| `ZoomEnabled` | `false` | enable the wheel-zoom gesture |

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

⚙ MAUI does not ship this behaviour at all, and Jalium mirrors the transform onto the pooled views rather than the host. Your GUI's reference says which applies.

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

## Links

A link view binds its endpoints to `Sender.Anchor` / `Receiver.Anchor` and draws the four-point golden-stub elbow — the geometry is in [canvas-math.md](canvas-math.md#links).

⚙ **Every link view starts with the render-ready gate:**

```csharp
if (DataContext is IWorkflowLinkViewModel link && !link.IsRenderReady()) return;
```

A link whose endpoints have not been measured must draw nothing. Some adapters spell the same predicate as a `CanRender` property bound to `IsVisible` plus a NaN check; the meaning is identical. **If your links are invisible, suspect an unmeasured slot before anything else.**

⚙ **Never hand a renderer an unbounded coordinate or a whole-world canvas size.** If you customize a link view on a framework with a size limit, keep the geometry local — your GUI's reference describes the technique its adapter uses (viewport-sized overlay, offset frame, or self-bounding).

### Which way the data goes

A settled link carries a **travelling highlight**, so its direction is read from the motion rather than from a mark that is a few pixels wide and invisible at 40% zoom. Every full demo does this; the Trimmed suites deliberately do not, because it is decoration rather than part of the editor.

```csharp
// the resting line, and the length of it that is lit as it passes
stops[0].Offset = centre - HalfWidth;   // HalfWidth ≈ 0.04 of the link
stops[1].Offset = centre;               // the band, at the lit colour
stops[2].Offset = centre + HalfWidth;
stops[1].Color  = Blend(Dim, Lit, mix);
```

⚙ **The line rests dim and the band is the same colour at full strength — do not paint the band a different colour.** The obvious reading of "highlight" is the link's colour pushed towards white, and it is invisible: cyan lifted 75% towards white differs from cyan in one channel out of three, on a 2px line, against a dark canvas. Dimming the *resting* line by alpha (about three fifths) keeps the hue and puts the contrast where the eye finds it, and the same rule works on the white links the non-Avalonia demos draw.

⚙ **The band is a phase of the cycle, not a colour swap.** It comes up from the resting colour over the first third of its travel, travels fully lit and unchanged for the middle third, and settles back over the last third — the last phase is also what makes the loop seam invisible, since the line is uniformly dim at both ends of a cycle.

⚙ **A virtual link (the rubber band under the pointer) and a selected link keep a flat pen.** One is not a settled connection and the other is already highlighted.

⚙ Start the animation when the view attaches and `Transition.Exit(...)` it when the view detaches: views are pooled, and a released view that is handed a different link must not keep animating the previous one.

⚙ The declaration is a **single looping segment** and the phases are a mapping from that one animated value. A `Then()` chain with `Repeat(...)` expresses the same three phases and is the more declarative spelling — see [the animation skill's segments note](../../veloxdev-create-animation/SKILL.md#segments); the demos carry the single-segment form, which needs no chain.

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

⚙ Generate, build, run — then **drag a node, make a connection, and zoom until the pointer stays under what you grabbed.** Those three exercise the surface, the drag behaviour, the connection protocol, the slot measurement and the zoom chain.

⚙ **Zoom to about 40% and look at the links.** Almost every view-layer problem — links that vanish, links that detach from their port, a camera that drifts — is invisible at 100%.

⚙ `Examples/Workflow/<GUI> Trimmed/Demo` is a complete, minimal view suite for your GUI and the fastest way to answer "what is this supposed to look like". `Examples/Workflow/<GUI>/Demo` is the same with every optional feature on. If you installed from NuGet they are on GitHub.
