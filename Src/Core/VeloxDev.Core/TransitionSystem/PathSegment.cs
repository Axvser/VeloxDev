using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace VeloxDev.TransitionSystem.Abstractions;

/// <summary>
/// 路径的一段。属性段与索引段共用它做身份（<see cref="SameAs"/> / <see cref="Hash"/>）与文本渲染，
/// 编译访问器则在 <c>TransitionProperty</c> 里按段落类型分派。
/// </summary>
internal abstract class PathSegment
{
    /// <summary>该段从哪个类型上取值。</summary>
    internal abstract Type DeclaringType { get; }

    /// <summary>该段产出的值类型。</summary>
    internal abstract Type ValueType { get; }

    /// <summary>路径文本里的这一段，仅用于诊断。</summary>
    internal abstract string Display { get; }

    /// <summary>
    /// 按值比较。必须与 <see cref="Hash"/> 严格同步——两侧不同步会让 <c>HashSet</c>/字典把同一条路径留成两份，
    /// 排除与包含判定就会静默失配。
    /// </summary>
    internal abstract bool SameAs(PathSegment other);

    internal abstract void Hash(ref HashCode hash);

    /// <summary>
    /// 按名字 + 声明类型比较两个 <see cref="PropertyInfo"/>：反射给出的实例与表达式树携带的实例不是同一个对象，
    /// 而 <c>PropertyInfo.Equals</c> 是引用比较。
    /// </summary>
    internal static bool SameMember(PropertyInfo left, PropertyInfo right)
        => ReferenceEquals(left, right)
           || (left.Name == right.Name && left.DeclaringType == right.DeclaringType);

    /// <summary>
    /// 哈希 <paramref name="property"/> 的名字与声明类型，<b>不</b>哈希实例本身：net5.0/netcoreapp3.0 上
    /// <c>RuntimePropertyInfo</c> 覆盖了 <c>GetHashCode</c> 看起来是按值的，但 netframework4.6.1 上是引用比较——
    /// 那样同一条路径会既相等又落在不同的桶里。
    /// </summary>
    internal static void HashMember(ref HashCode hash, PropertyInfo property)
    {
        hash.Add(property.Name);
        hash.Add(property.DeclaringType);
    }
}

/// <summary>普通属性段。身份语义与索引器支持之前完全一致：名字 + 声明类型。</summary>
internal sealed class PropertySegment : PathSegment
{
    internal PropertySegment(PropertyInfo property)
    {
        Property = property;
        DeclaringType = property.DeclaringType ?? throw new ArgumentException("A property segment needs a declaring type.", nameof(property));
        ValueType = property.PropertyType;
    }

    internal PropertyInfo Property { get; }

    internal override Type DeclaringType { get; }
    internal override Type ValueType { get; }
    internal override string Display => Property.Name;

    internal override bool SameAs(PathSegment other)
        => other is PropertySegment segment && SameMember(Property, segment.Property);

    internal override void Hash(ref HashCode hash) => HashMember(ref hash, Property);
}

/// <summary>索引器段：<c>x.Items[0]</c>、<c>x.Map["a"]</c>、<c>x.Grid[3, 7]</c>。</summary>
internal sealed class IndexerSegment : PathSegment
{
    internal IndexerSegment(PropertyInfo indexer, Type declaringType, IndexArgument[] arguments)
    {
        Indexer = indexer;
        DeclaringType = declaringType;
        Arguments = arguments;
        ValueType = indexer.PropertyType;
        ParameterTypes = indexer.GetIndexParameters().Select(static parameter => parameter.ParameterType).ToArray();
    }

    internal PropertyInfo Indexer { get; }
    internal Type[] ParameterTypes { get; }
    internal IndexArgument[] Arguments { get; }

    internal override Type DeclaringType { get; }
    internal override Type ValueType { get; }
    internal override string Display => $"[{string.Join(", ", Arguments.Select(static argument => argument.Display))}]";

    internal override bool SameAs(PathSegment other)
        => other is IndexerSegment segment
           && SameMember(Indexer, segment.Indexer)
           && IndexArgument.SameAll(Arguments, segment.Arguments);

    internal override void Hash(ref HashCode hash)
    {
        HashMember(ref hash, Indexer);
        IndexArgument.HashAll(ref hash, Arguments);
    }
}

/// <summary>
/// 数组元素段。数组没有 <see cref="PropertyInfo"/>，所以它单独成型；一维及以上共用（多维走 <c>IndexExpression</c>）。
/// </summary>
internal sealed class ArrayIndexSegment : PathSegment
{
    internal ArrayIndexSegment(Type declaringType, Type elementType, IndexArgument[] arguments)
    {
        DeclaringType = declaringType;
        ValueType = elementType;
        Arguments = arguments;
    }

    internal IndexArgument[] Arguments { get; }

    internal override Type DeclaringType { get; }
    internal override Type ValueType { get; }
    internal override string Display => $"[{string.Join(", ", Arguments.Select(static argument => argument.Display))}]";

    internal override bool SameAs(PathSegment other)
        => other is ArrayIndexSegment segment
           && DeclaringType == segment.DeclaringType
           && IndexArgument.SameAll(Arguments, segment.Arguments);

    internal override void Hash(ref HashCode hash)
    {
        hash.Add(DeclaringType);
        IndexArgument.HashAll(ref hash, Arguments);
    }
}

/// <summary>
/// 索引实参。三种身份规则，全部与「求值」解耦——键必须在构造期定死，因为属性是
/// <c>ConcurrentDictionary</c> 的键，而同一个用户 lambda 会在 <c>SetValue</c> / <c>SetOptions</c> /
/// <c>TryGetValue</c> 里被<b>分别解析多次</b>：键一旦依赖当时的值，闭包字段被改写后两次解析就对不上，
/// 表现为静默 miss。
/// </summary>
internal abstract class IndexArgument
{
    /// <summary>文本形式，仅用于诊断路径。</summary>
    internal abstract string Display { get; }

    /// <summary>是否需要把实参绑定到一个具体 target。只有冻结档才需要。</summary>
    internal abstract bool NeedsBinding { get; }

    internal abstract bool SameAs(IndexArgument other);

    internal abstract void Hash(ref HashCode hash);

    /// <summary>求值。<paramref name="target"/> 只在实参引用了 lambda 参数时才会被读。</summary>
    internal abstract object? Resolve(object? target);

    /// <summary>
    /// 把实参编译进访问器表达式。跟随档在这里<b>内联</b>原始表达式（每帧现场求值，没有委托调用开销），
    /// 冻结档则从 <paramref name="arguments"/> 数组里取第 <paramref name="slot"/> 个（由 <c>BindTo</c> 在启动时填好）。
    /// </summary>
    internal abstract Expression Build(Expression target, Expression arguments, ref int slot, Type parameterType);

    internal static bool SameAll(IndexArgument[] left, IndexArgument[] right)
    {
        if (left.Length != right.Length) return false;
        for (var index = 0; index < left.Length; index++)
        {
            if (!left[index].SameAs(right[index])) return false;
        }
        return true;
    }

    internal static void HashAll(ref HashCode hash, IndexArgument[] arguments)
    {
        hash.Add(arguments.Length);
        for (var index = 0; index < arguments.Length; index++)
        {
            arguments[index].Hash(ref hash);
        }
    }
}

/// <summary>
/// 编译期常量实参（<c>[0]</c>、<c>["a"]</c>、<c>[MyEnum.Value]</c>）。按「值 + 运行时类型」做身份：
/// <c>[0]</c> 与 <c>[0L]</c> 必须是两条路径，所以类型要参与比较，不能只比数值。
/// </summary>
internal sealed class ConstantIndexArgument : IndexArgument
{
    private readonly int _hash;

    internal ConstantIndexArgument(Type type, object? value)
    {
        Type = type;
        Value = value;

        // 哈希在构造期算一次并冻结：实参可能是可变的自定义键类型，键待在字典里而哈希变了会让字典直接失效。
        var hash = new HashCode();
        hash.Add(type);
        hash.Add(value);
        _hash = hash.ToHashCode();
    }

    internal Type Type { get; }
    internal object? Value { get; }

    internal override string Display => Value switch
    {
        null => "null",
        string text => $"\"{text}\"",
        _ => Convert.ToString(Value, CultureInfo.InvariantCulture) ?? "?"
    };
    internal override bool NeedsBinding => false;

    internal override bool SameAs(IndexArgument other)
        => other is ConstantIndexArgument argument
           && Type == argument.Type
           && Equals(Value, argument.Value);

    internal override void Hash(ref HashCode hash) => hash.Add(_hash);

    internal override object? Resolve(object? target) => Value;

    // 常量直接落成 Constant 节点；Convert 负责把 int 实参喂给 long 形参这类拓宽，且自身是零成本节点。
    internal override Expression Build(Expression target, Expression arguments, ref int slot, Type parameterType)
        => Expression.Convert(Expression.Constant(Value), parameterType);
}

/// <summary>
/// 需要解析的实参：闭包成员（<c>[i]</c>）与引用 lambda 参数的表达式（<c>[x.SelectedIndex]</c>）。
/// 身份是构造期定死的字符串——闭包按 (实例, 成员) 而不是按值（循环里批量声明时每次都是不同的闭包实例，
/// 按值会让它们挤成一个键），参数引用按规范化结构串（参数名要归一，否则 <c>x.K</c> 与 <c>s.K</c> 不相等，
/// 同一条路径会静默分裂成两个字典条目）。
/// </summary>
internal sealed class DeferredIndexArgument : IndexArgument
{
    private readonly string _key;
    private readonly int _hash;
    private readonly Func<object?, object?> _resolver;
    private readonly Expression _expression;
    private readonly ParameterExpression? _parameter;

    internal DeferredIndexArgument(
        string key,
        Expression expression,
        ParameterExpression? parameter,
        Func<object?, object?> resolver,
        bool needsBinding,
        string display)
    {
        _key = key;
        _expression = expression;
        _parameter = parameter;
        _resolver = resolver;
        NeedsBinding = needsBinding;
        Display = display;

        // 键在构造期冻结：实参可能是可变的自定义键类型，键待在字典里而哈希变了会让字典直接失效。
        var hash = new HashCode();
        hash.Add(key);
        _hash = hash.ToHashCode();
    }

    internal override string Display { get; }
    internal override bool NeedsBinding { get; }

    internal override bool SameAs(IndexArgument other)
        => other is DeferredIndexArgument argument && string.Equals(_key, argument._key, StringComparison.Ordinal);

    internal override void Hash(ref HashCode hash) => hash.Add(_hash);

    internal override object? Resolve(object? target) => _resolver(target);

    internal override Expression Build(Expression target, Expression arguments, ref int slot, Type parameterType)
    {
        if (NeedsBinding)
        {
            return Expression.Convert(
                Expression.ArrayIndex(arguments, Expression.Constant(slot++)), parameterType);
        }

        // 跟随档：把原始表达式内联进来，每帧现场求值——lambda 参数换成编译访问器的 target 形参。
        var body = _parameter is null
            ? _expression
            : new PathParameterReplacer(_parameter, Expression.Convert(target, _parameter.Type)).Visit(_expression);

        return Expression.Convert(body, parameterType);
    }
}

/// <summary>把一段表达式里的某个参数换成另一个表达式，用于把索引实参嫁接到编译访问器的形参上。</summary>
internal sealed class PathParameterReplacer(ParameterExpression from, Expression to) : ExpressionVisitor
{
    protected override Expression VisitParameter(ParameterExpression node)
        => ReferenceEquals(node, from) ? to : base.VisitParameter(node);
}

/// <summary>
/// 把索引实参表达式分类成 <see cref="IndexArgument"/>。失败（遇到表达不出来的节点、非法的实参类型）
/// 时返回 null，由调用方把整条路径当作不可解析——与 <c>TryCreate</c> 既有的「不认识就丢弃」一致，
/// 绝不输出被截断的身份。
/// </summary>
internal static class IndexArgumentFactory
{
    private static readonly MethodInfo FrozenDefinition =
        typeof(PathIndex).GetMethod(nameof(PathIndex.Frozen))!;

    internal static IndexArgument? TryCreate(Expression expression, ParameterExpression? parameter)
    {
        var frozen = false;
        expression = PathKey.Unwrap(expression);

        // 冻结标记只在解析期被结构性识别，从不求值——否则识别会退化成「按名字认方法」，可被同名方法冒充。
        if (expression is MethodCallExpression call && IsFrozen(call.Method))
        {
            frozen = true;
            expression = PathKey.Unwrap(call.Arguments[0]);
        }

        var argumentType = expression.Type;
        if (!IsSupportedArgumentType(argumentType)) return null;

        var display = expression.ToString();

        if (expression is ConstantExpression constant)
        {
            // 冻结一个常量是空操作，所以 [0] 与 [Frozen(0)] 是同一条路径。
            return new ConstantIndexArgument(argumentType, constant.Value);
        }

        // 闭包（不引用参数）按实例身份，跟随（引用参数）按结构身份。两者都传各自该用的那个 parameter。
        var referencesParameter = PathKey.References(expression, parameter);
        var identityParameter = referencesParameter ? parameter : null;
        var key = PathKey.For(expression, identityParameter);
        if (key is null) return null;

        var resolver = Compile(expression, identityParameter);
        if (resolver is null) return null;

        // 冻结与跟随是两条不同的路径：同一个实参写法在两种档位下行为不同，合成一个键会让后声明的覆盖掉先声明的。
        var identity = frozen ? $"frozen|{key}" : $"live|{key}";

        return new DeferredIndexArgument(identity, expression, identityParameter, resolver, frozen, display);
    }

    private static bool IsFrozen(MethodInfo method)
        => method.IsGenericMethod
           && method.GetGenericMethodDefinition() == FrozenDefinition;

    /// <summary>
    /// <c>System.Index</c> / <c>System.Range</c> 要挡在编译之前：它们不在 netstandard2.0 / netframework4.6.1 上，
    /// 表达式树里带着它们会让 <c>Compile()</c> 从内部抛 <c>TypeLoadException</c>——也就是从用户的
    /// <c>Property(...)</c> 里抛出来，既晚又难懂。
    /// </summary>
    private static bool IsSupportedArgumentType(Type type)
        => type.FullName is not "System.Index" and not "System.Range"
           && !(type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>)
                && type.GetGenericArguments()[0].FullName is "System.Index" or "System.Range");

    private static Func<object?, object?>? Compile(Expression expression, ParameterExpression? parameter)
    {
        try
        {
            var target = Expression.Parameter(typeof(object), "target");
            var body = expression;

            if (parameter is not null)
            {
                body = new PathParameterReplacer(parameter, Expression.Convert(target, parameter.Type)).Visit(body);
            }

            return Expression.Lambda<Func<object?, object?>>(
                Expression.Convert(body, typeof(object)), target).Compile();
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>
/// 实参表达式的规范化身份串。要求严格：遇到任何表达不出来的节点就返回 null（整条路径被丢弃），
/// 绝不输出截断的字符串——截断会让两条不同的路径撞成同一个键，是这里最坏的结果。
/// </summary>
internal static class PathKey
{
    private const int MaxDepth = 32;

    internal static string? For(Expression expression, ParameterExpression? parameter)
    {
        try
        {
            return Walk(Unwrap(expression), parameter, 0);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>表达式是否引用了这个 lambda 参数——决定它是闭包（按实例身份）还是跟随（按结构身份）。</summary>
    internal static bool References(Expression expression, ParameterExpression? parameter)
    {
        if (parameter is null) return false;

        var finder = new ParameterFinder(parameter);
        finder.Visit(expression);
        return finder.Found;
    }

    internal static Expression Unwrap(Expression expression)
    {
        while (expression is UnaryExpression unary
            && (unary.NodeType == ExpressionType.Convert || unary.NodeType == ExpressionType.ConvertChecked))
        {
            expression = unary.Operand;
        }
        return expression;
    }

    /// <summary>字面量的文本形式。值类型按值，引用类型按实例身份（字符串除外，它没有有意义的身份）。</summary>
    internal static string Literal(object? value)
    {
        if (value is null) return "null";
        if (value is string text) return $"str:{text}";

        var type = value.GetType();
        if (type.IsValueType)
        {
            return $"{type.FullName}:{Convert.ToString(value, CultureInfo.InvariantCulture) ?? "?"}";
        }
        return $"@{RuntimeHelpers.GetHashCode(value)}";
    }

    private static string? Walk(Expression expression, ParameterExpression? parameter, int depth)
    {
        if (depth > MaxDepth) return null;
        expression = Unwrap(expression);

        switch (expression)
        {
            case ParameterExpression parameterExpression:
                return ReferenceEquals(parameterExpression, parameter) ? "$0" : null;

            case ConstantExpression constant:
                return Literal(constant.Value);

            case MemberExpression member:
            {
                // 捕获的实例（闭包/显示类）按实例身份而不是按值：按值会让循环里逐次声明的路径撞成一个键，
                // 而实例身份跨解析稳定，且每次迭代都是新实例。
                if (member.Expression is ConstantExpression { Value: { } instance } && !instance.GetType().IsValueType)
                {
                    return $"@{RuntimeHelpers.GetHashCode(instance)}::{member.Member.DeclaringType?.FullName}::{member.Member.Name}";
                }

                var owner = member.Expression is null ? "static" : Walk(member.Expression, parameter, depth + 1);
                return owner is null ? null : $"{owner}.{member.Member.DeclaringType?.FullName}::{member.Member.Name}";
            }

            case MethodCallExpression call:
            {
                var owner = call.Object is null ? "static" : Walk(call.Object, parameter, depth + 1);
                if (owner is null) return null;

                var generic = call.Method.IsGenericMethod
                    ? "<" + string.Join(",", call.Method.GetGenericArguments().Select(static type => type.FullName)) + ">"
                    : string.Empty;

                var arguments = new string[call.Arguments.Count];
                for (var index = 0; index < arguments.Length; index++)
                {
                    var rendered = Walk(call.Arguments[index], parameter, depth + 1);
                    if (rendered is null) return null;
                    arguments[index] = rendered;
                }

                return $"{owner}.{call.Method.DeclaringType?.FullName}::{call.Method.Name}{generic}({string.Join(",", arguments)})";
            }

            case BinaryExpression binary:
            {
                var left = Walk(binary.Left, parameter, depth + 1);
                var right = Walk(binary.Right, parameter, depth + 1);
                return left is null || right is null ? null : $"({left}{binary.NodeType}{right})";
            }

            case UnaryExpression unary:
            {
                var operand = Walk(unary.Operand, parameter, depth + 1);
                return operand is null ? null : $"({unary.NodeType}{operand})";
            }

            case ConditionalExpression conditional:
            {
                var test = Walk(conditional.Test, parameter, depth + 1);
                var ifTrue = Walk(conditional.IfTrue, parameter, depth + 1);
                var ifFalse = Walk(conditional.IfFalse, parameter, depth + 1);
                return test is null || ifTrue is null || ifFalse is null ? null : $"(?{test}:{ifTrue}:{ifFalse})";
            }

            case IndexExpression index:
            {
                var target = index.Object is null ? "static" : Walk(index.Object, parameter, depth + 1);
                if (target is null) return null;

                var arguments = new string[index.Arguments.Count];
                for (var slot = 0; slot < arguments.Length; slot++)
                {
                    var rendered = Walk(index.Arguments[slot], parameter, depth + 1);
                    if (rendered is null) return null;
                    arguments[slot] = rendered;
                }

                var indexer = index.Indexer?.Name ?? "[]";
                return $"{target}#{indexer}({string.Join(",", arguments)})";
            }

            case DefaultExpression defaultExpression:
                return $"default({defaultExpression.Type.FullName})";

            case TypeBinaryExpression typeBinary:
            {
                var operand = Walk(typeBinary.Expression, parameter, depth + 1);
                return operand is null ? null : $"({operand} is {typeBinary.TypeOperand.FullName})";
            }

            // 明确拒绝：这些节点没有稳定的语法身份，硬串一个出来迟早会让两条路径撞键。
            default:
                return null;
        }
    }

    private sealed class ParameterFinder(ParameterExpression parameter) : ExpressionVisitor
    {
        internal bool Found { get; private set; }

        protected override Expression VisitParameter(ParameterExpression node)
        {
            if (ReferenceEquals(node, parameter)) Found = true;
            return base.VisitParameter(node);
        }
    }
}
