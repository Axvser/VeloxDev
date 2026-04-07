using System.Collections.ObjectModel;
using System.Linq.Expressions;
using System.Reflection;
using VeloxDev.Core.Interfaces.TransitionSystem;

namespace VeloxDev.Core.TransitionSystem
{
    public sealed class TransitionProperty : ITransitionProperty, IEquatable<TransitionProperty>
    {
        private readonly ReadOnlyCollection<PropertyInfo> _segments;

        public TransitionProperty(IEnumerable<PropertyInfo> segments)
        {
            var array = segments?.ToArray() ?? [];
            if (array.Length == 0)
            {
                throw new ArgumentException("Property path must contain at least one property.", nameof(segments));
            }

            if (array.Any(static property => property.GetIndexParameters().Length > 0))
            {
                throw new ArgumentException("Indexed properties are not supported.", nameof(segments));
            }

            _segments = Array.AsReadOnly(array);
            PropertyInfo = _segments[_segments.Count - 1];
            PropertyType = PropertyInfo.PropertyType;
            CanRead = _segments.All(static property => property.CanRead);
            CanWrite = PropertyInfo.CanWrite;
            Path = string.Join(".", _segments.Select(static property => property.Name));
        }

        public string Path { get; }
        public Type PropertyType { get; }
        public PropertyInfo PropertyInfo { get; }
        public bool CanRead { get; }
        public bool CanWrite { get; }
        public IReadOnlyList<PropertyInfo> Segments => _segments;

        public object? GetValue(object target)
        {
            if (target is null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            object? current = target;
            foreach (var segment in _segments)
            {
                if (current is null)
                {
                    return null;
                }

                current = segment.GetValue(current);
            }

            return current;
        }

        public bool SetValue(object target, object? value)
        {
            if (target is null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            object? current = target;
            for (int index = 0; index < _segments.Count - 1; index++)
            {
                if (current is null)
                {
                    return false;
                }

                current = _segments[index].GetValue(current);
            }

            if (current is null || !PropertyInfo.CanWrite)
            {
                return false;
            }

            PropertyInfo.SetValue(current, value);
            return true;
        }

        public static TransitionProperty FromProperty(PropertyInfo propertyInfo)
        {
            if (propertyInfo is null)
            {
                throw new ArgumentNullException(nameof(propertyInfo));
            }

            return new TransitionProperty([propertyInfo]);
        }

        public static bool TryCreate(LambdaExpression expression, out TransitionProperty? property)
        {
            property = null;
            if (expression is null)
            {
                return false;
            }

            var current = Unwrap(expression.Body);
            Stack<PropertyInfo> properties = [];
            while (current is MemberExpression memberExpression)
            {
                if (memberExpression.Member is not PropertyInfo propertyInfo || propertyInfo.GetIndexParameters().Length > 0)
                {
                    return false;
                }

                properties.Push(propertyInfo);
                current = Unwrap(memberExpression.Expression);
            }

            if (current is not ParameterExpression || properties.Count == 0)
            {
                return false;
            }

            property = new TransitionProperty(properties);
            return true;
        }

        private static Expression? Unwrap(Expression? expression)
        {
            while (expression is UnaryExpression unaryExpression
                && (unaryExpression.NodeType == ExpressionType.Convert || unaryExpression.NodeType == ExpressionType.ConvertChecked))
            {
                expression = unaryExpression.Operand;
            }

            return expression;
        }

        public bool Equals(TransitionProperty? other)
        {
            if (ReferenceEquals(this, other))
            {
                return true;
            }

            if (other is null || _segments.Count != other._segments.Count)
            {
                return false;
            }

            for (int index = 0; index < _segments.Count; index++)
            {
                if (!Equals(_segments[index], other._segments[index]))
                {
                    return false;
                }
            }

            return true;
        }

        public override bool Equals(object? obj) => obj is TransitionProperty other && Equals(other);

        public override int GetHashCode()
        {
            var hash = new HashCode();
            foreach (var property in _segments)
            {
                hash.Add(property);
            }

            return hash.ToHashCode();
        }

        public override string ToString() => Path;
    }
}
