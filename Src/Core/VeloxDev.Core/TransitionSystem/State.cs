using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;

namespace VeloxDev.TransitionSystem.Abstractions;

/// <summary>The default frame state: the values, samplers and options of one animation frame.</summary>
public class StateCore : IFrameState
{
    /// <summary>The backing store of interpolated values.</summary>
    protected ConcurrentDictionary<ITransitionProperty, object?> _values = [];
    /// <summary>The backing store of registered samplers.</summary>
    protected ConcurrentDictionary<ITransitionProperty, ISampler> _interpolators = [];
    /// <summary>The backing store of per-property options.</summary>
    protected ConcurrentDictionary<ITransitionProperty, object?> _options = [];

    /// <inheritdoc />
    public virtual ConcurrentDictionary<ITransitionProperty, object?> Values
    {
        get => _values;
        protected set => _values = value;
    }
    /// <inheritdoc />
    public virtual ConcurrentDictionary<ITransitionProperty, ISampler> Interpolators
    {
        get => _interpolators;
        protected set => _interpolators = value;
    }
    /// <inheritdoc />
    public virtual ConcurrentDictionary<ITransitionProperty, object?> Options
    {
        get => _options;
        protected set => _options = value;
    }

    /// <inheritdoc />
    public virtual void SetInterpolator<TSource, TValue>(Expression<Func<TSource, TValue>> expression, ISampler interpolator)
    {
        if (TransitionProperty.TryCreate(expression, out var property)
            && property is not null
            && property.CanRead
            && property.CanWrite)
        {
            SetInterpolator(property, interpolator);
        }
    }
    /// <inheritdoc />
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
    /// <inheritdoc />
    public virtual bool TryGetInterpolator<TSource, TValue>(Expression<Func<TSource, TValue>> expression, out ISampler? interpolator)
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
    /// <inheritdoc />
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

    /// <inheritdoc />
    public virtual void SetInterpolator(ITransitionProperty propertyInfo, ISampler interpolator)
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
    /// <inheritdoc />
    public virtual void SetValue(ITransitionProperty propertyInfo, object? value)
    {
        RejectPathConflict(propertyInfo);

        if (_values.TryGetValue(propertyInfo, out _))
        {
            _values[propertyInfo] = value;
        }
        else
        {
            _values.TryAdd(propertyInfo, value);
        }
    }

    /// <summary>
    /// Rejects a path that sits above or below one already on this transition. Re-adding the very same path is
    /// allowed — that is a plain overwrite.
    /// </summary>
    private void RejectPathConflict(ITransitionProperty incoming)
    {
        if (incoming is not TransitionProperty candidate)
        {
            return;
        }

        foreach (var existing in _values.Keys)
        {
            if (existing is TransitionProperty applied
                && (candidate.IsDescendantOf(applied) || applied.IsDescendantOf(candidate)))
            {
                throw new TransitionPathConflictException(applied, incoming);
            }
        }
    }
    /// <inheritdoc />
    public virtual bool TryGetInterpolator(ITransitionProperty propertyInfo, out ISampler? interpolator)
    {
        if (_interpolators.TryGetValue(propertyInfo, out var item))
        {
            interpolator = item;
            return true;
        }

        interpolator = null;
        return false;
    }
    /// <inheritdoc />
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
    /// <inheritdoc />
    public virtual void SetInterpolator(PropertyInfo propertyInfo, ISampler interpolator)
    {
        SetInterpolator(TransitionProperty.FromProperty(propertyInfo), interpolator);
    }
    /// <inheritdoc />
    public virtual void SetValue(PropertyInfo propertyInfo, object? value)
    {
        SetValue(TransitionProperty.FromProperty(propertyInfo), value);
    }
    /// <inheritdoc />
    public virtual bool TryGetInterpolator(PropertyInfo propertyInfo, out ISampler? interpolator)
    {
        return TryGetInterpolator(TransitionProperty.FromProperty(propertyInfo), out interpolator);
    }
    /// <inheritdoc />
    public virtual bool TryGetValue(PropertyInfo propertyInfo, out object? value)
    {
        return TryGetValue(TransitionProperty.FromProperty(propertyInfo), out value);
    }

    /// <inheritdoc />
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
    /// <inheritdoc />
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
    /// <inheritdoc />
    public virtual void SetOptions(PropertyInfo propertyInfo, object? options)
    {
        SetOptions(TransitionProperty.FromProperty(propertyInfo), options);
    }
    /// <inheritdoc />
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

    /// <inheritdoc />
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
