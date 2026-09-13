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
    /// Reads each animated property's current and target values, resolves its <see cref="ISampler"/> (override →
    /// registry), and stores the normalized endpoints in the <see cref="SamplerSet{TPriorityCore}"/>. A struct
    /// <see cref="ISampleable"/> is assembled member by member; reference types are never expanded.
    /// </summary>
    /// <remarks>
    /// Frozen index arguments are resolved here, once, against <paramref name="target"/> — the first moment a target
    /// exists. Everything else stays keyed by the <em>unbound</em> path; only the property handed to the sampler is
    /// bound. An override that does not call the base loses the freeze silently.
    /// </remarks>
    public virtual SamplerSet<TPriorityCore> Prepare<TPriorityCore>(object target, IFrameState state, ITransitionEffectCore effect, IUIThreadInspector<TPriorityCore> inspector)
    {
        var set = new SamplerSet<TPriorityCore>(inspector);
        foreach (var kvp in state.Values)
        {
            // 冻结档在这一刻把索引钉死；没有冻结实参时 BindTo 返回自身，不产生任何额外对象。
            var bound = kvp.Key is TransitionProperty property ? property.BindTo(target) : kvp.Key;
            var currentValue = inspector.ProtectedGetValue(target, bound);
            // The path is invalid for the current target (intermediate type mismatch) → skip this property to avoid distorting interpolation by treating it as a null value.
            if (ReferenceEquals(currentValue, TransitionProperty.UnreadablePath)) continue;
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

            if (sampler == null) continue;

            var normStart = sampler.NormalizeStart(currentValue, newValue, options);
            var normEnd = sampler.NormalizeEnd(currentValue, newValue, options);
            set.Add(bound, sampler, normStart, normEnd, options);
        }
        return set;
    }
}
