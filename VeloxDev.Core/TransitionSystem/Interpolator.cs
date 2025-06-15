using System.Collections.Concurrent;
using System.Reflection;
using VeloxDev.Core.Interfaces.TransitionSystem;

namespace VeloxDev.Core.TransitionSystem
{
    public abstract class InterpolatorCore<
        TOutputCore,
        TPriorityCore> : InterpolatorCore, IFrameInterpolator<TPriorityCore>
        where TOutputCore : IFrameSequence<TPriorityCore>, new()
    {
        public virtual IFrameSequence<TPriorityCore> Interpolate(object target, IFrameState state, ITransitionEffect<TPriorityCore> effect)
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

    public abstract class InterpolatorCore<TOutputCore> : InterpolatorCore, IFrameInterpolator
        where TOutputCore : IFrameSequence, new()
    {
        public virtual IFrameSequence Interpolate(object target, IFrameState state, ITransitionEffectCore effect)
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
