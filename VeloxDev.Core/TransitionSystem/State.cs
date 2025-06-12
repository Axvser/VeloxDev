using System.Collections.Concurrent;
using VeloxDev.Core.Interfaces.TransitionSystem;

namespace VeloxDev.Core.TransitionSystem
{
    public class StateBase<TOutput, TPriority>() : ICloneable
        where TOutput : IFrameSequence<TPriority>
    {
        public ConcurrentDictionary<string, object?> Values { get; protected set; } = new();
        public ConcurrentDictionary<string, IFrameInterpolator<TOutput, TPriority>> Interpolators { get; protected set; } = new();
        public void SetInterpolator(string propertyName, IFrameInterpolator<TOutput, TPriority> value)
        {
            Interpolators.AddOrUpdate(propertyName, value, (key, old) => value);
        }
        public void SetValue(string propertyName, object? value)
        {
            Values.AddOrUpdate(propertyName, value, (key, old) => value);
        }
        public bool TryGetInterpolator(string propertyName, out IFrameInterpolator<TOutput, TPriority>? interpolator)
        {
            if (Interpolators.TryGetValue(propertyName, out var value))
            {
                interpolator = value;
                return true;
            }
            interpolator = null;
            return false;
        }
        public bool TryGetValue(string propertyName, out object? value)
        {
            if (Interpolators.TryGetValue(propertyName, out var target))
            {
                value = target;
                return true;
            }
            value = null;
            return false;
        }
        public object Clone()
        {
            var newState = new StateBase<TOutput, TPriority>();
            foreach (var kvp in Values)
            {
                newState.Values[kvp.Key] = kvp.Value;
            }
            foreach (var kvp in Interpolators)
            {
                newState.Interpolators[kvp.Key] = kvp.Value;
            }
            return newState;
        }
    }
}
