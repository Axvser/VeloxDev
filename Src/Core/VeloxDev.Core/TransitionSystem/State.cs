using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;

namespace VeloxDev.TransitionSystem.Abstractions;

public class StateCore : IFrameState
{
    protected ConcurrentDictionary<ITransitionProperty, object?> _values = [];
    protected ConcurrentDictionary<ITransitionProperty, IValueInterpolator> _interpolators = [];
    protected ConcurrentDictionary<ITransitionProperty, object?> _options = [];

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
    public virtual ConcurrentDictionary<ITransitionProperty, object?> Options
    {
        get => _options;
        protected set => _options = value;
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

    public virtual void SetOptions<TSource, TValue>(Expression<Func<TSource, TValue>> expression, object? options)
    {
        if (TransitionProperty.TryCreate(expression, out var property)
            && property is not null
            && property.CanRead
            && property.CanWrite)
        {
            SetOptions(property, options);
        }
    }
    public virtual void SetOptions(ITransitionProperty property, object? options)
    {
        if (_options.TryGetValue(property, out _))
        {
            _options[property] = options;
        }
        else
        {
            _options.TryAdd(property, options);
        }
    }
    public virtual void SetOptions(PropertyInfo propertyInfo, object? options)
    {
        SetOptions(TransitionProperty.FromProperty(propertyInfo), options);
    }
    public virtual bool TryGetOptions(ITransitionProperty property, out object? options)
    {
        if (_options.TryGetValue(property, out var item))
        {
            options = item;
            return true;
        }
        options = null;
        return false;
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

        foreach (var kvp in _options)
        {
            value.Options.TryAdd(kvp.Key, kvp.Value);
        }

        return value;
    }
}
