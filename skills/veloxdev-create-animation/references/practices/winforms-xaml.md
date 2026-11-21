# WinForms XAML animations

`VeloxDev.WinForms` has two animation surfaces. Plain WinForms code uses the transition system
directly. The XAML layer adds a thin bridge, because XAML authors do not have property expressions:

- `VeloxDev.WinForms.Xaml.XamlTransition.Animate(control, property, value, effect)` — code-behind.
- `VeloxDev.WinForms.Xaml.TransitionAction` — a trigger action declared in XAML.

Both end in the same `Transition<VeloxDev.WinForms.Xaml.Controls.Control>` run through the WinForms
adapter's scheduler and samplers. The full runtime is documented in
[the workflow skill&#39;s XAML reference](../../../veloxdev-create-workflow/references/practices/gui/winforms-xaml.md).

## Markup form

```xml
<v:Button Content="Save">
  <v:Button.Triggers>
    <v:Trigger Property="IsPointerOver" Value="True">
      <v:Trigger.EnterAction>
        <v:TransitionAction Property="Background"
                            To="#FF2B6CFF"
                            Duration="0:0:0.18"
                            Ease="CubicOut" />
      </v:Trigger.EnterAction>
      <v:Trigger.ExitAction>
        <v:TransitionAction Property="Background"
                            To="#FF20242C"
                            Duration="0:0:0.24"
                            Ease="CubicOut" />
      </v:Trigger.ExitAction>
    </v:Trigger>
  </v:Button.Triggers>
</v:Button>
```

The same action works inside `v:Style.Triggers`, `DataTrigger` and `MultiTrigger`.

| Attribute    | Meaning                                                                                                  |
| ------------ | -------------------------------------------------------------------------------------------------------- |
| `Property` | Required. A DP on the target, or `Owner.Property` for an attached DP                                   |
| `To`       | End value converted to the property type; literal only                                                   |
| `Duration` | `TimeSpan` literal such as `0:0:0.2`                                                                 |
| `Ease`     | `Linear` / `Default`, or Sine/Quad/Cubic/Quart/Quint/Expo/Circ/Back/Elastic/Bounce with In/Out/InOut |

**Default effect only when both are omitted.** With no action effect, `XamlTransition.Animate` uses
240 ms `Cubic.Out`. An `Ease` without `Duration` creates a zero-length effect; a `Duration` without
`Ease` uses the transition system's default ease. Specify both when customizing either.

`EnterAction` runs when the trigger becomes active, `ExitAction` when it becomes inactive. Each action
writes a local value; it does not restore the previous value on its own, so write both for a
reversible hover/checked state.

## Keyframes

A `KeyFrameAnimation` chains ordinary transitions, one segment per adjacent pair:

```xml
<v:Trigger.EnterAction>
  <v:KeyFrameAnimation Property="Background" Duration="0:0:0.6" Ease="CubicInOut">
    <v:KeyFrame Offset="0" Value="#FF20242C" />
    <v:KeyFrame Offset="0.5" Value="#FF2B6CFF" />
    <v:KeyFrame Offset="1" Value="#FF20242C" />
  </v:KeyFrameAnimation>
</v:Trigger.EnterAction>
```

- `Offset` is `0..1`; frames are sorted, the first frame above zero animates from the current value,
  and a zero first frame applies immediately.
- Segment durations are proportional to offset deltas; the whole chain shares one `Ease` and one total
  `Duration` (240 ms `Cubic.Out` by default). Per-keyframe easing is not supported yet.
- The whole chain is one mutual run: a new animation supersedes it and `XamlTransition.Stop` ends it.
- `Value` is converted to the animated property's type like a normal transition endpoint.

Code-behind uses the same chain:

```csharp
XamlTransition.AnimateKeyFrames(
    control,
    MyControl.GlowProperty,
    [
        new KeyFrame { Offset = 0d, Value = 0d },
        new KeyFrame { Offset = 0.5d, Value = 0.5d },
        new KeyFrame { Offset = 1d, Value = 1d },
    ],
    TimeSpan.FromMilliseconds(300),
    Eases.Cubic.Out);
```

## Code-behind form

```csharp
using VeloxDev.TransitionSystem;
using VeloxDev.WinForms.Xaml;

XamlTransition.Animate(
    this,
    MyControl.GlowProperty,
    1d,
    new TransitionEffect
    {
        Duration = TimeSpan.FromMilliseconds(180),
        Ease = Eases.Cubic.Out,
    });

XamlTransition.Stop(this);
```

- Target must be a `VeloxDev.WinForms.Xaml.Controls.Control`, never a native `Control`.
- A later `Animate` on the same target supersedes the earlier mutual run. `Stop` ends it where it
  stands.
- Start the first animation on the UI thread: frames are posted through `Control.BeginInvoke`.
- `TransitionAction` and `KeyFrameAnimation` expose `Duration`, `Ease`, `LoopTime` and
  `IsAutoReverse` in XAML. `FPS` still needs code-behind.
- An `EnterAction` / `ExitAction` may contain several action elements. They run as one
  `TriggerActionGroup` in declaration order.

## Animatable values

Core samplers cover `double`, `float`, `int`, `long`, `Color`, `Point`, `PointF`, `Size`, `SizeF`,
`Rectangle`, `RectangleF`, `Vector2`, `Vector3`, `Vector4`, `Quaternion`, `Padding` and `Thickness`.
The WinForms adapter adds the `Thickness` sampler used by `Margin` / `Padding` /
`BorderThickness`.

`decimal`, `bool`, `string` and enums have no sampler. The transition engine warns and skips an
unsampled path instead of throwing, so an animation can “run” without changing anything. A custom
type needs an `ISampler` registration before it will move.

## Custom trigger actions

Derive from `VeloxDev.WinForms.Xaml.TriggerAction`, give it a public parameterless constructor and
public settable properties, then declare it under `EnterAction` / `ExitAction`. Nested content is not
supported; literal and `{StaticResource}` property values are.

## Limits

- XAML triggers exist only on XAML `Control`; native controls can use style setters but not triggers
  or `TransitionAction`.
- There is no storyboard/timeline/keyframe API; one action animates one DP to one value with one
  effect.
- `To` accepts a literal, not `{Binding}` / `{StaticResource}`.
- No implicit auto-reverse on trigger exit: provide the opposite `ExitAction`.
- There is no built-in `Opacity` DP; add a custom DP if the control paints from it. WinForms child windows do not composite over siblings, so alpha alone is not a general fade.
