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
        public void SetInterpolator<T>(Expression<Func<T, IValueInterpolator>> expression, IValueInterpolator interpolator);
        public void SetValue<T>(Expression<Func<T, T?>> expression, T? value);
        public bool TryGetInterpolator<T>(Expression<Func<T, IValueInterpolator?>> expression, out IValueInterpolator? interpolator);
        public bool TryGetValue<T>(Expression<Func<T, T?>> expression, out T? value);
        public void SetInterpolator(PropertyInfo propertyInfo, IValueInterpolator interpolator);
        public void SetValue(PropertyInfo propertyInfo, object? value);
        public bool TryGetInterpolator(PropertyInfo propertyInfo, out IValueInterpolator? interpolator);
        public bool TryGetValue(PropertyInfo propertyInfo, out object? value);
        public IFrameState<TOutput, TPriority> DeepCopy();
    }

    public interface IFrameState
    {

    }
}
