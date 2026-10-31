# Blazor / Razor

`VeloxDev.Razor` · targets `net6.0;net8.0` + `Microsoft.AspNetCore.App`

⚙ **This GUI goes by two names, and the mismatch is only in the folder.** The package is `VeloxDev.Razor`, the template prefix is `razor`, and the demos live under **`Examples/Workflow/Blazor Trimmed/Demo`**. Everywhere else the three agree.

⚙ **Reference implementation:** `Examples/Workflow/Blazor Trimmed/Demo/Components/Workflow/` — note `Components/`, the Blazor convention. Every role is a `.razor` + `.razor.cs` pair, including the selector, decorator and minimap. The JS half of the adapter ships in the package, not the demo.

⚙ **The "Trimmed" in that path means *minimal demo*.** It is a demo-size choice, not a publish setting — see [the rule in SKILL.md](../../../SKILL.md#checking-your-work).

The adapter's API surface is the components themselves: each role is a `.razor` component under `Src/Adapters/VeloxDev.Razor/Attached/Workflow/`, and its `[Parameter]`s are the contract.

## Writing the surface

There is no canvas control and no attached properties — the surface is a **Blazor component**, and what the other adapters express as attached properties are its parameters:

| Parameter | Meaning |
|---|---|
| `Tree` | the tree to host |
| `IsEnabled` | master switch; the surface reads the other parameters only while it is `true` |
| `ZoomEnabled` | enables wheel zoom |
| `ScrollViewerId` | `id` of the scroll element |
| `CanvasId` | `id` of the canvas element |
| `GridDecorator` / `Minimap` | `RenderFragment<SurfaceViewport>` for the two decorator roles |
| `ChildContent` | `RenderFragment<SurfaceCanvas>` — the node/slot content |
| `LinkMenu` | `RenderFragment<IWorkflowLinkViewModel>` — the link context menu's entries; the surface renders the chrome and wires it |
| `Background` / `GridColor` / `MajorGridColor` / `AxisColor` | surface visuals |
| `GridSpacing` / `MajorLineEvery` / `RulerThickness` | grid and ruler geometry |

The ids are the DOM contract: the JS half resolves the scroll and canvas elements by these ids. The render fragments are the extension points for the decorator, minimap and menu.

## Adding your own content to a node

The node card is a CSS transform of design-size content:

```css
transform: scale(collapsedWidth / 260);   /* the node's design width */
transform-origin: top left;
```

```razor
<div class="veloxdev-wf-node-card"
     style="position:relative;width:260px;height:180px;
            transform:scale(@ScaleCss);transform-origin:top left;">
    @* Your content, at design-size coordinates. *@
</div>
```

```csharp
private const double DesignWidth = 260;

private string ScaleCss => Node is null || Node.Size.Width <= 0
    ? "1"
    : (Node.Size.Width / DesignWidth).ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
```

⚙ **`getBoundingClientRect()` includes that transform**, which is why slot anchors and links stay correct through a zoom without the measuring code the desktop adapters need. The transform is part of the measurement contract; a layout-based position changes what the browser reports.

⚙ Put your controls on the design-size card as usual. The browser scales them.

## Zoom

⚙ The gesture is a **non-passive** `wheel` listener in JS, coalesced to one in-flight burst. `ZoomEnabled` alone is not enough — the JS half has to be initialised, or the canvas never zooms.

⚙ **After a zoom, the JS dispatches `veloxdev-wf-layout-changed` on the next animation frame**, and the slot layout listens for it to re-measure. Nodes reposition during the zoom and the slots do not; the event is what makes them catch up. A custom zoom path dispatches the same event.

⚙ **Near-edge zoom is clamped, never grown.** Centring the pivot by growing `NegativeOffset` while the scroll stays pinned pushes the visible viewport inward on every zoom step — a camera that drifts and then jumps. The adapter centres the pivot by scrolling within the reachable range and allows the pivot to sit slightly off-centre near an edge. The DOM has an edge margin the desktop surfaces do not.

⚙ If you write your own zoom, take the pivot from `Math.Max(0, scroll)` — the effective scroll is `DOM scrollLeft − edgeX`, and a negative value feeds `WorldAtViewportCenter` nonsense.

## Connection preview and scroll

⚙ **`WorkflowSlotConnectionBehavior.OnSlotConnectionMove` (`[JSInvokable]`) reports the candidate receiver.** JS knows only the press and release targets, so the drag path is completed geometrically: `Tree.StandardSetPreviewReceiver(SlotHitTestEx.HitTest(Tree, worldX, worldY))`. The stage is read back from the virtual link in the normal way.

⚙ **Plain wheel scrolling goes through `WorkflowInput.Scroller`,** which the surface component registers while attached. The JS wheel listener hands the deltas to .NET; the surface routes them and, when no subscriber refused, scrolls through its `SurfaceScroller` instead of letting the DOM scroll the canvas.

## Item templates — `VeloxDev.Razor.Templates`

**Generates a `.razor` + `.razor.cs` pair for all seven items** — not just the four markup-style ones. Fourteen files.

⚙ Siblings resolve by bare type name through `@namespace <your -ns>` + `@using <your -ns>` (`<GridDecorator>`, `<MinimapOverlay>`, `<LinkView>`, `<TemplateSelector>`, `<NodeView>`, `<SlotView>`), so all seven go into one namespace as usual.

⚙ **The tree declares the link menu as a fragment.** The generated surface passes `<LinkMenu Context="link">…</LinkMenu>`; each entry receives the right-pressed link, and the surface renders the chrome and, on right-press, opens the menu itself, positions it, and suspends the input router while it is open. Add or remove entry `<button>`s there — this GUI has no binding for menu items, so the handler is inline.

⚙ **Inert on this pack, each marked "accepted for cross-GUI CLI parity":** slot `slotBackground`; tree `surfaceBorderBrush`, `surfaceBorderThickness`, `surfaceCornerRadius`; decorator `gridBackground`, `minorGridColor`, `majorGridColor`. Those three decorator colours are drawn by the surface's own CSS/JS rather than by the decorator's parameters.

⚙ **Colours are also runtime parameters here** — each symbol becomes the fallback default of a Blazor `[Parameter]`:

```csharp
private string BackgroundCss => Background ?? ToCss("TemplateNodeBackground");
```

A colour can be set at generation time or at render time, which the other packs cannot do.

⚙ **There is no decorator with a `RulerBand` to ask.** The surface passes its `RulerThickness` parameter straight into `Tree.SetVirtualizeInset(left: RulerThickness, top: RulerThickness)`, so nodes under the ruler are not culled.

⚙ **The generic node's input slot does not wrap `WorkflowSlotConnectionBehavior`** — `SlotView` already wraps itself, and wrapping twice doubles both the anchor measurement and the gesture. The demo carries the same rule.
