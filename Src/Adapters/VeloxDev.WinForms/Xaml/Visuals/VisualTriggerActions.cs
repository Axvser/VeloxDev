using VeloxDev.TransitionSystem;
using VeloxDev.TransitionSystem.Abstractions;

namespace VeloxDev.WinForms.Xaml.Visuals;

/// <summary>An action run by a <see cref="VisualTrigger"/> when it enters or exits its active state.</summary>
public abstract class VisualTriggerAction
{
    /// <summary>Runs the action against the element the trigger applies to.</summary>
    /// <param name="target">The styled element, or the named template part the trigger targets.</param>
    public abstract void Invoke(VisualElement target);
}

/// <summary>Runs several actions in declaration order when a trigger enters or exits.</summary>
public sealed class TriggerActionGroup : VisualTriggerAction
{
    /// <summary>The actions to run, in order.</summary>
    public IList<VisualTriggerAction> Actions { get; } = new List<VisualTriggerAction>();

    /// <inheritdoc />
    public override void Invoke(VisualElement target)
    {
        foreach (var action in Actions)
        {
            action?.Invoke(target);
        }
    }
}

/// <summary>One value at a normalized point in a <see cref="KeyFrameAnimation"/>.</summary>
public sealed class KeyFrame
{
    /// <summary>The position in the animation, from 0 (start) to 1 (end).</summary>
    public double Offset { get; set; }

    /// <summary>The value the animated property reaches at <see cref="Offset"/>.</summary>
    public object? Value { get; set; }
}

/// <summary>
/// A trigger action that animates one dependency property to <see cref="To"/> through the VeloxDev transition
/// system.
/// </summary>
/// <remarks>
/// The animation writes at <see cref="BindingPriority.Animation"/>, so it outranks a local value, style setter or
/// trigger setter while it runs. A new animation started on the same element supersedes the running one.
/// </remarks>
public sealed class TransitionAction : VisualTriggerAction
{
    /// <summary>The dependency property to animate, or <see langword="null"/> while unconfigured.</summary>
    public DependencyProperty? Property { get; set; }

    /// <summary>The value the animation ends at.</summary>
    public object? To { get; set; }

    /// <summary>The timing and easing of the animation; omitted means a 240 ms ease-out.</summary>
    public TransitionEffect? Effect { get; set; }

    /// <inheritdoc />
    public override void Invoke(VisualElement target)
    {
        if (target is null) throw new ArgumentNullException(nameof(target));
        if (Property is null)
        {
            throw new InvalidOperationException("A TransitionAction needs a Property.");
        }

        VisualTransition.Animate(target, Property, To, Effect);
    }
}

/// <summary>
/// A trigger action that animates one dependency property through a sequence of keyframes.
/// </summary>
/// <remarks>
/// Each adjacent pair of keyframes becomes one segment whose duration is proportional to its slice of
/// <see cref="Duration"/>. The chain is one mutual animation: a later animation on the same element supersedes all
/// of it, and <see cref="VisualTransition.Stop"/> ends it where it stands.
/// </remarks>
public sealed class KeyFrameAnimation : VisualTriggerAction
{
    /// <summary>The dependency property to animate, or <see langword="null"/> while unconfigured.</summary>
    public DependencyProperty? Property { get; set; }

    /// <summary>The total duration of all segments; 240 milliseconds by default.</summary>
    public TimeSpan Duration { get; set; } = TimeSpan.FromMilliseconds(240);

    /// <summary>The easing applied to every segment; <c>Cubic.Out</c> by default.</summary>
    public IEaseCalculator Ease { get; set; } = Eases.Cubic.Out;

    /// <summary>Additional full passes of each segment; zero runs once and <see cref="int.MaxValue"/> repeats forever.</summary>
    public int LoopTime { get; set; }

    /// <summary>Whether each segment also runs backwards.</summary>
    public bool IsAutoReverse { get; set; }

    /// <summary>The keyframes, in any order; they are sorted by <see cref="KeyFrame.Offset"/> when invoked.</summary>
    public IList<KeyFrame> KeyFrames { get; } = new List<KeyFrame>();

    /// <inheritdoc />
    public override void Invoke(VisualElement target)
    {
        if (target is null) throw new ArgumentNullException(nameof(target));
        if (Property is null)
        {
            throw new InvalidOperationException("A KeyFrameAnimation needs a Property.");
        }

        // 集合是公开可写的；null 元素排到比较器里会先炸成 NRE，而它本来就没有可用的 Offset。
        var frames = KeyFrames
            .Where(static frame => frame is not null)
            .OrderBy(static frame => frame.Offset)
            .ToArray();
        if (frames.Length == 0)
        {
            return;
        }

        VisualTransition.AnimateKeyFrames(target, Property, frames, Duration, Ease, LoopTime, IsAutoReverse);
    }
}

/// <summary>
/// Runs a dependency-property animation on a <see cref="VisualElement"/> through the VeloxDev transition system.
/// </summary>
/// <remarks>
/// <see cref="VisualDependencyTransitionProperty"/> makes one <see cref="DependencyProperty"/> look like any other
/// animatable path; the WinForms adapter's <see cref="Interpolator"/> prepares and samples it, and its scheduler
/// owns frame pacing on the surface's UI thread. No transition code is duplicated here.
/// </remarks>
public static class VisualTransition
{
    /// <summary>Animates <paramref name="property"/> on <paramref name="target"/> to <paramref name="to"/>.</summary>
    /// <param name="target">The element whose property is animated.</param>
    /// <param name="property">The dependency property to animate.</param>
    /// <param name="to">The value the animation ends at.</param>
    /// <param name="effect">The effect to run; omitted means a 240 ms ease-out.</param>
    /// <remarks>A new animation supersedes whatever is already running on <paramref name="target"/>.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="target"/> or <paramref name="property"/> is <see langword="null"/>.</exception>
    public static void Animate(
        VisualElement target,
        DependencyProperty property,
        object? to,
        TransitionEffect? effect = null)
    {
        if (target is null) throw new ArgumentNullException(nameof(target));
        if (property is null) throw new ArgumentNullException(nameof(property));

        // 调用方常把 1 / 1f 交给一个 Thickness 属性；在这一处统一成属性类型，sampler 就不必猜。
        if (to is not null && !property.PropertyType.IsInstanceOfType(to))
        {
            to = XamlRuntime.ConvertValue(to, property.PropertyType);
        }

        effect ??= new TransitionEffect
        {
            Duration = TimeSpan.FromMilliseconds(240),
            Ease = Eases.Cubic.Out,
        };

        // 走 Transition<T> 而不是直接拿 scheduler.Execute：后者创建的 run 不会被登记，TransitionCore.Exit
        // 就停不掉它。Transition<T> 会在目标锁内登记，并顺带 supersede 上一趟互斥动画。
        var transition = Transition<VisualElement>.Create().Effect(effect);
        transition.GetState().SetValue(new VisualDependencyTransitionProperty(target, property), to);
        Transition<VisualElement>.Execute(target, new[] { transition }, CanMutualTask: true);
    }

    /// <summary>Animates <paramref name="property"/> through <paramref name="frames"/>, one segment per adjacent pair.</summary>
    /// <param name="target">The element whose property is animated.</param>
    /// <param name="property">The dependency property to animate.</param>
    /// <param name="frames">The keyframes; they are sorted by <see cref="KeyFrame.Offset"/>.</param>
    /// <param name="duration">The total duration of the whole chain.</param>
    /// <param name="ease">The easing applied to every segment; omitted means <c>Cubic.Out</c>.</param>
    /// <param name="loopTime">Additional full passes; zero runs once and <see cref="int.MaxValue"/> repeats forever.</param>
    /// <param name="isAutoReverse">Whether each pass also runs backwards.</param>
    /// <exception cref="ArgumentNullException"><paramref name="target"/>, <paramref name="property"/> or <paramref name="frames"/> is <see langword="null"/>.</exception>
    public static void AnimateKeyFrames(
        VisualElement target,
        DependencyProperty property,
        IReadOnlyList<KeyFrame> frames,
        TimeSpan duration,
        IEaseCalculator? ease = null,
        int loopTime = 0,
        bool isAutoReverse = false)
    {
        if (target is null) throw new ArgumentNullException(nameof(target));
        if (property is null) throw new ArgumentNullException(nameof(property));
        if (frames is null) throw new ArgumentNullException(nameof(frames));
        if (frames.Count == 0)
        {
            return;
        }

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
        var root = Transition<VisualElement>.Create();
        var segment = root;
        var previousOffset = 0d;

        for (var index = 0; index < ordered.Count; index++)
        {
            var frame = ordered[index];
            var offset = XamlMath.Clamp(frame.Offset, 0d, 1d);
            var span = Math.Max(0d, offset - previousOffset);
            var segmentDuration = TimeSpan.FromTicks((long)Math.Round(ticks * span));
            var to = frame.Value;
            if (to is not null && !property.PropertyType.IsInstanceOfType(to))
            {
                to = XamlRuntime.ConvertValue(to, property.PropertyType);
            }

            segment.GetState().SetValue(new VisualDependencyTransitionProperty(target, property), to);
            segment.Effect(new TransitionEffect
            {
                Duration = segmentDuration,
                Ease = ease ?? Eases.Cubic.Out,
                LoopTime = loopTime,
                IsAutoReverse = isAutoReverse,
            });

            previousOffset = offset;
            if (index < ordered.Count - 1)
            {
                segment = segment.Then<Transition<VisualElement>>();
            }
        }

        Transition<VisualElement>.Execute(target, new[] { root }, CanMutualTask: true);
    }

    /// <summary>Stops every animation currently running on <paramref name="target"/>.</summary>
    /// <param name="target">The element whose animations are stopped.</param>
    /// <exception cref="ArgumentNullException"><paramref name="target"/> is <see langword="null"/>.</exception>
    public static void Stop(VisualElement target)
    {
        if (target is null) throw new ArgumentNullException(nameof(target));
        TransitionCore.Exit(target, IncludeMutual: true, IncludeNoMutual: false);
    }
}

// 把 (元素, DP) 适配成过渡系统的可动画路径；GetValue/SetValue 直接用持有的元素，忽略调度器传入的目标。
internal sealed class VisualDependencyTransitionProperty : ITransitionProperty
{
    private readonly VisualElement _target;
    private readonly DependencyProperty _property;

    internal VisualDependencyTransitionProperty(VisualElement target, DependencyProperty property)
    {
        _target = target;
        _property = property;
    }

    public string Path => $"{_target.GetType().Name}.{_property.Name}";

    public Type PropertyType => _property.PropertyType;

    public bool CanRead => true;

    public bool CanWrite => true;

    public object? GetValue(object? target) => _property.GetValue(_target);

    public bool SetValue(object target, object? value)
    {
        _property.SetValue(_target, value, BindingPriority.Animation);
        return true;
    }
}
