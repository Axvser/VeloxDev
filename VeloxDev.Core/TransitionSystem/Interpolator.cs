using System.Collections.Concurrent;
using VeloxDev.Core.Interfaces.TransitionSystem;

namespace VeloxDev.Core.TransitionSystem
{
    public abstract class InterpolatorCore<
        TOutputCore,
        TPriorityCore> : InterpolatorCore, IFrameInterpolator<TPriorityCore>
        where TOutputCore : IFrameSequence<TPriorityCore>, new()
    {
        public virtual async Task<IFrameSequence<TPriorityCore>> Interpolate(
            object target,
            IFrameState state,
            ITransitionEffect<TPriorityCore> effect,
            bool isUIAccess,
            IUIThreadInspector<TPriorityCore> inspector)
        {
            var output = new TOutputCore();
            var count = (int)(effect.FPS * effect.Duration.TotalSeconds);
            count = count > 0 ? count : 1;
            output.SetCount(count);
            foreach (var kvp in state.Values)
            {
                var currentValue = isUIAccess ? kvp.Key.GetValue(target) : inspector.ProtectedGetValue(isUIAccess, target, kvp.Key);
                var newValue = kvp.Value;
                if (TryGetInterpolator(kvp.Key.PropertyType, out var interpolator))
                {
                    if (state.TryGetInterpolator(kvp.Key, out var item))
                    {
                        if (item != null)
                        {
                            var frames = await Task.Run(() => item.Interpolate(currentValue, newValue, count));
                            output.AddPropertyInterpolations(kvp.Key, frames);
                        }
                    }
                    else
                    {
                        if (interpolator != null)
                        {
                            var frames = await Task.Run(() => interpolator.Interpolate(currentValue, newValue, count));
                            output.AddPropertyInterpolations(kvp.Key, frames);
                        }
                    }
                }
                else
                {
                    if (currentValue is IInterpolable v1)
                    {
                        var frames = await Task.Run(() => v1.Interpolate(currentValue, newValue, count));
                        output.AddPropertyInterpolations(kvp.Key, frames);
                    }
                    else if (newValue is IInterpolable v2)
                    {
                        var frames = await Task.Run(() => v2.Interpolate(currentValue, newValue, count));
                        output.AddPropertyInterpolations(kvp.Key, frames);
                    }
                }
            }
            return output;
        }
    }

    public abstract class InterpolatorCore<TOutputCore> : InterpolatorCore, IFrameInterpolator
        where TOutputCore : IFrameSequence, new()
    {
        public virtual async Task<IFrameSequence> Interpolate(
            object target,
            IFrameState state,
            ITransitionEffectCore effect,
            bool isUIAccess,
            IUIThreadInspector inspector)
        {
            var output = new TOutputCore();
            var count = (int)(effect.FPS * effect.Duration.TotalSeconds);
            count = count > 0 ? count : 1;
            output.SetCount(count);
            foreach (var kvp in state.Values)
            {
                var currentValue = isUIAccess ? kvp.Key.GetValue(target) : inspector.ProtectedGetValue(isUIAccess, target, kvp.Key);
                var newValue = kvp.Value;
                if (TryGetInterpolator(kvp.Key.PropertyType, out var interpolator))
                {
                    if (state.TryGetInterpolator(kvp.Key, out var item))
                    {
                        if (item != null)
                        {
                            var frames = await Task.Run(() => item.Interpolate(currentValue, newValue, count));
                            output.AddPropertyInterpolations(kvp.Key, frames);
                        }
                    }
                    else
                    {
                        if (interpolator != null)
                        {
                            var frames = await Task.Run(() => interpolator.Interpolate(currentValue, newValue, count));
                            output.AddPropertyInterpolations(kvp.Key, frames);
                        }
                    }
                }
                else
                {
                    if (currentValue is IInterpolable v1)
                    {
                        var frames = await Task.Run(() => v1.Interpolate(currentValue, newValue, count));
                        output.AddPropertyInterpolations(kvp.Key, frames);
                    }
                    else if (newValue is IInterpolable v2)
                    {
                        var frames = await Task.Run(() => v2.Interpolate(currentValue, newValue, count));
                        output.AddPropertyInterpolations(kvp.Key, frames);
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
