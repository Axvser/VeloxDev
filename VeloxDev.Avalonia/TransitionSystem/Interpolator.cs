using Avalonia.Threading;
using System;
using System.Collections.Concurrent;
using VeloxDev.Core.Interfaces.TransitionSystem;
using VeloxDev.Core.TransitionSystem;

namespace VeloxDev.Avalonia.TransitionSystem
{
    public class Interpolator : InterpolatorCore<InterpolatorOutput, DispatcherPriority>
    {
        static Interpolator()
        {
            
        }
        public static ConcurrentDictionary<Type, IValueInterpolator> Natives { get; protected set; } = [];
        public static bool TryGetInterpolator(Type type, out IValueInterpolator? interpolator)
        {
            if (Natives.TryGetValue(type, out interpolator))
            {
                return true;
            }
            interpolator = null;
            return false;
        }
        public static bool RegisterInterpolator(Type type, IValueInterpolator interpolator)
        {
            if (Natives.TryGetValue(type, out var oldValue))
            {
                return Natives.TryUpdate(type, interpolator, oldValue);
            }
            else
            {
                return Natives.TryAdd(type, interpolator);
            }
        }
        public static bool RemoveInterpolator(Type type, out IValueInterpolator? interpolator)
        {
            return Natives.TryRemove(type, out interpolator);
        }

        public override InterpolatorOutput Interpolate(object target, IFrameState<InterpolatorOutput, DispatcherPriority> state, ITransitionEffect<DispatcherPriority> effect)
        {
            var output = new InterpolatorOutput();
            var count = (int)(effect.FPS * effect.Duration.TotalSeconds);
            count = count > 0 ? count : 1;
            output.SetCount(count);
            foreach (var kvp in state.Values)
            {
                if (TryGetInterpolator(kvp.Key.PropertyType, out var interpolator))
                {
                    var currentValue = kvp.Key.GetValue(target);
                    var newValue = kvp.Value;
                    if (state.TryGetInterpolator(kvp.Key, out var item))
                    {
                        if (item != null)
                        {
                            output.AddFrameFrameSequence(kvp.Key, item.Interpolate(currentValue, newValue, count));
                        }
                    }
                    else
                    {
                        if (interpolator != null)
                        {
                            output.AddFrameFrameSequence(kvp.Key, interpolator.Interpolate(currentValue, newValue, count));
                        }
                    }
                }
            }
            return output;
        }
    }
}
