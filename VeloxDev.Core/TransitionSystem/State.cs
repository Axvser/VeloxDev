using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using VeloxDev.Core.Interfaces.TransitionSystem;

namespace VeloxDev.Core.TransitionSystem
{
    public class StateBase<TOutput, TPriority>() : IFrameState<TOutput, TPriority>, ICloneable
        where TOutput : IFrameSequence<TPriority>
    {
        protected ConcurrentDictionary<PropertyInfo, object?> _values = [];
        protected ConcurrentDictionary<PropertyInfo, IValueInterpolator> _interpolators = [];

        public virtual ConcurrentDictionary<PropertyInfo, object?> Values
        {
            get => _values;
            protected set => _values = value;
        }
        public virtual ConcurrentDictionary<PropertyInfo, IValueInterpolator> Interpolators
        {
            get => _interpolators;
            protected set => _interpolators = value;
        }

        public virtual void SetInterpolator<TSource, TValue>(Expression<Func<TSource, TValue>> expression, IValueInterpolator interpolator)
        {
            if (expression.Body is MemberExpression propertyExpr)
            {
                var property = propertyExpr.Member as PropertyInfo;
                if (property == null || !property.CanRead || !property.CanWrite)
                {
                    return;
                }
                else
                {
                    if (_interpolators.TryGetValue(property, out _))
                    {
                        _interpolators[property] = interpolator;
                    }
                    else
                    {
                        _interpolators.TryAdd(property, interpolator);
                    }
                }
            }
        }
        public virtual void SetValue<TSource, TValue>(Expression<Func<TSource, TValue>> expression, TValue? value)
        {
            if (expression.Body is MemberExpression propertyExpr)
            {
                var property = propertyExpr.Member as PropertyInfo;
                if (property == null || !property.CanRead || !property.CanWrite)
                {
                    return;
                }
                else
                {
                    if (_values.TryGetValue(property, out _))
                    {
                        _values[property] = value;
                    }
                    else
                    {
                        _values.TryAdd(property, value);
                    }
                }
            }
        }
        public virtual bool TryGetInterpolator<TSource, TValue>(Expression<Func<TSource, TValue>> expression, out IValueInterpolator? interpolator)
        {
            if (expression.Body is MemberExpression propertyExpr
                && propertyExpr.Member is PropertyInfo property
                && property is not null
                && property.CanRead
                && property.CanWrite
                && _interpolators.TryGetValue(property, out var item))
            {
                interpolator = item as IValueInterpolator;
                return true;
            }
            else
            {
                interpolator = null;
                return false;
            }
        }
        public virtual bool TryGetValue<TSource, TValue>(Expression<Func<TSource, TValue>> expression, out TValue? value)
        {
            if (expression.Body is MemberExpression propertyExpr
                && propertyExpr.Member is PropertyInfo property
                && property is not null
                && property.CanRead
                && property.CanWrite
                && _values.TryGetValue(property, out var item))
            {
                value = (TValue?)item;
                return true;
            }
            else
            {
                value = default;
                return false;
            }
        }

        public virtual void SetInterpolator(PropertyInfo propertyInfo, IValueInterpolator interpolator)
        {
            if (_interpolators.TryGetValue(propertyInfo, out _))
            {
                _interpolators[propertyInfo] = interpolator;
            }
            else
            {
                _interpolators.TryAdd(propertyInfo, interpolator);
            }
        }
        public virtual void SetValue(PropertyInfo propertyInfo, object? value)
        {
            if (_values.TryGetValue(propertyInfo, out _))
            {
                _values[propertyInfo] = value;
            }
            else
            {
                _values.TryAdd(propertyInfo, value);
            }
        }
        public virtual bool TryGetInterpolator(PropertyInfo propertyInfo, out IValueInterpolator? interpolator)
        {
            if (_interpolators.TryGetValue(propertyInfo, out var item))
            {
                interpolator = item;
                return true;
            }

            interpolator = null;
            return false;
        }
        public virtual bool TryGetValue(PropertyInfo propertyInfo, out object? value)
        {
            if (_values.TryGetValue(propertyInfo, out var item))
            {
                value = item;
                return true;
            }

            value = null;
            return false;
        }

        public virtual IFrameState<TOutput, TPriority> DeepCopy()
        {
            var value = new StateBase<TOutput, TPriority>();

            foreach (var kvp in _values)
            {
                value._values.TryAdd(kvp.Key, kvp.Value);
            }

            foreach (var kvp in _interpolators)
            {
                value._interpolators.TryAdd(kvp.Key, kvp.Value);
            }

            return value;
        }
        public virtual object Clone()
        {
            return DeepCopy();
        }
    }
}
