using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;

namespace VeloxDev.TransitionSystem
{
    /// <summary>The values, samplers and options of one animation frame, keyed by animatable property.</summary>
    public interface IFrameState
    {
        /// <summary>The interpolated value of each property in this frame.</summary>
        public ConcurrentDictionary<ITransitionProperty, object?> Values { get; }

        /// <summary>The sampler registered for each property in this frame.</summary>
        public ConcurrentDictionary<ITransitionProperty, ISampler> Interpolators { get; }

        /// <summary>The per-property options handed to the sampler.</summary>
        public ConcurrentDictionary<ITransitionProperty, object?> Options { get; }

        /// <summary>Registers <paramref name="interpolator"/> for the property named by <paramref name="expression"/>.</summary>
        public void SetInterpolator<TSource, TValue>(Expression<Func<TSource, TValue>> expression, ISampler interpolator);

        /// <summary>Sets the value of the property named by <paramref name="expression"/>.</summary>
        public void SetValue<TSource, TValue>(Expression<Func<TSource, TValue>> expression, TValue? value);

        /// <summary>Tries to read the sampler registered for the property named by <paramref name="expression"/>.</summary>
        public bool TryGetInterpolator<TSource, TValue>(Expression<Func<TSource, TValue>> expression, out ISampler? interpolator);

        /// <summary>Tries to read the value of the property named by <paramref name="expression"/>.</summary>
        public bool TryGetValue<TSource, TValue>(Expression<Func<TSource, TValue>> expression, out TValue? value);

        /// <summary>Registers <paramref name="interpolator"/> for <paramref name="property"/>.</summary>
        public void SetInterpolator(ITransitionProperty property, ISampler interpolator);

        /// <summary>Sets the value of <paramref name="property"/>.</summary>
        public void SetValue(ITransitionProperty property, object? value);

        /// <summary>Tries to read the sampler registered for <paramref name="property"/>.</summary>
        public bool TryGetInterpolator(ITransitionProperty property, out ISampler? interpolator);

        /// <summary>Tries to read the value of <paramref name="property"/>.</summary>
        public bool TryGetValue(ITransitionProperty property, out object? value);

        /// <summary>Registers <paramref name="interpolator"/> for the property named by <paramref name="propertyInfo"/>.</summary>
        public void SetInterpolator(PropertyInfo propertyInfo, ISampler interpolator);

        /// <summary>Sets the value of the property named by <paramref name="propertyInfo"/>.</summary>
        public void SetValue(PropertyInfo propertyInfo, object? value);

        /// <summary>Tries to read the sampler registered for the property named by <paramref name="propertyInfo"/>.</summary>
        public bool TryGetInterpolator(PropertyInfo propertyInfo, out ISampler? interpolator);

        /// <summary>Tries to read the value of the property named by <paramref name="propertyInfo"/>.</summary>
        public bool TryGetValue(PropertyInfo propertyInfo, out object? value);

        /// <summary>Sets the options of the property named by <paramref name="expression"/>.</summary>
        public void SetOptions<TSource, TValue>(Expression<Func<TSource, TValue>> expression, object? options);

        /// <summary>Sets the options of <paramref name="property"/>.</summary>
        public void SetOptions(ITransitionProperty property, object? options);

        /// <summary>Sets the options of the property named by <paramref name="propertyInfo"/>.</summary>
        public void SetOptions(PropertyInfo propertyInfo, object? options);

        /// <summary>Tries to read the options of <paramref name="property"/>.</summary>
        public bool TryGetOptions(ITransitionProperty property, out object? options);

        /// <summary>Returns a copy of this frame state.</summary>
        public IFrameState Clone();
    }
}
