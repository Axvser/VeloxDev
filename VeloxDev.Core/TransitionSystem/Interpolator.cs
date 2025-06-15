using System.Collections.Concurrent;
using VeloxDev.Core.Interfaces.TransitionSystem;

namespace VeloxDev.Core.TransitionSystem
{
    /// <summary>
    /// <para>---</para>
    /// ✨ ⌈ 核心 ⌋ 插值器
    /// <para>解释 : </para>
    /// <para>在不同平台实现过渡系统时，您需要具体地实现此核心以构建用于计算过渡过程的插值器</para>
    /// </summary>
    /// <typeparam name="TTransitionEffectCore">您在具体框架对ITransitionEffect的实现类</typeparam>
    /// <typeparam name="TOutputCore">帧计算完成后，需要一个统一的结构用于存储结果并按索引更新帧</typeparam>
    /// <typeparam name="TPriorityCore">在不同框架中，使用不同的结构来表示UI更新操作的优先级</typeparam>
    /// <typeparam name="TStateCore">您在具体框架对StateCore的实现类</typeparam>
    public abstract class InterpolatorCore<
        TOutputCore,
        TTransitionEffectCore,
        TStateCore,
        TPriorityCore> : InterpolatorCore, IFrameInterpolator<
            TTransitionEffectCore,
            TStateCore,
            TOutputCore,
            TPriorityCore>
        where TStateCore : IFrameState<TStateCore>
        where TTransitionEffectCore : ITransitionEffect<TTransitionEffectCore>
        where TOutputCore : IFrameSequence<TPriorityCore>, new()
    {
        public virtual TOutputCore Interpolate(object target, IFrameState<TStateCore> state, ITransitionEffect<TTransitionEffectCore, TPriorityCore> effect)
        {
            var output = new TOutputCore();
            var count = (int)(effect.FPS * effect.Duration.TotalSeconds);
            count = count > 0 ? count : 1;
            output.SetCount(count);
            foreach (var kvp in state.Values)
            {
                var currentValue = kvp.Key.GetValue(target);
                var newValue = kvp.Value;
                if (TryGetInterpolator(kvp.Key.PropertyType, out var interpolator))
                {
                    if (state.TryGetInterpolator(kvp.Key, out var item))
                    {
                        if (item != null)
                        {
                            output.AddPropertyInterpolations(kvp.Key, item.Interpolate(currentValue, newValue, count));
                        }
                    }
                    else
                    {
                        if (interpolator != null)
                        {
                            output.AddPropertyInterpolations(kvp.Key, interpolator.Interpolate(currentValue, newValue, count));
                        }
                    }
                }
                else
                {
                    if (currentValue is IInterpolable v1)
                    {
                        output.AddPropertyInterpolations(kvp.Key, v1.Interpolate(currentValue, newValue, count));
                    }
                    else if (newValue is IInterpolable v2)
                    {
                        output.AddPropertyInterpolations(kvp.Key, v2.Interpolate(currentValue, newValue, count));
                    }
                }
            }
            return output;
        }
    }

    /// <summary>
    /// <para>---</para>
    /// ✨ ⌈ 核心 ⌋ 插值器
    /// <para>解释 : </para>
    /// <para>在不同平台实现过渡系统时，您需要具体地实现此核心以构建用于计算过渡过程的插值器</para>
    /// </summary>
    /// <typeparam name="TTransitionEffectCore">您在具体框架对ITransitionEffect的实现类</typeparam>
    /// <typeparam name="TOutputCore">帧计算完成后，需要一个统一的结构用于存储结果并按索引更新帧</typeparam>
    /// <typeparam name="TStateCore">您在具体框架对StateCore的实现类</typeparam>
    public abstract class InterpolatorCore<
        TTransitionEffectCore,
        TStateCore,
        TOutputCore> : InterpolatorCore, IFrameInterpolator<
            TTransitionEffectCore,
            TStateCore,
            TOutputCore>
        where TStateCore : IFrameState<TStateCore>
        where TTransitionEffectCore : ITransitionEffect<TTransitionEffectCore>
        where TOutputCore : IFrameSequence, new()
    {
        public virtual TOutputCore Interpolate(object target, IFrameState<TStateCore> state, ITransitionEffect<TTransitionEffectCore> effect)
        {
            var output = new TOutputCore();
            var count = (int)(effect.FPS * effect.Duration.TotalSeconds);
            count = count > 0 ? count : 1;
            output.SetCount(count);
            foreach (var kvp in state.Values)
            {
                var currentValue = kvp.Key.GetValue(target);
                var newValue = kvp.Value;
                if (TryGetInterpolator(kvp.Key.PropertyType, out var interpolator))
                {
                    if (state.TryGetInterpolator(kvp.Key, out var item))
                    {
                        if (item != null)
                        {
                            output.AddPropertyInterpolations(kvp.Key, item.Interpolate(currentValue, newValue, count));
                        }
                    }
                    else
                    {
                        if (interpolator != null)
                        {
                            output.AddPropertyInterpolations(kvp.Key, interpolator.Interpolate(currentValue, newValue, count));
                        }
                    }
                }
                else
                {
                    if (currentValue is IInterpolable v1)
                    {
                        output.AddPropertyInterpolations(kvp.Key, v1.Interpolate(currentValue, newValue, count));
                    }
                    else if (newValue is IInterpolable v2)
                    {
                        output.AddPropertyInterpolations(kvp.Key, v2.Interpolate(currentValue, newValue, count));
                    }
                }
            }
            return output;
        }
    }

    /// <summary>
    /// <para>---</para>
    /// ✨ ⌈ 核心 ⌋ 插值器
    /// <para>解释 : </para>
    /// <para>在不同平台实现过渡系统时，此核心的多个泛型子类可帮助你构建具体的插值器</para>
    /// </summary>
    public abstract class InterpolatorCore : IFrameInterpolatorCore
    {
        public static ConcurrentDictionary<Type, IValueInterpolator> NativeInterpolators { get; protected set; } = [];
        public static bool TryGetInterpolator(Type type, out IValueInterpolator? interpolator)
        {
            if (NativeInterpolators.TryGetValue(type, out interpolator))
            {
                return true;
            }
            interpolator = null;
            return false;
        }
        public static bool RegisterInterpolator(Type type, IValueInterpolator interpolator)
        {
            if (NativeInterpolators.TryGetValue(type, out var oldValue))
            {
                return NativeInterpolators.TryUpdate(type, interpolator, oldValue);
            }
            else
            {
                return NativeInterpolators.TryAdd(type, interpolator);
            }
        }
        public static bool RemoveInterpolator(Type type, out IValueInterpolator? interpolator)
        {
            return NativeInterpolators.TryRemove(type, out interpolator);
        }
    }
}
