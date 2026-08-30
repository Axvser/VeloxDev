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

    public static bool TryGetInterpolator(Type type, out ISampler? sampler)
    {
        if (NativeInterpolators.TryGetValue(type, out sampler))
        {
            return true;
        }
        sampler = null;
        return false;
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
    /// Normalizes each animated property: reads the current value (start) and target value (end), resolves the
    /// <see cref="ISampler"/> (custom override → registry), calls <see cref="ISampler.NormalizeStart"/> /
    /// <see cref="ISampler.NormalizeEnd"/> to produce the endpoint values, and stores the stateless sampler with
    /// the normalized endpoints in the <see cref="SamplerSet"/>. ISampleable member expansion is done at capture
    /// time, not here.
    /// </summary>
    public virtual SamplerSet Prepare(object target, IFrameState state, ITransitionEffectCore effect, IUIThreadInspectorCore inspector)
    {
        var set = new SamplerSet(inspector);
        foreach (var kvp in state.Values)
        {
            var currentValue = inspector.ProtectedGetValue(target, kvp.Key);
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

            if (sampler == null) continue;

            var normStart = sampler.NormalizeStart(currentValue, newValue, options);
            var normEnd = sampler.NormalizeEnd(currentValue, newValue, options);
            set.Add(kvp.Key, sampler, normStart, normEnd, options);
        }
        return set;
    }
}
