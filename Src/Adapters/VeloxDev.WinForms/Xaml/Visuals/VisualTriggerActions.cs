using System.Runtime.CompilerServices;
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
/// <para>
/// When it completes or is stopped, the value it reached is written at <see cref="BindingPriority.LocalValue"/> and
/// the animation layer is cleared. The end value therefore holds at local precedence (HoldEnd) instead of shadowing
/// every later write at animation precedence: a later local write overrides it, and clearing the local value lets
/// the style, trigger or default value show through again.
/// </para>
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
/// of it, and <see cref="VisualTransition.Stop"/> ends it where it stands. As with
/// <see cref="TransitionAction"/>, the value reached when the chain ends is held at
/// <see cref="BindingPriority.LocalValue"/> after the animation layer is cleared.
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
/// <para>
/// A completed or stopped animation holds its last value by writing it at <see cref="BindingPriority.LocalValue"/>
/// and clearing the animation layer, so the value never keeps outranking later writes at animation precedence.
/// </para>
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

        var animated = new VisualDependencyTransitionProperty(target, property);
        Track(animated);
        animated.AttachCompleter(effect);

        // 走 Transition<T> 而不是直接拿 scheduler.Execute：后者创建的 run 不会被登记，TransitionCore.Exit
        // 就停不掉它。Transition<T> 会在目标锁内登记，并顺带 supersede 上一趟互斥动画。
        var transition = Transition<VisualElement>.Create().Effect(effect);
        transition.GetState().SetValue(animated, to);
        try
        {
            Transition<VisualElement>.Execute(target, new[] { transition }, CanMutualTask: true);
        }
        catch
        {
            // 校验失败时动画根本没有启动，不会有 Finally 来收尾；把登记与回调撤掉，值保持原样。
            animated.Rollback();
            throw;
        }
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
        var animated = new VisualDependencyTransitionProperty(target, property);

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

            segment.GetState().SetValue(animated, to);
            var segmentEffect = new TransitionEffect
            {
                Duration = segmentDuration,
                Ease = ease ?? Eases.Cubic.Out,
                LoopTime = loopTime,
                IsAutoReverse = isAutoReverse,
            };
            segment.Effect(segmentEffect);

            // 链的最后一段最后结束；只在这里挂收尾，避免前面每段的 Finally 把动画层提前清掉。
            if (index == ordered.Count - 1)
            {
                animated.AttachCompleter(segmentEffect);
            }

            previousOffset = offset;
            if (index < ordered.Count - 1)
            {
                segment = segment.Then<Transition<VisualElement>>();
            }
        }

        Track(animated);
        try
        {
            Transition<VisualElement>.Execute(target, new[] { root }, CanMutualTask: true);
        }
        catch
        {
            animated.Rollback();
            throw;
        }
    }

    /// <summary>Stops every animation currently running on <paramref name="target"/>.</summary>
    /// <param name="target">The element whose animations are stopped.</param>
    /// <remarks>
    /// The value each running animation had reached is held at <see cref="BindingPriority.LocalValue"/> and the
    /// animation layer is cleared, so the stop freezes where it stands without leaving a value that outranks every
    /// later write. Use it when a trigger exits or an element is torn down.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="target"/> is <see langword="null"/>.</exception>
    public static void Stop(VisualElement target)
    {
        if (target is null) throw new ArgumentNullException(nameof(target));

        var active = TakeActive(target);
        TransitionCore.Exit(target, IncludeMutual: true, IncludeNoMutual: false);
        foreach (var animated in active)
        {
            animated.HoldCurrentValue();
        }
    }

    // 按目标弱引用登记正在动画的属性；ConditionalWeakTable 保证表面释放后不滞留元素。
    private static readonly ConditionalWeakTable<VisualElement, Dictionary<DependencyProperty, VisualDependencyTransitionProperty>> ActiveAnimations = new();
    private static readonly object ActiveAnimationsLock = new();

    private static void Track(VisualDependencyTransitionProperty animated)
    {
        lock (ActiveAnimationsLock)
        {
            ActiveAnimations.GetOrCreateValue(animated.Target)[animated.Property] = animated;
        }
    }

    // 只有仍登记在册的实例才移除：被新动画取代的旧实例不能清掉新动画正在写的层位。
    internal static bool Untrack(VisualDependencyTransitionProperty animated)
    {
        lock (ActiveAnimationsLock)
        {
            if (ActiveAnimations.TryGetValue(animated.Target, out var map)
                && map.TryGetValue(animated.Property, out var current)
                && ReferenceEquals(current, animated))
            {
                map.Remove(animated.Property);
                return true;
            }

            return false;
        }
    }

    internal static bool HasActive(VisualElement target)
    {
        lock (ActiveAnimationsLock)
        {
            return ActiveAnimations.TryGetValue(target, out var map) && map.Count > 0;
        }
    }

    private static List<VisualDependencyTransitionProperty> TakeActive(VisualElement target)
    {
        lock (ActiveAnimationsLock)
        {
            if (!ActiveAnimations.TryGetValue(target, out var map))
            {
                return [];
            }

            var active = new List<VisualDependencyTransitionProperty>(map.Values);
            map.Clear();
            return active;
        }
    }
}

// 把 (元素, DP) 适配成过渡系统的可动画路径；GetValue/SetValue 直接用持有的元素，忽略调度器传入的目标。
internal sealed class VisualDependencyTransitionProperty : ITransitionProperty
{
    private readonly VisualElement _target;
    private readonly DependencyProperty _property;
    private EventHandler<TransitionEventArgs>? _finallyHandler;
    private TransitionEffect? _finallyEffect;

    internal VisualDependencyTransitionProperty(VisualElement target, DependencyProperty property)
    {
        _target = target;
        _property = property;
    }

    internal VisualElement Target => _target;

    internal DependencyProperty Property => _property;

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

    // 动画自然结束或被取消时把最末值落到本地层并清掉动画层；否则 Animation 层会永远压住 local/style。
    internal void AttachCompleter(TransitionEffect effect)
    {
        DetachCompleter();
        _finallyEffect = effect;
        _finallyHandler = OnFinally;
        effect.Finally += _finallyHandler;
    }

    internal void DetachCompleter()
    {
        if (_finallyEffect is { } effect && _finallyHandler is { } handler)
        {
            effect.Finally -= handler;
        }

        _finallyEffect = null;
        _finallyHandler = null;
    }

    private void OnFinally(object? sender, TransitionEventArgs e)
    {
        // 样式里的同一个 effect 会被克隆给多个元素；回调的 sender 是触发它的目标，只处理自己的那个。
        if (!ReferenceEquals(sender, _target))
        {
            return;
        }

        // 先摘回调：即使是已被取代的旧实例，也不该让调用方复用的 effect 累积处理器。
        DetachCompleter();

        // 被新动画取代的旧实例不在册上，不能把新动画正在写的层位清掉。
        if (VisualTransition.Untrack(this))
        {
            HoldCurrentValue();
        }
    }

    // 把动画层当前值改成 Local 层，再摘掉 Animation 层：值留住（HoldEnd），但不再压过后续写入。
    internal void HoldCurrentValue()
    {
        DetachCompleter();

        // DP 存储不是线程安全的；无句柄或已在句柄线程上时直接写，否则投回表面线程。
        if (_target.Surface is { IsHandleCreated: true, IsDisposed: false } surface && surface.InvokeRequired)
        {
            try
            {
                surface.BeginInvoke(new Action(HoldCurrentValueCore));
                return;
            }
            catch (InvalidOperationException)
            {
                // 句柄在检查与投递之间消失：退回就地写入。
            }
        }

        HoldCurrentValueCore();
    }

    private void HoldCurrentValueCore()
    {
        var value = _property.GetValue(_target);
        _property.SetValue(_target, value, BindingPriority.LocalValue);
        _property.ClearValue(_target, BindingPriority.Animation);
    }

    // 启动失败时撤回登记与回调，不动属性值。
    internal void Rollback()
    {
        VisualTransition.Untrack(this);
        DetachCompleter();
    }
}
