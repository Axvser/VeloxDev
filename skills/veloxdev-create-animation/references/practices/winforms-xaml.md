# VeloxDev animation on the WinForms XAML surface

The Easel animation contract, markup trigger-action syntax, keyframes, composition timing and the
no-backend behavior are documented by Easel:
[`Easel/skills/easel-build-winforms-xaml-ui/references/animation.md`](../../../../Easel/skills/easel-build-winforms-xaml-ui/references/animation.md).
This page only covers what **VeloxDev.WinForms** adds: injecting its transition engine and using `Transition<T>`
on Easel elements.

## Injection

**Install `VeloxDev.WinForms` 12.0.0 or later.** The package contains `EaselVisualAnimator`, an
`IVisualAnimator` bridge to `VeloxDev.TransitionSystem`, and registers it through a module initializer. A
module initializer runs before the first execution of code from the assembly — not on reference — so a host
that installs the package only for the animation backend calls the public bootstrap once at startup:

```csharp
EaselAnimationBootstrap.EnsureRegistered();   // VeloxDev.WinForms.XamlAnimation
```

No separate animation package or manual `AnimationRuntime.Animator` assignment is needed.

Consequences:

- Ensure a type from `VeloxDev.WinForms` has run, or call `EaselAnimationBootstrap.EnsureRegistered()`, before
  using markup trigger actions or `VisualTransition`; otherwise Easel's null animator throws the explicit
  "No IVisualAnimator is registered" exception.
- A host may overwrite `AnimationRuntime.Animator` with its own implementation. The most recently assigned
  animator wins.
- `CanAnimate` reflects VeloxDev's sampler table and `ISampleable` value types: a DP with neither is rejected at
  start, not silently held.
- The bridge writes frames at `Easel.BindingPriority.Animation` and holds the reached value at local precedence
  when the animation completes, is superseded or is stopped. It bridges `IEase` to VeloxDev's `IEaseCalculator`.

Markup trigger actions, `KeyFrameAnimation`, `TriggerActionGroup` and `VisualTransition` are the same Easel
API described in the Easel skill. There is no separate VeloxDev markup dialect.

## `Transition<T>` on an Easel element

Use VeloxDev's transition builder for code-behind animations:

```csharp
using VeloxDev.TransitionSystem;

Transition<Easel.Visuals.VisualElement>.Create()
    .Property(v => v.Margin, new Easel.Controls.Thickness(0, 8, 0, 0))
    .Property(v => v.CornerRadius, 8f)
    .Effect(new TransitionEffect
    {
        Duration = TimeSpan.FromMilliseconds(180),
        Ease = Eases.Cubic.Out,
    })
    .Execute(element);
```

- `T` is the object the transition writes. For an Easel element use `VisualElement` or the concrete control, and
  name a DP-backed property: `Margin`, `Padding`, `Width`, `Height`, `CornerRadius`, `Background`, or a custom
  `[XamlProperty]`.
- A code-behind transition writes frames at `BindingPriority.Animation`, the same layer a markup action
  writes, so either overrides a style/trigger value while it runs.
- A DP-backed setter invalidates through `AffectsMeasure`/`AffectsArrange`/`AffectsRender`: `Margin`, `Padding`,
  `Width` and `Height` are `AffectsMeasure`; `Opacity`, `Transform`, `Transform3D`, `Clip` and `Effect` are
  `AffectsRender`. On a raw `Visual` (not a `VisualElement`), `Opacity`, `Transform`, `Clip` and `Effect` are
  plain properties with no change notification; animate a DP that feeds them or call `InvalidateVisual` yourself.
- A declaration is immutable and reusable. Cache a fixed-endpoint one in `static readonly`; build a computed
  one at the call site. A new animation on the same element supersedes the previous one.
- Start the first animation on the UI thread; the WinForms adapter posts frames through `Control.BeginInvoke`.

## Sampled types

VeloxDev's samplers cover `double`, `float`, `int`, `long`, `Color`, `Point`, `PointF`, `Size`, `SizeF`,
`Rectangle`, `RectangleF` (and nullable `RectangleF`), `Vector2/3/4`, `Quaternion`, `Padding`, `Thickness`,
`Brush` (`Easel.Brush`), `Transform3D` (`Easel.Transform3D`) and `Matrix` (`System.Drawing.Drawing2D.Matrix`). `decimal`, `bool`, `string` and enums have no sampler and are
rejected/unsampled; register an `ISampler` for a custom type before animating it.

## Workflow-view specifics

- Animate node/link view DP-backed properties (`Opacity`, `BorderBrush`, `Margin`, `Width`/`Height` on a card).
- `WorkflowSurfaceBehavior` writes the camera matrix to `PART_Canvas.Transform`; do not animate that matrix
  concurrently with a pan/zoom gesture. Animate a card, not the camera, unless you own the gesture lifecycle.
- The parent `Viewbox`/camera scales a node's design grid; an animation inside the node template runs in design
  coordinates and is scaled with the card.
- Native islands (the focused text editor's IME window, OS file dialogs) are not part of the visual animation;
  the workflow minimap, HUD and slot views are `VisualElement`s in the same tree.
- Stop/dispose animations when a pooled view is recycled; a reused view must not keep an animation writing its
  old DataContext.

## Limits

- Easel has no storyboard/timeline object; chain one-property actions with `TriggerActionGroup` or a custom
  `VisualTriggerAction`.
- Per-keyframe easing is not supported by `KeyFrameAnimation`; one ease applies to the chain.
- A `Transform3D` animation re-warps the composed bitmap each frame; cache the pre-warp layer and keep the
  projected area small.
- The runtime's `VisualHost` remains the only HWND; animations never create child windows.
