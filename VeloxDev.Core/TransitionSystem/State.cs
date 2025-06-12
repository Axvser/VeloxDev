using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.InteropServices;
using VeloxDev.Core.Interfaces.TransitionSystem;

namespace VeloxDev.Core.TransitionSystem
{
    public class StateBase<TTarget, TOutput, TPriority>() : IFrameState<TTarget, TOutput, TPriority>, ICloneable
        where TOutput : IFrameSequence<TPriority>
    {
        protected ConcurrentDictionary<PropertyInfo, object?> _values = [];
        protected ConcurrentDictionary<PropertyInfo, object> _interpolators = [];

        public virtual ConcurrentDictionary<PropertyInfo, object?> Values
        {
            get => _values;
            protected set => _values = value;
        }
        public virtual ConcurrentDictionary<PropertyInfo, object> Interpolators
        {
            get => _interpolators;
            protected set => _interpolators = value;
        }

        public virtual void SetInterpolator(Expression<Func<TTarget>> expression, IFrameInterpolator<TTarget, TOutput, TPriority> interpolator)
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
        public virtual void SetValue(Expression<Func<TTarget>> expression, object? value)
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
        public virtual bool TryGetInterpolator(Expression<Func<TTarget>> expression, out IFrameInterpolator<TTarget, TOutput, TPriority>? interpolator)
        {
            if (expression.Body is MemberExpression propertyExpr
                && propertyExpr.Member is PropertyInfo property
                && property is not null
                && property.CanRead
                && property.CanWrite
                && _interpolators.TryGetValue(property, out var item))
            {
                interpolator = item as IFrameInterpolator<TTarget, TOutput, TPriority>;
                return true;
            }
            else
            {
                interpolator = null;
                return false;
            }
        }
        public virtual bool TryGetValue(Expression<Func<TTarget>> expression, out object? value)
        {
            if (expression.Body is MemberExpression propertyExpr
                && propertyExpr.Member is PropertyInfo property
                && property is not null
                && property.CanRead
                && property.CanWrite
                && _values.TryGetValue(property, out var item))
            {
                value = item;
                return true;
            }
            else
            {
                value = null;
                return false;
            }
        }

        public virtual IFrameState<TTarget, TOutput, TPriority> DeepCopy()
        {
            var value = new StateBase<TTarget, TOutput, TPriority>();

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
