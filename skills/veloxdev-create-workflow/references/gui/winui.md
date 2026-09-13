# WinUI 3 / Windows App SDK

`VeloxDev.WinUI` · targets `net8.0-windows10.0.19041.0` and `net10.0-windows10.0.19041.0`

⚙ **Reference implementation:** `Examples/Workflow/WinUI Trimmed/Demo/Views/Workflow/` — read `TreeView.xaml` and `LinkView.xaml` **together**; they are the pair that implements the offset frame described below, and neither makes sense alone. ViewModels at `Demo/ViewModels/Workflow/`.

## Writing the surface

The attached properties and the `PART_` names are the same as WPF — see [wpf.md](wpf.md#writing-the-surface); only the `clr-namespace` assembly changes to `VeloxDev.WinUI`.

⚙ **Attach the zoom handler to the ScrollViewer, not the Canvas**, and expect one native scroll before it can be marked handled: WinUI has no preview/tunnel phase and marks the wheel event handled itself, so the adapter registers with `handledEventsToo: true`. Attaching to the `Canvas` only covers the hit-testable area.

## Adding your own content to a node

`<Viewbox Stretch="Uniform">` around content pinned to design size, as on WPF — your controls go inside the design canvas at design-size coordinates.

⚙ **Alias `Path` in code-first views.** Generated code uses `Microsoft.UI.Xaml.Shapes.Path` because an implicit `using System.IO` collides with it. The demo avoids the alias only because it does not pull in those implicit usings; your project may.

## Links — the offset frame

WinUI has a real constraint the desktop-shaped code has to respect, so this adapter positions and draws links differently from WPF:

⚙ **The canvas pan is baked into the link geometry, and the element is positioned at the negative offset:**

```csharp
Canvas.SetLeft(this, -layout.ActualOffset.Horizontal);
Canvas.SetTop(this,  -layout.ActualOffset.Vertical);
// Width/Height come from layout.ActualSize; the baked geometry lands inside [0, ActualSize]
```

The point is to keep the geometry non-negative and inside the element's own box, so nothing is clipped and no renderer limit is approached.

⚙ **`Clip` is cleared in three places** — the container `Grid`, the `Path`, and the root XAML. If you re-template a WinUI link view and lines get truncated at large coordinates, a clip reappeared.

⚙ **Do not bind the link view's `Width`/`Height` to the canvas element.** That reintroduces exactly the clipping the offset frame exists to avoid; the view drives its own box from `ActualSize`.

⚙ Slot anchors on this adapter are measured **canvas-local** and written through `SlotAnchorFromCanvasLocal` (the identity form), because the composition translation already inverts the canvas pan. Taking the visual-centre form here subtracts `ActualOffset` a second time and shifts **every** link by a constant — the single most likely mistake if you port view code from WPF or Avalonia.

⚙ Pooled views can lag a zoom by ~100 ms unless the surface virtualizes right after repositioning. The adapter does this for you; if you write your own surface, do the same.

## Item templates — `VeloxDev.WinUI.Templates`

**Generates** `.xaml` + `.xaml.cs` for node, slot, link and tree, and a single `.cs` for selector, decorator and minimap.

⚙ **The tree uses the offset frame**, subscribing to `CanvasLayout.ActualOffset` and `ActualSize`, positioning with `Canvas.SetLeft/Top(−offset)`, baking `+offset` into the geometry, and clearing `Clip` in all three places. That is the code to read when you need to change how links are drawn.

⚙ **`slotBorderColor` (`-bc`) is accepted and discarded** on this pack.

⚙ **The template does not emit `x:Name="Root"`**, although the demo's tree has one and MAUI's template requires it. Do not assume the packs are symmetric.

⚙ The demo's `TreeView.xaml.cs` carries a leftover drift probe from debugging. It is not part of the contract — do not port it into your project.
