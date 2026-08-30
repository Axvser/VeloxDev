using System.Collections;
using System.Linq.Expressions;
using System.Reflection;

namespace VeloxDev.TransitionSystem.Abstractions;

public static class TransitionSnapshotHelper
{
    public static void CaptureSpecific<T>(T target, IFrameState state, IEnumerable<Expression<Func<T, object?>>>? expressions)
        where T : class
    {
        if (target is null)
        {
            throw new ArgumentNullException(nameof(target));
        }
        if (state is null)
        {
            throw new ArgumentNullException(nameof(state));
        }

        CaptureProperties(target, state, GetExplicitProperties(expressions));
    }

    public static void CaptureAll<T>(
        T target,
        IFrameState state,
        Func<Type, bool> canAnimateType,
        IEnumerable<Expression<Func<T, object?>>>? extraExpressions = null)
        where T : class
    {
        if (target is null)
        {
            throw new ArgumentNullException(nameof(target));
        }
        if (state is null)
        {
            throw new ArgumentNullException(nameof(state));
        }
        if (canAnimateType is null)
        {
            throw new ArgumentNullException(nameof(canAnimateType));
        }

        var properties = new HashSet<ITransitionProperty>(DiscoverAnimatableProperties(target, canAnimateType));
        foreach (var property in GetExplicitProperties(extraExpressions))
        {
            properties.Add(property);
        }

        CaptureProperties(target, state, properties);
    }

    public static void CaptureAllExcept<T>(
        T target,
        IFrameState state,
        Func<Type, bool> canAnimateType,
        IEnumerable<Expression<Func<T, object?>>>? excludedExpressions = null)
        where T : class
    {
        if (target is null)
        {
            throw new ArgumentNullException(nameof(target));
        }
        if (state is null)
        {
            throw new ArgumentNullException(nameof(state));
        }
        if (canAnimateType is null)
        {
            throw new ArgumentNullException(nameof(canAnimateType));
        }

        var excludedProperties = GetExplicitProperties(excludedExpressions);
        HashSet<ITransitionProperty> properties = [];
        foreach (var property in DiscoverAnimatableProperties(target, canAnimateType))
        {
            if (!IsExcluded(property, excludedProperties))
            {
                properties.Add(property);
            }
        }

        CaptureProperties(target, state, properties);
    }

    /// <summary>
    /// Discovers animatable properties for snapshot record/restore. Walks the object graph recursively
    /// (cycle-guarded, bounded by <paramref name="maxDepth"/>) so sub-leaf values are captured:
    /// (a) the property type matches the registry or implements <see cref="ISampler"/> → the whole path;
    /// (b) the value implements <see cref="ISampleable"/> → its declared members (one level, not recursive);
    /// (c) otherwise a composite (not ISampleable) → recurse into its sub-leaves.
    /// </summary>
    public static IReadOnlyCollection<ITransitionProperty> DiscoverAnimatableProperties(
        object target,
        Func<Type, bool> canAnimateType)
    {
        if (target is null)
        {
            throw new ArgumentNullException(nameof(target));
        }
        if (canAnimateType is null)
        {
            throw new ArgumentNullException(nameof(canAnimateType));
        }

        HashSet<ITransitionProperty> result = [];
        DiscoverAnimatablePropertiesCore(target, [], result, [], [], canAnimateType);
        return [.. result];
    }

    private static void DiscoverAnimatablePropertiesCore(
        object current,
        List<PropertyInfo> path,
        HashSet<ITransitionProperty> result,
        HashSet<object> ancestors,
        HashSet<Type> ancestorTypes,
        Func<Type, bool> canAnimateType)
    {
        // Object-value cycle (ancestors) + type-cycle guard (ancestorTypes): a reference back into the current path
        // (an object re-visit, or a member whose type is already an ancestor) stops the recursion — no depth limit.
        if (!ancestors.Add(current) || !ancestorTypes.Add(current.GetType()))
        {
            return;
        }

        try
        {
            foreach (var property in current.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!property.CanRead || !property.CanWrite || property.GetIndexParameters().Length != 0)
                {
                    continue;
                }

                path.Add(property);
                var transitionProperty = new TransitionProperty(path);
                var propertyType = property.PropertyType;

                if (IsAnimatable(propertyType, canAnimateType))
                {
                    result.Add(transitionProperty);
                }
                else
                {
                    object? value;
                    try
                    {
                        value = property.GetValue(current);
                    }
                    catch
                    {
                        value = null;
                    }

                    // ISampleable → green light: expand declared members recursively all the way down (nested
                    // ISampleable members and plain-composite sub-leaves), bounded only by the object/type guards.
                    if (value is ISampleable meta)
                    {
                        if (propertyType.IsValueType)
                        {
                            // Struct: animate the whole value — member paths can't be written back through a value
                            // type (the setter would hit a boxed copy). The assembler re-constructs it via its ctor.
                            result.Add(transitionProperty);
                        }
                        else
                        {
                            ExpandISampleable(meta, value, transitionProperty, result, ancestors, ancestorTypes, canAnimateType);
                        }
                    }
                    // Plain composite → recurse into sub-leaves so the snapshot records them.
                    else if (CanDescendInto(propertyType) && value is not null)
                    {
                        DiscoverAnimatablePropertiesCore(value, path, result, ancestors, ancestorTypes, canAnimateType);
                    }
                }

                path.RemoveAt(path.Count - 1);
            }
        }
        finally
        {
            ancestors.Remove(current);
            ancestorTypes.Remove(current.GetType());
        }
    }

    /// <summary>
    /// Green-light <see cref="ISampleable"/> expansion: a declared member that is itself ISampleable expands all the
    /// way down; a plain-composite member descends into its sub-leaves. Bounded only by the object-value cycle guard
    /// and the ancestor-type guard (a member whose type is already on the path stops — self-referencing chains).
    /// </summary>
    private static void ExpandISampleable(
        ISampleable sampleable,
        object value,
        TransitionProperty prefix,
        HashSet<ITransitionProperty> result,
        HashSet<object> ancestors,
        HashSet<Type> ancestorTypes,
        Func<Type, bool> canAnimateType)
    {
        if (!ancestors.Add(value) || !ancestorTypes.Add(value.GetType()))
        {
            return;
        }

        try
        {
            foreach (var member in sampleable.GetAnimatableMembers())
            {
                if (!member.CanRead || !member.CanWrite)
                {
                    continue;
                }

                var combined = TransitionProperty.Combine(prefix, member);
                if (IsAnimatable(combined.PropertyType, canAnimateType))
                {
                    result.Add(combined); // registered / self-sampler → whole leaf
                    continue;
                }

                object? memberValue;
                try
                {
                    memberValue = member.GetValue(value);
                }
                catch
                {
                    continue;
                }

                if (combined.PropertyType.IsValueType)
                {
                    // Struct member → animate as a whole (assembled via ISampleable.CreateFrameValue) when it is
                    // itself ISampleable; otherwise it is not animatable.
                    if (memberValue is ISampleable)
                    {
                        result.Add(combined);
                    }
                    continue;
                }

                if (memberValue is ISampleable nested)
                {
                    ExpandISampleable(nested, memberValue, combined, result, ancestors, ancestorTypes, canAnimateType);
                }
                else if (CanDescendInto(combined.PropertyType) && memberValue is not null)
                {
                    DiscoverAnimatablePropertiesCore(memberValue, [.. combined.Segments], result, ancestors, ancestorTypes, canAnimateType);
                }
            }
        }
        finally
        {
            ancestors.Remove(value);
            ancestorTypes.Remove(value.GetType());
        }
    }

    private static bool CanDescendInto(Type propertyType)
    {
        var actualType = Nullable.GetUnderlyingType(propertyType) ?? propertyType;
        if (actualType == typeof(string)
            || actualType == typeof(object)
            || actualType.IsPrimitive
            || actualType.IsEnum
            || actualType.IsValueType
            || typeof(IEnumerable).IsAssignableFrom(actualType)
            || typeof(Delegate).IsAssignableFrom(actualType))
        {
            return false;
        }

        return true;
    }

    public static bool TryGetPropertyFromExpression<T>(Expression<Func<T, object?>> expression, out ITransitionProperty? property)
        where T : class
    {
        property = null;
        if (expression is null)
        {
            return false;
        }

        if (!TransitionProperty.TryCreate(expression, out var parsed)
            || parsed is null
            || !parsed.CanRead
            || !parsed.CanWrite
            || parsed.PropertyInfo.GetIndexParameters().Length != 0)
        {
            return false;
        }

        property = parsed;
        return true;
    }

    public static void CaptureProperties(object target, IFrameState state, IEnumerable<ITransitionProperty> properties)
    {
        if (target is null)
        {
            throw new ArgumentNullException(nameof(target));
        }
        if (state is null)
        {
            throw new ArgumentNullException(nameof(state));
        }
        if (properties is null)
        {
            throw new ArgumentNullException(nameof(properties));
        }

        foreach (var property in properties)
        {
            if (!property.CanRead || !property.CanWrite)
            {
                continue;
            }

            object? currentValue;
            try
            {
                currentValue = property.GetValue(target);
            }
            catch
            {
                continue;
            }

            state.SetValue(property, currentValue);
        }
    }

    private static HashSet<ITransitionProperty> GetExplicitProperties<T>(IEnumerable<Expression<Func<T, object?>>>? expressions)
        where T : class
    {
        HashSet<ITransitionProperty> properties = [];
        if (expressions is null)
        {
            return properties;
        }

        foreach (var expression in expressions)
        {
            if (TryGetPropertyFromExpression(expression, out var property) && property is not null)
            {
                properties.Add(property);
            }
        }

        return properties;
    }

    private static bool IsAnimatable(Type propertyType, Func<Type, bool> canAnimateType)
    {
        return canAnimateType(propertyType) || typeof(ISampler).IsAssignableFrom(propertyType);
    }

    private static bool IsExcluded(ITransitionProperty property, HashSet<ITransitionProperty> excludedProperties)
    {
        foreach (var excludedProperty in excludedProperties)
        {
            if (HasSameOrChildPath(property, excludedProperty))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasSameOrChildPath(ITransitionProperty property, ITransitionProperty excludedProperty)
    {
        if (property is not TransitionProperty candidate || excludedProperty is not TransitionProperty excluded)
        {
            return Equals(property, excludedProperty);
        }

        if (excluded.Segments.Count > candidate.Segments.Count)
        {
            return false;
        }

        for (int index = 0; index < excluded.Segments.Count; index++)
        {
            if (!Equals(candidate.Segments[index], excluded.Segments[index]))
            {
                return false;
            }
        }

        return true;
    }
}
