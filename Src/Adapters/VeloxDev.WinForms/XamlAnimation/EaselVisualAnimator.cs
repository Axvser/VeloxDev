using System.Runtime.CompilerServices;
using Easel.Animation;
using Easel.Visuals;
using VeloxDev.TransitionSystem;
using VeloxDev.TransitionSystem.Abstractions;
using EaselDependencyProperty = Easel.DependencyProperty;
using EaselVisualElement = Easel.Visuals.VisualElement;
using VeloxTransitionEffect = VeloxDev.TransitionSystem.TransitionEffect;

namespace VeloxDev.WinForms.XamlAnimation;

/// <summary>
/// Bridges Easel's <see cref="IVisualAnimator"/> contract to the VeloxDev transition engine.
/// </summary>
/// <remarks>
/// Easel owns the visual runtime and the animation contract; VeloxDev.WinForms supplies interpolation, frame pacing and the
/// hold-end bookkeeping. The registration in <see cref="EaselAnimationBootstrap"/> makes any process that references this
/// assembly use it automatically, while a host may still overwrite <see cref="AnimationRuntime.Animator"/>.
/// </remarks>
internal sealed class EaselVisualAnimator : IVisualAnimator
{
    /// <inheritdoc />
    public bool CanAnimate(EaselVisualElement target, EaselDependencyProperty property)
    {
        // 采样器注册在 Interpolator 的静态构造里；只查 InterpolatorCore 会漏掉 Matrix/RectangleF 等。
        RuntimeHelpers.RunClassConstructor(typeof(Interpolator).TypeHandle);

        if (InterpolatorCore.TryGetInterpolator(property.PropertyType, out var sampler) && sampler is not null)
        {
            return true;
        }

        if (property.PropertyType.IsValueType)
        {
            try
            {
                return property.GetValue(target) is ISampleable;
            }
            catch (Exception)
            {
                // 读不到当前值就当作不可采样。
            }
        }

        return false;
    }

    /// <inheritdoc />
    public void Animate(EaselVisualElement target, EaselDependencyProperty property, object? to, AnimationSpec spec)
    {
        var effect = CreateEffect(spec);
        var animated = new EaselDependencyTransitionProperty(target, property);
        Track(animated);
        animated.AttachCompleter(effect);

        // 走 Transition<T> 而不是直接拿 scheduler.Execute：后者创建的 run 不会被登记，TransitionCore.Exit
        // 就停不掉它。Transition<T> 会在目标锁内登记，并顺带 supersede 上一趟互斥动画。
        var transition = Transition<EaselVisualElement>.Create().Effect(effect);
        transition.GetState().SetValue(animated, to);
        try
        {
            Transition<EaselVisualElement>.Execute(target, new[] { transition }, CanMutualTask: true);
        }
        catch
        {
            animated.Rollback();
            throw;
        }
    }

    /// <inheritdoc />
    public void AnimateKeyFrames(
        EaselVisualElement target,
        EaselDependencyProperty property,
        IReadOnlyList<KeyFrame> frames,
        AnimationSpec spec)
    {
        if (frames.Count == 0)
        {
            return;
        }

        var ticks = Math.Max(0L, spec.Duration.Ticks);
        var root = Transition<EaselVisualElement>.Create();
        var segment = root;
        var previousOffset = 0d;
        var animated = new EaselDependencyTransitionProperty(target, property);

        for (var index = 0; index < frames.Count; index++)
        {
            var frame = frames[index];
            var offset = Math.Max(0d, Math.Min(1d, frame.Offset));
            var span = Math.Max(0d, offset - previousOffset);
            var segmentDuration = TimeSpan.FromTicks((long)Math.Round(ticks * span));

            segment.GetState().SetValue(animated, frame.Value);
            var segmentEffect = new VeloxTransitionEffect
            {
                Duration = segmentDuration,
                Ease = new EaseBridge(spec.Ease),
                FPS = spec.FPS,
                LoopTime = spec.LoopTime,
                IsAutoReverse = spec.IsAutoReverse,
            };
            segment.Effect(segmentEffect);

            // 链的最后一段最后结束；只在这里挂收尾，避免前面每段的 Finally 把动画层提前清掉。
            if (index == frames.Count - 1)
            {
                animated.AttachCompleter(segmentEffect);
            }

            previousOffset = offset;
            if (index < frames.Count - 1)
            {
                segment = segment.Then<Transition<EaselVisualElement>>();
            }
        }

        Track(animated);
        try
        {
            Transition<EaselVisualElement>.Execute(target, new[] { root }, CanMutualTask: true);
        }
        catch
        {
            animated.Rollback();
            throw;
        }
    }

    /// <inheritdoc />
    public void Stop(EaselVisualElement target)
    {
        var active = TakeActive(target);
        TransitionCore.Exit(target, IncludeMutual: true, IncludeNoMutual: false);
        foreach (var animated in active)
        {
            animated.HoldCurrentValue();
        }
    }

    /// <inheritdoc />
    public bool HasActive(EaselVisualElement target)
    {
        lock (ActiveAnimationsLock)
        {
            return ActiveAnimations.TryGetValue(target, out var map) && map.Count > 0;
        }
    }

    private static VeloxTransitionEffect CreateEffect(AnimationSpec spec) => new()
    {
        Duration = spec.Duration,
        Ease = new EaseBridge(spec.Ease),
        FPS = spec.FPS,
        LoopTime = spec.LoopTime,
        IsAutoReverse = spec.IsAutoReverse,
    };

    private static readonly ConditionalWeakTable<
        EaselVisualElement,
        Dictionary<EaselDependencyProperty, EaselDependencyTransitionProperty>> ActiveAnimations = new();

    private static readonly object ActiveAnimationsLock = new();

    private static void Track(EaselDependencyTransitionProperty animated)
    {
        lock (ActiveAnimationsLock)
        {
            ActiveAnimations.GetOrCreateValue(animated.Target)[animated.Property] = animated;
        }
    }

    // 只有仍登记在册的实例才移除：被新动画取代的旧实例不能清掉新动画正在写的层位。
    internal static bool Untrack(EaselDependencyTransitionProperty animated)
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

    private static List<EaselDependencyTransitionProperty> TakeActive(EaselVisualElement target)
    {
        lock (ActiveAnimationsLock)
        {
            if (!ActiveAnimations.TryGetValue(target, out var map))
            {
                return [];
            }

            var active = new List<EaselDependencyTransitionProperty>(map.Values);
            map.Clear();
            return active;
        }
    }

    private sealed class EaseBridge(Easel.Animation.IEase ease) : IEaseCalculator
    {
        public double Ease(double t) => ease.Ease(t);
    }
}

// 把 (元素, DP) 适配成过渡系统的可动画路径；GetValue/SetValue 直接用持有的元素，忽略调度器传入的目标。
internal sealed class EaselDependencyTransitionProperty : ITransitionProperty
{
    private readonly EaselVisualElement _target;
    private readonly EaselDependencyProperty _property;
    private EventHandler<TransitionEventArgs>? _finallyHandler;
    private VeloxTransitionEffect? _finallyEffect;

    internal EaselDependencyTransitionProperty(EaselVisualElement target, EaselDependencyProperty property)
    {
        _target = target;
        _property = property;
    }

    internal EaselVisualElement Target => _target;

    internal EaselDependencyProperty Property => _property;

    public string Path => $"{_target.GetType().Name}.{_property.Name}";

    public Type PropertyType => _property.PropertyType;

    public bool CanRead => true;

    public bool CanWrite => true;

    public object? GetValue(object? target) => _property.GetValue(_target);

    public bool SetValue(object target, object? value)
    {
        _property.SetValue(_target, value, Easel.BindingPriority.Animation);
        return true;
    }

    // 动画自然结束或被取消时把最末值落到本地层并清掉动画层；否则 Animation 层会永远压住 local/style。
    internal void AttachCompleter(VeloxTransitionEffect effect)
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
        if (EaselVisualAnimator.Untrack(this))
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
        _property.SetValue(_target, value, Easel.BindingPriority.LocalValue);
        _property.ClearValue(_target, Easel.BindingPriority.Animation);
    }

    // 启动失败时撤回登记与回调，不动属性值。
    internal void Rollback()
    {
        EaselVisualAnimator.Untrack(this);
        DetachCompleter();
    }
}

/// <summary>Registers the VeloxDev transition adapter as Easel's animation backend.</summary>
internal static class EaselAnimationBootstrap
{
#pragma warning disable CA2255 // 库通过模块初始化器把适配器注入 Easel；这是该规则描述的高级用法。
    [ModuleInitializer]
    internal static void Initialize() => AnimationRuntime.Animator = new EaselVisualAnimator();
#pragma warning restore CA2255
}
