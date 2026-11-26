# VeloxDev animation on the WinForms XAML surface

Easel's XAML surface animates itself: the engine ships inside Easel (`Easel.Animation`), and markup trigger actions,
`KeyFrameAnimation`, `TriggerActionGroup`, `VisualTransition` and element `Transitions` need no adapter package or
backend registration. The full guide — timing, markup actions, element transitions, animatable types and
composition timing — is Easel's:
[`Easel/skills/easel-build-winforms-xaml-ui/references/animation.md`](../../../../Easel/skills/easel-build-winforms-xaml-ui/references/animation.md).

## Use Easel's API

```csharp
Easel.Visuals.VisualTransition.Animate(
    element,
    Easel.Visuals.VisualElement.MarginProperty,
    new Easel.Controls.Thickness(0, 8, 0, 0),
    new Easel.Animation.AnimationSpec
    {
        Duration = TimeSpan.FromMilliseconds(180),
        Ease = Easel.Animation.Eases.Cubic.Out,
    });
```

- The engine writes frames at `Easel.BindingPriority.Animation` and holds the reached value at
  `BindingPriority.LocalValue` when it completes or is stopped.
- Supersede is per dependency property: a new animation on the same DP replaces the running one; a
  `TriggerActionGroup` can animate several DPs at once.
- Supported interpolated types: numeric (`double`, `float`, `int`, `long`, `short`, `byte`, `decimal`),
  `Point`/`PointF`, `Size`/`SizeF`, `Rectangle`/`RectangleF`, `Padding`, `Thickness`, `Matrix`, `Matrix4x4`,
  `Color`, `Brush` and `Transform3D` (plus nullable variants). Anything else is rejected at start with
  `NotSupportedException`.
- Start the first animation on the UI thread. Frames are paced by the engine and marshalled through the
  element's `VisualHost`.

VeloxDev.Core's `Transition<T>` builder is **not** wired to Easel elements: the old adapter that adapted
Easel dependency properties into `ITransitionProperty` has been removed, and `VeloxDev.WinForms` carries no
Easel animator or bootstrap. Use `VisualTransition` and the trigger actions above instead.

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
