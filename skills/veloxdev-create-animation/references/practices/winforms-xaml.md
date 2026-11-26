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

`VisualTransition.Animate` and the trigger actions write frames at `Easel.BindingPriority.Animation` and hold the
reached value at `Easel.BindingPriority.LocalValue` when they complete or are stopped. An element-level `Transitions`
entry instead releases the animation layer when it ends, so the trigger/style value shows through; the first style
application is suppressed. See [the Easel reference](../../../../Easel/skills/easel-build-winforms-xaml-ui/references/animation.md#element-transitions).

VeloxDev.Core's `Transition<T>` builder does **not** target Easel dependency properties, and `VeloxDev.WinForms`
carries no Easel animator or bootstrap. Use `VisualTransition` and the trigger actions above instead.

## Workflow-view specifics

- Animate node/link view DP-backed properties (`Opacity`, `BorderBrush`, `Margin`, `Width`/`Height` on a card).
- `WorkflowSurfaceBehavior` writes the camera matrix to `PART_Canvas.Transform`; do not animate that matrix
  concurrently with a pan/zoom gesture. Animate a card, not the camera, unless you own the gesture lifecycle.
- The parent `Viewbox`/camera scales a node's design grid; an animation inside the node template runs in design
  coordinates and is scaled with the card.
- Native islands (the focused text editor's IME window) and OS file dialogs are outside the visual animation;
  the workflow minimap, HUD and slot views are `VisualElement`s in the same tree.
- Stop/dispose animations when a pooled view is recycled; a reused view must not keep an animation writing its
  old DataContext.

## Limits

- Easel has no storyboard/timeline object; chain one-property actions with `TriggerActionGroup` or a custom
  `VisualTriggerAction`.
- Per-keyframe easing is not supported by `KeyFrameAnimation`; one ease applies to the chain.
