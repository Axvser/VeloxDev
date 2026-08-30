using System.Collections.ObjectModel;
using System.Linq.Expressions;
using System.Reflection;

namespace VeloxDev.TransitionSystem.Abstractions;

public sealed class TransitionProperty : ITransitionProperty, IEquatable<TransitionProperty>
{
    private readonly ReadOnlyCollection<PropertyInfo> _segments;
    private Func<object, object?>? _compiledGetter;
    private Func<object, object?, bool>? _compiledSetter;

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

    /// <summary>
    /// When the property path is invalid for the current target (the intermediate object's runtime type does not
    /// match, e.g. RenderTransform is a RotateTransform but TranslateTransform.X is being read),
    /// <see cref="GetValue"/> returns this sentinel. Callers should skip the property rather than interpolate it
    /// as a null value — otherwise the invalid path would be treated as 0/identity, causing distortion (e.g.
    /// rotation/3D transforms incorrectly treated as starting from identity).
    /// </summary>
    public static readonly object UnreadablePath = new();

    public object? GetValue(object target)
    {
        if (target is null)
        {
            throw new ArgumentNullException(nameof(target));
        }

        return (_compiledGetter ??= CompileGetter())(target);
    }

    public bool SetValue(object target, object? value)
    {
        if (target is null)
        {
            throw new ArgumentNullException(nameof(target));
        }

        return (_compiledSetter ??= CompileSetter())(target, value);
    }

    /// <summary>
    /// Compiles the "per-segment reflective GetValue + type/null checks" into a single delegate, eliminating
    /// per-frame per-property reflection overhead (the hot path of <c>SamplerSet</c> /
    /// <c>ProtectedGetValue</c>).
    ///
    /// The semantics distinguish two kinds of "cannot read":
    /// - The intermediate object is null (the value is genuinely null) → returns null, and the interpolator starts
    ///   from identity/default.
    /// - The intermediate object is non-null but the type does not match (the path is invalid for the current
    ///   target) → returns <see cref="UnreadablePath"/>, and the caller skips the property to avoid distorting
    ///   interpolation by treating it as a null value.
    /// </summary>
    private Func<object, object?> CompileGetter()
    {
        if (!CanRead || PropertyInfo.GetMethod is null)
        {
            return static _ => UnreadablePath;
        }

        var targetParam = Expression.Parameter(typeof(object), "target");
        Expression current = targetParam;

        foreach (var segment in _segments)
        {
            var declaringType = segment.DeclaringType!;
            var isNull = Expression.Equal(current, Expression.Constant(null));
            var isCorrectType = Expression.TypeIs(current, declaringType);

            var typed = declaringType.IsValueType
                ? Expression.Convert(current, declaringType)
                : (Expression)Expression.TypeAs(current, declaringType);
            var readBoxed = Expression.Convert(
                Expression.Property(typed, segment),
                typeof(object));

            // Current object is non-null but type does not match → path invalid, return the sentinel (caller skips)
            var invalid = Expression.AndAlso(
                Expression.Not(isNull),
                Expression.Not(isCorrectType));

            current = Expression.Condition(
                invalid,
                Expression.Constant(UnreadablePath),
                Expression.Condition(isNull, Expression.Constant(null), readBoxed));
        }

        return Expression.Lambda<Func<object, object?>>(current, targetParam).Compile();
    }

    /// <summary>
    /// Compiles the "per-segment navigation + final SetValue + type/null checks" into a single delegate.
    /// Preserves the original semantics: returns false when an intermediate object's type does not match or is
    /// null, instead of throwing TargetException.
    /// </summary>
    private Func<object, object?, bool> CompileSetter()
    {
        if (!PropertyInfo.CanWrite || PropertyInfo.SetMethod is null)
        {
            return static (_, _) => false;
        }

        var targetParam = Expression.Parameter(typeof(object), "target");
        var valueParam = Expression.Parameter(typeof(object), "value");
        var currentVar = Expression.Variable(typeof(object), "current");
        var exit = Expression.Label(typeof(bool), "exit");
        var fail = Expression.Return(exit, Expression.Constant(false));

        var statements = new List<Expression>
        {
            Expression.Assign(currentVar, targetParam)
        };

        for (int index = 0; index < _segments.Count - 1; index++)
        {
            var segment = _segments[index];
            var declaringType = segment.DeclaringType!;
            statements.Add(Expression.IfThen(
                Expression.Not(Expression.TypeIs(currentVar, declaringType)),
                fail));
            var typed = declaringType.IsValueType
                ? Expression.Convert(currentVar, declaringType)
                : (Expression)Expression.TypeAs(currentVar, declaringType);
            statements.Add(Expression.Assign(
                currentVar,
                Expression.Convert(Expression.Property(typed, segment), typeof(object))));
        }

        var finalSegment = _segments[_segments.Count - 1];
        var finalType = finalSegment.DeclaringType!;
        statements.Add(Expression.IfThen(
            Expression.Not(Expression.TypeIs(currentVar, finalType)),
            fail));

        var propertyType = finalSegment.PropertyType;
        Expression valueCheck = Expression.TypeIs(valueParam, propertyType);
        if (!propertyType.IsValueType)
        {
            valueCheck = Expression.OrElse(
                valueCheck,
                Expression.Equal(valueParam, Expression.Constant(null)));
        }
        statements.Add(Expression.IfThen(Expression.Not(valueCheck), fail));

        var typedCurrent = finalType.IsValueType
            ? Expression.Convert(currentVar, finalType)
            : (Expression)Expression.TypeAs(currentVar, finalType);
        statements.Add(Expression.Call(
            typedCurrent,
            finalSegment.SetMethod!,
            Expression.Convert(valueParam, propertyType)));
        statements.Add(Expression.Label(exit, Expression.Constant(true)));

        var body = Expression.Block(new[] { currentVar }, statements);
        return Expression.Lambda<Func<object, object?, bool>>(body, targetParam, valueParam).Compile();
    }

    public static TransitionProperty FromProperty(PropertyInfo propertyInfo)
    {
        if (propertyInfo is null)
        {
            throw new ArgumentNullException(nameof(propertyInfo));
        }

        return new TransitionProperty([propertyInfo]);
    }

    /// <summary>
    /// Declares a set of animatable member paths from expressions (for <see cref="ISampleable.GetAnimatableMembers"/>).
    /// Filters out members that are not readable or writable.
    /// </summary>
    public static IReadOnlyList<ITransitionProperty> Members<TSource>(params Expression<Func<TSource, object?>>[] expressions)
    {
        List<ITransitionProperty> members = [];
        foreach (var expression in expressions)
        {
            if (TryCreate(expression, out var property)
                && property is not null
                && property.CanRead
                && property.CanWrite)
            {
                members.Add(property);
            }
        }
        return members;
    }

    /// <summary>
    /// Combines a prefix path with a suffix path: prefix = target.Foo, suffix = Foo.Bar → target.Foo.Bar.
    /// </summary>
    public static TransitionProperty Combine(ITransitionProperty prefix, ITransitionProperty suffix)
    {
        if (prefix is null)
        {
            throw new ArgumentNullException(nameof(prefix));
        }
        if (suffix is null)
        {
            throw new ArgumentNullException(nameof(suffix));
        }

        return new TransitionProperty([.. prefix.Segments, .. suffix.Segments]);
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
