using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;

namespace VeloxDev.TransitionSystem
{
    public interface IFrameState
    {
        public ConcurrentDictionary<ITransitionProperty, object?> Values { get; }
        public ConcurrentDictionary<ITransitionProperty, IValueInterpolator> Interpolators { get; }

        public void SetInterpolator<TSource, TValue>(Expression<Func<TSource, TValue>> expression, IValueInterpolator interpolator);
        public void SetValue<TSource, TValue>(Expression<Func<TSource, TValue>> expression, TValue? value);
        public bool TryGetInterpolator<TSource, TValue>(Expression<Func<TSource, TValue>> expression, out IValueInterpolator? interpolator);
        public bool TryGetValue<TSource, TValue>(Expression<Func<TSource, TValue>> expression, out TValue? value);
        public void SetInterpolator(ITransitionProperty property, IValueInterpolator interpolator);
        public void SetValue(ITransitionProperty property, object? value);
        public bool TryGetInterpolator(ITransitionProperty property, out IValueInterpolator? interpolator);
        public bool TryGetValue(ITransitionProperty property, out object? value);
        public void SetInterpolator(PropertyInfo propertyInfo, IValueInterpolator interpolator);
        public void SetValue(PropertyInfo propertyInfo, object? value);
        public bool TryGetInterpolator(PropertyInfo propertyInfo, out IValueInterpolator? interpolator);
        public bool TryGetValue(PropertyInfo propertyInfo, out object? value);

        public IFrameState Clone();
    }
}
