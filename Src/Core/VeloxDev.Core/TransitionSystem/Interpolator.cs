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

    public static ConcurrentDictionary<Type, ISampleable> NativeInterpolators { get; protected set; } = [];

    public static bool TryGetInterpolator(Type type, out ISampleable? sampleable)
    {
        if (NativeInterpolators.TryGetValue(type, out sampleable))
        {
            return true;
        }
        sampleable = null;
        return false;
    }
    public static bool RegisterInterpolator(Type type, ISampleable sampleable)
    {
        // Atomic last-writer-wins install. AddOrUpdate makes the update unconditional and atomic, so the
        // registration is guaranteed to land.
        NativeInterpolators.AddOrUpdate(type, sampleable, (_, _) => sampleable);
        return true;
    }
    public static bool UnregisterInterpolator(Type type, out ISampleable? sampleable)
    {
        return NativeInterpolators.TryRemove(type, out sampleable);
    }

    /// <summary>
    /// 归一化：读每个属性的当前值（start）与目标值（end），解析 <see cref="ISampleable"/>（自定义 → 注册表 → 值本身
    /// 是 ISampleable），调用 <c>Normalize(start, end, options)</c> 得到无状态采样处理器并存入 <see cref="SamplerSet"/>。
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

            ISampleable? sampleable = null;
            if (state.TryGetInterpolator(kvp.Key, out var customInterpolator) && customInterpolator != null)
            {
                sampleable = customInterpolator;
            }
            else if (TryGetInterpolator(kvp.Key.PropertyType, out var registered) && registered != null)
            {
                sampleable = registered;
            }
            else if (currentValue is ISampleable s1)
            {
                sampleable = s1;
            }
            else if (newValue is ISampleable s2)
            {
                sampleable = s2;
            }

            if (sampleable == null) continue;

            var sampler = sampleable.Normalize(currentValue, newValue, options);
            set.Add(kvp.Key, sampler, currentValue, newValue, options);
        }
        return set;
    }
}
