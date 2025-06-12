using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;

namespace VeloxDev.Core.Interfaces.TransitionSystem
{
    public interface IFrameState<TOutput, TPriority> : IFrameState
        where TOutput : IFrameSequence<TPriority>
    {
        public ConcurrentDictionary<PropertyInfo, object?> Values { get; }
        public ConcurrentDictionary<PropertyInfo, IValueInterpolator> Interpolators { get; }
        public void SetInterpolator<T>(Expression<Func<T>> expression, IValueInterpolator interpolator);
        public bool TryGetInterpolator<T>(Expression<Func<T>> expression, out IValueInterpolator? interpolator);
        public void SetValue<T>(Expression<Func<T>> expression, object? value);
        public bool TryGetValue<T>(Expression<Func<T>> expression, out object? value);
        public IFrameState<TOutput, TPriority> DeepCopy();
    }

    public interface IFrameState
    {

    }
}
