using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;

namespace VeloxDev.Core.Interfaces.TransitionSystem
{
    public interface IFrameState<TStateCore> : IFrameStateCore
    {
        public TStateCore Clone();
    }

    public interface IFrameState : IFrameStateCore
    {
        public IFrameState Clone();
    }

    public interface IFrameStateCore
    {
        public ConcurrentDictionary<PropertyInfo, object?> Values { get; }
        public ConcurrentDictionary<PropertyInfo, IValueInterpolator> Interpolators { get; }
        public void SetInterpolator<TSource, TValue>(Expression<Func<TSource, TValue>> expression, IValueInterpolator interpolator);
        public void SetValue<TSource, TValue>(Expression<Func<TSource, TValue>> expression, TValue? value);
        public bool TryGetInterpolator<TSource, TValue>(Expression<Func<TSource, TValue>> expression, out IValueInterpolator? interpolator);
        public bool TryGetValue<TSource, TValue>(Expression<Func<TSource, TValue>> expression, out TValue? value);
        public void SetInterpolator(PropertyInfo propertyInfo, IValueInterpolator interpolator);
        public void SetValue(PropertyInfo propertyInfo, object? value);
        public bool TryGetInterpolator(PropertyInfo propertyInfo, out IValueInterpolator? interpolator);
        public bool TryGetValue(PropertyInfo propertyInfo, out object? value);
    }
}
