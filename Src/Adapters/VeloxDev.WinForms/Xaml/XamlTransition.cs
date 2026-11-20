using System;
using System.Collections.Generic;
using VeloxDev.TransitionSystem;
using VeloxDev.TransitionSystem.Abstractions;

namespace VeloxDev.WinForms.Xaml;

/// <summary>
/// Runs a dependency-property animation through the existing VeloxDev transition system.
/// </summary>
/// <remarks>
/// The bridge is deliberately thin. <see cref="DependencyTransitionProperty"/> makes one
/// <see cref="DependencyProperty"/> look like any other animatable path, the WinForms adapter's
/// <see cref="Interpolator"/> prepares and samples it, and the adapter's scheduler owns the frame pacing on the
/// control's UI thread. No transition code is duplicated here.
/// </remarks>
public static class XamlTransition
{
    /// <summary>
    /// Animates <paramref name="property"/> on <paramref name="target"/> to <paramref name="to"/>.
    /// </summary>
    /// <param name="target">The control whose property is animated.</param>
    /// <param name="property">The dependency property to animate.</param>
    /// <param name="to">The value the animation ends at.</param>
    /// <param name="effect">The effect to run; omitted means a 240 ms ease-out.</param>
    /// <remarks>
    /// A new animation supersedes whatever is already running on the control. That is the behaviour a trigger wants
    /// when the pointer enters and leaves quickly: the second transition restarts from the current value instead of
    /// queueing behind the first.
    /// </remarks>
    public static void Animate(
        VeloxDev.WinForms.Xaml.Controls.Control target,
        DependencyProperty property,
        object? to,
        TransitionEffect? effect = null)
    {
        if (target is null) throw new ArgumentNullException(nameof(target));
        if (property is null) throw new ArgumentNullException(nameof(property));
        if (target.IsDisposed || target.Disposing)
        {
            return;
        }

        // 调用方常把 1 / 1f 交给一个 Thickness 属性；在这一个入口统一成属性类型，sampler 就不必猜。
        if (to is not null && !property.PropertyType.IsInstanceOfType(to))
        {
            to = XamlRuntime.ConvertValue(to, property.PropertyType);
        }

        effect ??= new TransitionEffect
        {
            Duration = TimeSpan.FromMilliseconds(240),
            Ease = Eases.Cubic.Out,
        };

        // 走 Transition<T> 而不是直接拿 scheduler.Execute：后者创建的 run 不会被 scheduler 登记，
        // TransitionCore.Exit 就停不掉它。Transition<T> 会在目标锁内登记 run，并顺带 supersede 上一趟互斥动画。
        var transition = Transition<VeloxDev.WinForms.Xaml.Controls.Control>.Create().Effect(effect);
        transition.GetState().SetValue(new DependencyTransitionProperty(target, property), to);
        Transition<VeloxDev.WinForms.Xaml.Controls.Control>.Execute(target, new[] { transition }, CanMutualTask: true);
    }

    /// <summary>
    /// Animates <paramref name="property"/> through <paramref name="frames"/>, one segment per adjacent pair.
    /// </summary>
    /// <param name="target">The control whose property is animated.</param>
    /// <param name="property">The dependency property to animate.</param>
    /// <param name="frames">The keyframes; they are sorted by <see cref="KeyFrame.Offset"/>.</param>
    /// <param name="duration">The total duration of the whole chain.</param>
    /// <param name="ease">The easing applied to every segment; omitted means <c>Cubic.Out</c>.</param>
    /// <remarks>
    /// Offsets are normalized to <c>0..1</c>. A first frame above zero animates from the property's current value
    /// to that frame; a first frame at zero applies immediately. Each segment's duration is its share of
    /// <paramref name="duration"/>. The chain is one mutual run, so a new animation supersedes the whole sequence.
    /// </remarks>
    public static void AnimateKeyFrames(
        VeloxDev.WinForms.Xaml.Controls.Control target,
        DependencyProperty property,
        IReadOnlyList<KeyFrame> frames,
        TimeSpan duration,
        IEaseCalculator? ease = null)
    {
        if (target is null) throw new ArgumentNullException(nameof(target));
        if (property is null) throw new ArgumentNullException(nameof(property));
        if (frames is null) throw new ArgumentNullException(nameof(frames));
        if (target.IsDisposed || target.Disposing || frames.Count == 0)
        {
            return;
        }

        // 跳过 null 帧：它们是公开 IList 上的空元素，排序与求值都没法处理。
        var ordered = new List<KeyFrame>(frames.Count);
        foreach (var frame in frames)
        {
            if (frame is not null)
            {
                ordered.Add(frame);
            }
        }

        if (ordered.Count == 0)
        {
            return;
        }

        ordered.Sort(static (left, right) => left.Offset.CompareTo(right.Offset));

        var ticks = Math.Max(0L, duration.Ticks);
        var root = Transition<VeloxDev.WinForms.Xaml.Controls.Control>.Create();
        var segment = root;
        var previousOffset = 0d;

        for (var index = 0; index < ordered.Count; index++)
        {
            var frame = ordered[index];
            var offset = Clamp(frame.Offset, 0d, 1d);
            var span = Math.Max(0d, offset - previousOffset);
            var segmentDuration = TimeSpan.FromTicks((long)Math.Round(ticks * span));
            var to = frame.Value;
            if (to is not null && !property.PropertyType.IsInstanceOfType(to))
            {
                to = XamlRuntime.ConvertValue(to, property.PropertyType);
            }

            segment.GetState().SetValue(new DependencyTransitionProperty(target, property), to);
            segment.Effect(new TransitionEffect
            {
                Duration = segmentDuration,
                Ease = ease ?? Eases.Cubic.Out,
            });

            previousOffset = offset;
            if (index < ordered.Count - 1)
            {
                segment = segment.Then<Transition<VeloxDev.WinForms.Xaml.Controls.Control>>();
            }
        }

        Transition<VeloxDev.WinForms.Xaml.Controls.Control>.Execute(
            target,
            new[] { root },
            CanMutualTask: true);
    }

    /// <summary>Stops every animation currently running on <paramref name="target"/>.</summary>
    /// <param name="target">The control whose animations are stopped.</param>
    public static void Stop(VeloxDev.WinForms.Xaml.Controls.Control target)
    {
        if (target is null) throw new ArgumentNullException(nameof(target));
        TransitionCore.Exit(target, IncludeMutual: true, IncludeNoMutual: false);
    }
}

/// <summary>
/// Adapts <c>(Control, DependencyProperty)</c> to the transition system's animatable-path contract.
/// </summary>
/// <remarks>
/// The transition system reads and writes every value through this interface, so a dependency property needs no
/// special sampler and no change to Core or to the WinForms adapter.
/// </remarks>
internal sealed class DependencyTransitionProperty : ITransitionProperty
{
    private readonly VeloxDev.WinForms.Xaml.Controls.Control _target;
    private readonly DependencyProperty _property;

    public DependencyTransitionProperty(VeloxDev.WinForms.Xaml.Controls.Control target, DependencyProperty property)
    {
        _target = target;
        _property = property;
    }

    /// <inheritdoc />
    public string Path => $"{_target.GetType().Name}.{_property.Name}";

    /// <inheritdoc />
    public Type PropertyType => _property.PropertyType;

    /// <inheritdoc />
    public bool CanRead => true;

    /// <inheritdoc />
    public bool CanWrite => true;

    /// <inheritdoc />
    public object? GetValue(object? target)
        => target is System.Windows.Forms.Control control ? _property.GetValue(control) : null;

    /// <inheritdoc />
    public bool SetValue(object target, object? value)
    {
        if (target is not System.Windows.Forms.Control control)
        {
            return false;
        }

        if (control is VeloxDev.WinForms.Xaml.Controls.Control xaml)
        {
            xaml.SetValueFromTransition(_property, value);
        }
        else
        {
            _property.SetValue(control, value);
        }

        return true;
    }
}
