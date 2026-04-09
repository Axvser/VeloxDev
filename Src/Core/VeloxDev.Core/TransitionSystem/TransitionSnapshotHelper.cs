using System.Collections;
using System.Linq.Expressions;
using System.Reflection;
using VeloxDev.Core.Interfaces.TransitionSystem;

namespace VeloxDev.Core.TransitionSystem
{
    public static class TransitionSnapshotHelper
    {
        private const int DefaultMaxDepth = 4;

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
            IEnumerable<Expression<Func<T, object?>>>? extraExpressions = null,
            int maxDepth = DefaultMaxDepth)
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

            var properties = new HashSet<ITransitionProperty>(DiscoverAnimatableProperties(target, canAnimateType, maxDepth));
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
            IEnumerable<Expression<Func<T, object?>>>? excludedExpressions = null,
            int maxDepth = DefaultMaxDepth)
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
            HashSet<ITransitionProperty> properties = new();
            foreach (var property in DiscoverAnimatableProperties(target, canAnimateType, maxDepth))
            {
                if (!IsExcluded(property, excludedProperties))
                {
                    properties.Add(property);
                }
            }

            CaptureProperties(target, state, properties);
        }

        public static IReadOnlyCollection<ITransitionProperty> DiscoverAnimatableProperties(
            object target,
            Func<Type, bool> canAnimateType,
            int maxDepth = DefaultMaxDepth)
        {
            if (target is null)
            {
                throw new ArgumentNullException(nameof(target));
            }
            if (canAnimateType is null)
            {
                throw new ArgumentNullException(nameof(canAnimateType));
            }

            HashSet<ITransitionProperty> result = new();
            DiscoverAnimatablePropertiesCore(target, new List<PropertyInfo>(), result, new HashSet<object>(ReferenceObjectEqualityComparer.Instance), canAnimateType, 0, maxDepth);
            return result.ToArray();
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
            HashSet<ITransitionProperty> properties = new();
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

        private static void DiscoverAnimatablePropertiesCore(
            object current,
            List<PropertyInfo> path,
            HashSet<ITransitionProperty> result,
            HashSet<object> ancestors,
            Func<Type, bool> canAnimateType,
            int depth,
            int maxDepth)
        {
            if (depth > maxDepth || !ancestors.Add(current))
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
                    else if (depth < maxDepth && CanDescendInto(propertyType))
                    {
                        object? nextValue;
                        try
                        {
                            nextValue = property.GetValue(current);
                        }
                        catch
                        {
                            nextValue = null;
                        }

                        if (nextValue is not null)
                        {
                            DiscoverAnimatablePropertiesCore(nextValue, path, result, ancestors, canAnimateType, depth + 1, maxDepth);
                        }
                    }

                    path.RemoveAt(path.Count - 1);
                }
            }
            finally
            {
                ancestors.Remove(current);
            }
        }

        private static bool IsAnimatable(Type propertyType, Func<Type, bool> canAnimateType)
        {
            return canAnimateType(propertyType) || typeof(IInterpolable).IsAssignableFrom(propertyType);
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

        private sealed class ReferenceObjectEqualityComparer : IEqualityComparer<object>
        {
            public static ReferenceObjectEqualityComparer Instance { get; } = new();

            public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);

            public int GetHashCode(object obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
        }
    }
}
