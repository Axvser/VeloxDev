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
    /// 属性路径对当前目标无效（中间对象的运行时类型不匹配，例如 RenderTransform 是
    /// RotateTransform 却要读 TranslateTransform.X）时，<see cref="GetValue"/> 返回此哨兵。
    /// 调用方应跳过该属性，而不是把它当作 null 值去插值——否则会把无效路径当作 0/恒等，
    /// 造成失真（例如旋转/3D 变换被错误地当作从恒等开始）。
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
    /// 把"逐段反射 GetValue + 类型/空检查"编译成单个委托，消除每帧每属性的反射开销
    /// （<c>InterpolatorOutputBase.SetValues</c> / <c>ProtectedGetValue</c> 的热路径）。
    ///
    /// 语义区分两种"读不到"：
    /// - 中间对象为 null（值本来就为 null）→ 返回 null，插值器从恒等/默认开始。
    /// - 中间对象非空但类型不匹配（路径对当前目标无效）→ 返回 <see cref="UnreadablePath"/>，
    ///   调用方跳过该属性，避免把它当作 null 值插值造成失真。
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

            // 当前对象非空但类型不匹配 → 路径无效，返回哨兵（调用方跳过）
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
    /// 把"逐段导航 + 最终 SetValue + 类型/空检查"编译成单个委托。
    /// 保留原语义：中间对象类型不匹配或为 null 时返回 false，而非抛 TargetException。
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
