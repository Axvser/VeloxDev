using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Threading;
using VeloxDev.Core.Interfaces.TransitionSystem;
using VeloxDev.Core.TransitionSystem;

namespace VeloxDev.WPF.TransitionSystem
{
    public class LinearInterpolator : LinearInterpolatorBase<InterpolatorOutput, DispatcherPriority>
    {
        static LinearInterpolator()
        {
            Natives.TryAdd(typeof(double), new Double());
            Natives.TryAdd(typeof(System.Windows.Media.Brush), new Brush());
            Natives.TryAdd(typeof(System.Windows.Thickness), new Thickness());
            Natives.TryAdd(typeof(System.Windows.Point), new Point());
            Natives.TryAdd(typeof(System.Windows.CornerRadius), new CornerRadius());
            Natives.TryAdd(typeof(System.Windows.Media.Transform), new Transform());
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

        public class Double : IValueInterpolator
        {
            public List<object?> Interpolate(object? start, object? end, int steps)
                => LinearInterpolation.DoubleComputing(start, end, steps);
        }

        public class Thickness : IValueInterpolator
        {
            public List<object?> Interpolate(object? start, object? end, int steps)
                => LinearInterpolation.ThicknessComputing(start, end, steps);
        }

        public class CornerRadius : IValueInterpolator
        {
            public List<object?> Interpolate(object? start, object? end, int steps)
                => LinearInterpolation.CornerRadiusComputing(start, end, steps);
        }

        public class Transform : IValueInterpolator
        {
            public List<object?> Interpolate(object? start, object? end, int steps)
                => LinearInterpolation.TransformComputing(start, end, steps);
        }

        public class Brush : IValueInterpolator
        {
            public List<object?> Interpolate(object? start, object? end, int steps)
                => LinearInterpolation.BrushComputing(start, end, steps);
        }

        public class Point : IValueInterpolator
        {
            public List<object?> Interpolate(object? start, object? end, int steps)
                => LinearInterpolation.PointComputing(start, end, steps);
        }
    }
}
