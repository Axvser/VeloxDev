using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;

namespace VeloxDev.TransitionSystem.Abstractions;

public class StateCore : IFrameState
{
    protected ConcurrentDictionary<ITransitionProperty, object?> _values = [];
    protected ConcurrentDictionary<ITransitionProperty, IValueInterpolator> _interpolators = [];

    public virtual ConcurrentDictionary<ITransitionProperty, object?> Values
    {
        get => _values;
        protected set => _values = value;
    }
    public virtual ConcurrentDictionary<ITransitionProperty, IValueInterpolator> Interpolators
    {
        get => _interpolators;
        protected set => _interpolators = value;
    }

    public virtual void SetInterpolator<TSource, TValue>(Expression<Func<TSource, TValue>> expression, IValueInterpolator interpolator)
    {
        if (TransitionProperty.TryCreate(expression, out var property)
            && property is not null
            && property.CanRead
            && property.CanWrite)
        {
            SetInterpolator(property, interpolator);
        }
    }
    public virtual void SetValue<TSource, TValue>(Expression<Func<TSource, TValue>> expression, TValue? value)
    {
        if (TransitionProperty.TryCreate(expression, out var property)
            && property is not null
            && property.CanRead
            && property.CanWrite)
        {
            SetValue(property, value);
        }
    }
    public virtual bool TryGetInterpolator<TSource, TValue>(Expression<Func<TSource, TValue>> expression, out IValueInterpolator? interpolator)
    {
        if (TransitionProperty.TryCreate(expression, out var property)
            && property is not null
            && property.CanRead
            && property.CanWrite
            && _interpolators.TryGetValue(property, out var item))
        {
            interpolator = item;
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
        if (TransitionProperty.TryCreate(expression, out var property)
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

    public virtual void SetInterpolator(ITransitionProperty propertyInfo, IValueInterpolator interpolator)
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
    public virtual void SetValue(ITransitionProperty propertyInfo, object? value)
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
    public virtual bool TryGetInterpolator(ITransitionProperty propertyInfo, out IValueInterpolator? interpolator)
    {
        if (_interpolators.TryGetValue(propertyInfo, out var item))
        {
            interpolator = item;
            return true;
        }

        interpolator = null;
        return false;
    }
    public virtual bool TryGetValue(ITransitionProperty propertyInfo, out object? value)
    {
        if (_values.TryGetValue(propertyInfo, out var item))
        {
            value = item;
            return true;
        }

        value = null;
        return false;
    }
    public virtual void SetInterpolator(PropertyInfo propertyInfo, IValueInterpolator interpolator)
    {
        SetInterpolator(TransitionProperty.FromProperty(propertyInfo), interpolator);
    }
    public virtual void SetValue(PropertyInfo propertyInfo, object? value)
    {
        SetValue(TransitionProperty.FromProperty(propertyInfo), value);
    }
    public virtual bool TryGetInterpolator(PropertyInfo propertyInfo, out IValueInterpolator? interpolator)
    {
        return TryGetInterpolator(TransitionProperty.FromProperty(propertyInfo), out interpolator);
    }
    public virtual bool TryGetValue(PropertyInfo propertyInfo, out object? value)
    {
        return TryGetValue(TransitionProperty.FromProperty(propertyInfo), out value);
    }

    public virtual IFrameState Clone()
    {
        var value = new StateCore();

        foreach (var kvp in _values)
        {
            value.Values.TryAdd(kvp.Key, kvp.Value);
        }

        foreach (var kvp in _interpolators)
        {
            value.Interpolators.TryAdd(kvp.Key, kvp.Value);
        }

        return value;
    }
}
