# WinForms visual XAML animations

`VeloxDev.WinForms` has two animation surfaces sharing `VeloxDev.TransitionSystem`: markup trigger
actions and code-behind transitions on a `VisualElement`. Markup uses `TransitionAction`,
`KeyFrameAnimation` and `TriggerActionGroup` inside `<Trigger.EnterAction>` / `<Trigger.ExitAction>`;
the `XamlTransition` HWND helper is not part of the visual runtime.

The visual runtime is documented in
[the workflow skill's XAML reference](../../../veloxdev-create-workflow/references/practices/gui/winforms-xaml.md).

## Markup trigger actions

```xml
<Trigger Property="IsEnabled" Value="False">
  <Trigger.EnterAction>
    <TransitionAction Property="Width" To="120" Duration="0:0:0.2" Ease="CubicOut" />
  </Trigger.EnterAction>
  <Trigger.ExitAction>
    <KeyFrameAnimation Property="Width" Duration="0:0:0.3" Ease="Linear">
      <KeyFrame Offset="0" Value="120" />
      <KeyFrame Offset="1" Value="40" />
    </KeyFrameAnimation>
  </Trigger.ExitAction>
</Trigger>
```

Several actions in one `EnterAction`/`ExitAction` are emitted as a `TriggerActionGroup`. A custom
action derives `Easel.Visuals.VisualTriggerAction` and overrides
`Invoke(VisualElement target)`; in a template trigger the target is the same `TargetName` part the
setters write. The bridge is `VisualTransition.Animate(element, property, value, effect)` and
`VisualTransition.AnimateKeyFrames(...)`; `VisualTransition.Stop(element)` ends the running
animation. Frames write at `BindingPriority.Animation`, and a new animation on the same element
supersedes the previous one.

## Animating a visual element

A visual element's dependency properties are exposed as CLR wrappers (`Opacity`, `Transform`,
`Margin`, `Width`, `Background`, `CornerRadius`, …). `Transition<T>` writes those wrappers, so the
transition sets the element's local priority layer each frame:

```csharp
using VeloxDev.TransitionSystem;
using Easel.Visuals;

Transition<VisualElement>.Create()
    .Property(v => v.Margin, new Easel.Controls.Thickness(0, 8, 0, 0))
    .Property(v => v.CornerRadius, 8f)
    .Effect(new TransitionEffect
    {
        Duration = TimeSpan.FromMilliseconds(180),
        Ease = Eases.Cubic.Out,
    })
    .Execute(element);
```

- `T` is the object the animation writes into. Use `VisualElement` (or the concrete control) and name
  a DP-backed CLR property: `Margin`, `Padding`, `Width`, `Height`, `CornerRadius`, `Background`, or
  your own `[XamlProperty]`. A DP-backed setter invalidates the tree through its
  `AffectsMeasure`/`AffectsArrange`/`AffectsRender` metadata.
- `Visual.Opacity`, `Transform`, `Clip` and `Effect` are plain visual properties with no invalidation.
  To fade, expose a DP on your control that feeds `Opacity` and mark it `AffectsRender`.
- A transition writes the local layer; a style or trigger value is overridden while it runs.
- The visual tree is painted by one `VisualHost`; frames are posted through `Control.BeginInvoke`, so
  start the first animation on the UI thread.
- A declaration is immutable and reusable. Cache a fixed-endpoint one in `static readonly` and build
  a computed one at the call site.

## Cache versus per-call

Same shape and endpoint every time → cache it:

```csharp
private static readonly Transition<VisualElement> GrowIn =
    Transition<VisualElement>.Create()
        .Property(v => v.CornerRadius, 8f)
        .Effect(new TransitionEffect
        {
            Duration = TimeSpan.FromMilliseconds(160),
            Ease = Eases.Sine.InOut,
        });

// …elsewhere
GrowIn.Execute(border);
```

An endpoint read from a local is frozen when the declaration is built, so build that one per caller.

## Animatable values

Core samplers cover `double`, `float`, `int`, `long`, `Color`, `Point`, `PointF`, `Size`, `SizeF`,
`Rectangle`, `RectangleF`, `Vector2`, `Vector3`, `Vector4`, `Quaternion`, `Padding` and `Thickness`.
`Thickness` covers `Margin` and `Padding`.

`decimal`, `bool`, `string` and enums have no sampler. The transition engine skips an unsampled path
instead of throwing, so an animation can "run" without changing anything. A custom type needs an
`ISampler` registration before it will move.

## Custom visual properties

A custom visual control registers an animatable property with `DependencyProperty.Register` and a
CLR wrapper, then animates the wrapper like any other property:

```csharp
public static readonly DependencyProperty GlowProperty = DependencyProperty.Register(
    nameof(Glow), typeof(double), typeof(MyVisual),
    new PropertyMetadata(0d) { AffectsRender = true });

public double Glow
{
    get => (double)GetValue(GlowProperty)!;
    set => SetValue(GlowProperty, value);
}

// Transition<MyVisual>.Create().Property(v => v.Glow, 1d)…
```

`AffectsRender` makes the tree repaint; without it the value changes but nothing redraws.

## Limits

- Markup actions animate one dependency property each; there is no storyboard/timeline object, so
  chain several actions with `TriggerActionGroup` or write a custom `VisualTriggerAction`.
- `decimal`, `bool`, `string` and enums do not animate.
- `NativeControlHost` content is a child HWND and is not part of the visual animation.
- `Visual.Opacity`, `Transform`, `Clip` and `Effect` live on the visual base and are plain properties
  with no change notification; animate a DP-backed element property or add a DP that feeds them.
