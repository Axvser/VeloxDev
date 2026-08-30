using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;

namespace VeloxDev.TransitionSystem
{
    public interface IFrameState
    {
        public ConcurrentDictionary<ITransitionProperty, object?> Values { get; }
        public ConcurrentDictionary<ITransitionProperty, ISampler> Interpolators { get; }
        public ConcurrentDictionary<ITransitionProperty, object?> Options { get; }

        public void SetInterpolator<TSource, TValue>(Expression<Func<TSource, TValue>> expression, ISampler interpolator);
        public void SetValue<TSource, TValue>(Expression<Func<TSource, TValue>> expression, TValue? value);
        public bool TryGetInterpolator<TSource, TValue>(Expression<Func<TSource, TValue>> expression, out ISampler? interpolator);
        public bool TryGetValue<TSource, TValue>(Expression<Func<TSource, TValue>> expression, out TValue? value);
        public void SetInterpolator(ITransitionProperty property, ISampler interpolator);
        public void SetValue(ITransitionProperty property, object? value);
        public bool TryGetInterpolator(ITransitionProperty property, out ISampler? interpolator);
        public bool TryGetValue(ITransitionProperty property, out object? value);
        public void SetInterpolator(PropertyInfo propertyInfo, ISampler interpolator);
        public void SetValue(PropertyInfo propertyInfo, object? value);
        public bool TryGetInterpolator(PropertyInfo propertyInfo, out ISampler? interpolator);
        public bool TryGetValue(PropertyInfo propertyInfo, out object? value);

        public void SetOptions<TSource, TValue>(Expression<Func<TSource, TValue>> expression, object? options);
        public void SetOptions(ITransitionProperty property, object? options);
        public void SetOptions(PropertyInfo propertyInfo, object? options);
        public bool TryGetOptions(ITransitionProperty property, out object? options);

        public IFrameState Clone();
    }
}
