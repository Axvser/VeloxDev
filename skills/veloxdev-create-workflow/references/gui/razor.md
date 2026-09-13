# Blazor / Razor

`VeloxDev.Razor` · targets `net6.0` + `Microsoft.AspNetCore.App`

⚙ **This GUI goes by two names, and the mismatch is only in the folder.** The package is `VeloxDev.Razor`, the template prefix is `razor`, but the demos live under **`Examples/Workflow/Blazor Trimmed/Demo`** — there is no `Razor` folder. Everywhere else the three agree.

⚙ **Reference implementation:** `Examples/Workflow/Blazor Trimmed/Demo/Components/Workflow/` — note `Components/`, the Blazor convention. Every role is a `.razor` + `.razor.cs` pair, including the selector, decorator and minimap. The JS half of the adapter ships in the package, not the demo.

There is a long-form adapter README at `Src/Adapters/VeloxDev.Razor/README.md`.

## Writing the surface

There is no canvas control and no attached properties — the surface is a **Blazor component**, and what the other adapters express as attached properties are its parameters:

| Parameter | Default | Meaning |
|---|---|---|
| `Tree` | `null` | the tree to host |
| `IsEnabled` | `false` | master switch |
| `ZoomEnabled` | `false` | enables wheel zoom |
| `ScrollViewerId` | `"veloxdev-wf-scroll"` | `id` of the scroll element |
| `CanvasId` | `"veloxdev-wf-canvas"` | `id` of the canvas element |
| `GridDecorator` / `Minimap` | `null` | `RenderFragment<SurfaceViewport>` |
| `ChildContent` | `null` | `RenderFragment<SurfaceCanvas>` — the node/slot content |
| `Background` / `GridColor` / `MajorGridColor` / `AxisColor` | CSS colours | surface visuals |
| `GridSpacing` / `MajorLineEvery` / `RulerThickness` | `40` / `5` / `28` | grid and ruler |

## Adding your own content to a node

The node card is a CSS transform of design-size content:

```css
transform: scale(collapsedWidth / 260);   /* the node's design width */
transform-origin: top left;
```

⚙ **`getBoundingClientRect()` includes that transform**, which is why slot anchors and links stay correct through a zoom without any of the measuring code the desktop adapters need. **Do not replace the transform with a layout-based position** — the measurement contract depends on the transform being in the rect.

So: put your controls on the design-size card as usual. The browser scales them.

## Zoom

⚙ The gesture is a **non-passive** `wheel` listener in JS, coalesced to one in-flight burst. `ZoomEnabled` alone is not enough — the JS half has to be initialised, or the canvas simply never zooms.

⚙ **After a zoom, the JS dispatches `veloxdev-wf-layout-changed` on the next animation frame**, and the slot layout listens for it to re-measure. Nodes reposition during the zoom and the slots do not, so without that re-measure links visibly detach from their ports. If you write your own zoom path, dispatch it.

⚙ **Near-edge zoom is deliberately clamped, never grown.** Centring the pivot by growing `NegativeOffset` while the scroll stays pinned pushes the visible viewport inward on every zoom step — a camera that drifts and then jumps. The adapter centres the pivot by scrolling within the reachable range and allows the pivot to sit slightly off-centre near an edge. Do not "harmonise" this with the desktop adapters' growth behaviour: the DOM has an edge margin they do not.

⚙ If you write your own zoom, take the pivot from `Math.Max(0, scroll)` — the effective scroll is `DOM scrollLeft − edgeX`, and a negative value feeds `WorldAtViewportCenter` nonsense.

## Item templates — `VeloxDev.Razor.Templates`

**Generates a `.razor` + `.razor.cs` pair for all seven items** — not just the four markup-style ones. Fourteen files.

⚙ Siblings resolve by bare type name through `@namespace <your -ns>` + `@using <your -ns>` (`<GridDecorator>`, `<MinimapOverlay>`, `<LinkView>`, `<TemplateSelector>`, `<NodeView>`, `<SlotView>`), so all seven go into one namespace as usual.

⚙ **Inert on this pack, each marked "accepted for cross-GUI CLI parity":** slot `slotBackground`; tree `surfaceBorderBrush`, `surfaceBorderThickness`, `surfaceCornerRadius`; decorator `gridBackground`, `minorGridColor`, `majorGridColor`. The decorator's colours are baked into the generated JS/CSS rather than driven by the symbols.

⚙ **Colours are also runtime parameters here** — each symbol becomes the fallback default of a Blazor `[Parameter]`:

```csharp
private string BackgroundCss => Background ?? ToCss("TemplateNodeBackground");
```

So on this GUI a colour can be set at generation time *or* at render time, which the other packs cannot do.

⚙ **There is no decorator with a `RulerBand` to ask.** Pass the ruler thickness to the virtualize inset explicitly, as the generated tree does.

⚙ **The generic node's input slot does not wrap `WorkflowSlotConnectionBehavior`** — `SlotView` already wraps itself, and wrapping twice doubles both the anchor measurement and the gesture. The demo carries the same comment.
