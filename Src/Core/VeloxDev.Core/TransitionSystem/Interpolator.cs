using System.Collections.Concurrent;
using System.Drawing;
using System.Numerics;
using VeloxDev.TransitionSystem.NativeSamplers;

namespace VeloxDev.TransitionSystem.Abstractions;

public abstract class InterpolatorCore
{
    static InterpolatorCore()
    {
        RegisterInterpolator(typeof(double), new DoubleSampler());
        RegisterInterpolator(typeof(float), new FloatSampler());
        RegisterInterpolator(typeof(int), new IntSampler());
        RegisterInterpolator(typeof(long), new LongSampler());
        RegisterInterpolator(typeof(Point), new PointSampler());
        RegisterInterpolator(typeof(PointF), new PointFSampler());
        RegisterInterpolator(typeof(Size), new SizeSampler());
        RegisterInterpolator(typeof(SizeF), new SizeFSampler());
        RegisterInterpolator(typeof(Color), new ColorSampler());
        RegisterInterpolator(typeof(Rectangle), new RectangleSampler());
        RegisterInterpolator(typeof(RectangleF), new RectangleFSampler());
#if !NETSTANDARD2_0
        RegisterInterpolator(typeof(Vector2), new Vector2Sampler());
        RegisterInterpolator(typeof(Vector3), new Vector3Sampler());
        RegisterInterpolator(typeof(Vector4), new Vector4Sampler());
        RegisterInterpolator(typeof(Quaternion), new QuaternionSampler());
#endif
    }

    public static ConcurrentDictionary<Type, ISampler> NativeInterpolators { get; protected set; } = [];

    /// <summary>
    /// Resolves the sampler for a type: the exact type first, then base classes nearest-first, then interfaces.
    /// </summary>
    /// <remarks>
    /// A framework property is often declared as a subclass of what the adapter registered — a
    /// <c>LinearGradientBrush</c> property against WPF's registered <c>Brush</c> — so an exact match would leave it
    /// unanimated and report it unsampleable. Interfaces come last and are ordered by name, because reflection's own
    /// order is not specified. The walk runs once per property per animation, never per frame.
    /// </remarks>
    public static bool TryGetInterpolator(Type type, out ISampler? sampler)
    {
        if (NativeInterpolators.TryGetValue(type, out sampler))
        {
            return true;
        }

        for (var baseType = type.BaseType; baseType is not null; baseType = baseType.BaseType)
        {
            if (NativeInterpolators.TryGetValue(baseType, out sampler))
            {
                return true;
            }
        }

        ISampler? matched = null;
        string? matchedName = null;

        foreach (var contract in type.GetInterfaces())
        {
            if (!NativeInterpolators.TryGetValue(contract, out var candidate) || candidate is null)
            {
                continue;
            }

            var name = contract.FullName ?? contract.Name;
            if (matchedName is not null && string.CompareOrdinal(name, matchedName) >= 0)
            {
                continue;
            }

            matched = candidate;
            matchedName = name;
        }

        sampler = matched;
        return matched is not null;
    }

    public static bool RegisterInterpolator(Type type, ISampler sampler)
    {
        // Atomic last-writer-wins install. AddOrUpdate makes the update unconditional and atomic, so the
        // registration is guaranteed to land.
        NativeInterpolators.AddOrUpdate(type, sampler, (_, _) => sampler);
        return true;
    }
    public static bool UnregisterInterpolator(Type type, out ISampler? sampler)
    {
        return NativeInterpolators.TryRemove(type, out sampler);
    }

    /// <summary>
    /// The host this platform animates <paramref name="target"/> with, for a caller that knows the target only
    /// as an <see cref="object"/>, or null when it cannot carry <paramref name="effect"/>.
    /// </summary>
    /// <remarks>
    /// The theme system runs one switch across targets of many runtime types, so it cannot name the type argument of
    /// <c>Transition&lt;T&gt;</c>. Which host, interpreter and dispatcher priority to build a scheduler from is the
    /// one thing the platform knows and Core does not.
    /// <para>
    /// Null is the honest answer both for "this platform has not opted in" and for "this effect does not belong to
    /// this platform" — the second mirroring the cast the scheduler itself performs before running. The caller then
    /// switches without animating rather than starting a run that draws nothing.
    /// </para>
    /// <para>
    /// An implementation must go through <c>TransitionSchedulerCore&lt;...&gt;.FindOrCreate</c>, not construct a
    /// scheduler directly: only that path files it under the target, which is what makes a later
    /// <c>Transition.Pause</c>, <c>Seek</c> or <c>Exit</c> able to find the animation.
    /// </para>
    /// </remarks>
    public virtual TransitionSchedulerCore? CreateScheduler(object target, ITransitionEffectCore effect) => null;

    /// <summary>
    /// Reads each animated property's current and target values, resolves its <see cref="ISampler"/> (override →
    /// registry), and stores the normalized endpoints in the <see cref="SamplerSet{TPriorityCore}"/>. A struct
    /// <see cref="ISampleable"/> is assembled member by member; reference types are never expanded.
    /// </summary>
    /// <remarks>
    /// Frozen index arguments are resolved here, once, against <paramref name="target"/> — the first moment a target
    /// exists. Everything else stays keyed by the <em>unbound</em> path; only the property handed to the sampler is
    /// bound. A property that cannot be prepared is reported through the effect's <c>Warn</c> and skipped, which is
    /// the behaviour a host opts into by declaring a path that only some targets match.
    /// </remarks>
    public virtual SamplerSet<TPriorityCore> Prepare<TPriorityCore>(
        object target,
        IFrameState state,
        ITransitionEffectCore effect,
        ITransitionHost<TPriorityCore> host)
    {
        var set = new SamplerSet<TPriorityCore>(host);
        var diagnostics = new TransitionDiagnostics(effect, target);
        set.SetDiagnostics(diagnostics);

        foreach (var kvp in state.Values)
        {
            // 冻结档在这一刻把索引钉死；没有冻结实参时 BindTo 返回自身，不产生任何额外对象。
            var bound = kvp.Key is TransitionProperty property ? property.BindTo(target) : kvp.Key;
            var currentValue = host.Run<object?>(target, () => bound.GetValue(target));

            // 路径对当前目标无效（中间对象运行时类型不符）→ 跳过，否则会被当成 null 参与插值而扭曲结果。
            if (ReferenceEquals(currentValue, TransitionProperty.UnreadablePath))
            {
                diagnostics.Warn("Unreadable", $"'{bound.Path}' does not match the target's runtime type.");
                continue;
            }

            var newValue = kvp.Value;
            state.TryGetOptions(kvp.Key, out var options);

            ISampler? sampler = null;
            if (state.TryGetInterpolator(kvp.Key, out var customInterpolator) && customInterpolator != null)
            {
                sampler = customInterpolator;
            }
            else if (TryGetInterpolator(kvp.Key.PropertyType, out var registered) && registered != null)
            {
                sampler = registered;
            }
            else if (kvp.Key.PropertyType.IsValueType && currentValue is ISampleable sampleable)
            {
                // Struct ISampleable → assemble the whole value from its interpolated members (member paths can't
                // be written back through a value type). Null when the struct can't be assembled → skip.
                sampler = StructAssembler.Create(kvp.Key, sampleable, currentValue, newValue);
            }

            if (sampler == null)
            {
                diagnostics.Warn("Unsampled", $"'{bound.Path}' has no sampler for {kvp.Key.PropertyType.Name}.");
                continue;
            }

            var normStart = sampler.NormalizeStart(currentValue, newValue, options);
            var normEnd = sampler.NormalizeEnd(currentValue, newValue, options);
            set.Add(bound, sampler, normStart, normEnd, options);
        }
        return set;
    }
}
