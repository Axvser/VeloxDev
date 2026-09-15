using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;

namespace VeloxDev.TransitionSystem.Abstractions;

/// <summary>
/// A path to one animatable value: a chain of property segments, index segments, or both
/// (<c>x.Foo.Bar</c>, <c>x.Items[0].Width</c>, <c>x.Map["player"].Color</c>).
/// </summary>
/// <remarks>
/// The path is both the accessor (compiled once into a getter/setter delegate) and the <b>identity</b> of the value
/// inside a <c>StateCore</c>: it is a dictionary key, and the same lambda is parsed several times over — once by
/// <c>SetValue</c>, again by <c>SetOptions</c>, again by <c>TryGetValue</c>. Equality is therefore load-bearing, and
/// the property segments and the index arguments alike have to compare by value and hash in step with it.
/// <para>
/// Nothing in the identity may depend on a target, and <b>accessibility is not consulted</b>: a path is judged by
/// whether the member at its end can be read and written, never by how accessible it is. <c>private set</c> and
/// <c>internal</c> members animate, and <see cref="FromProperty"/> takes any <c>PropertyInfo</c> you can obtain —
/// deliberately, since narrowing that would silently stop animations that work today.
/// </para>
/// </remarks>
public sealed class TransitionProperty : ITransitionProperty, IEquatable<TransitionProperty>
{
    private readonly PathSegment[] _segments;
    private readonly ParameterExpression? _parameter;
    private readonly int _boundArgumentCount;

    // 实参在调用现场给，编译只做一次：冻结档若把实参编进表达式，每个动画就要付一次 Reflection.Emit。
    private Func<object, object?[]?, object?>? _compiledGetter;
    private Func<object, object?[]?, object?, bool>? _compiledSetter;

    /// <summary>
    /// Builds a path from property segments. Index segments cannot be expressed this way — an indexer needs its
    /// arguments — so this constructor rejects them; use the expression factories instead.
    /// </summary>
    public TransitionProperty(IEnumerable<PropertyInfo> segments)
        : this(ToPropertySegments(segments), null)
    {
    }

    private TransitionProperty(PathSegment[] segments, ParameterExpression? parameter)
    {
        if (segments.Length == 0)
        {
            throw new ArgumentException("Property path must contain at least one segment.", nameof(segments));
        }

        _segments = segments;
        _parameter = parameter;

        // 索引段自带方括号，只在属性段前面补点：Items[0].Width，而不是 Items.[0].Width。
        var builder = new StringBuilder();
        foreach (var segment in segments)
        {
            if (builder.Length > 0 && segment is PropertySegment) builder.Append('.');
            builder.Append(segment.Display);
        }
        Path = builder.ToString();
        PropertyType = segments[segments.Length - 1].ValueType;
        CanRead = segments.All(IsReadable);
        CanWrite = IsWritable(segments[segments.Length - 1]);

        var bound = 0;
        foreach (var segment in segments)
        {
            foreach (var argument in ArgumentsOf(segment))
            {
                if (argument.NeedsBinding) bound++;
            }
        }
        _boundArgumentCount = bound;
    }

    /// <inheritdoc />
    public string Path { get; }

    /// <inheritdoc />
    public Type PropertyType { get; }

    /// <inheritdoc />
    public bool CanRead { get; }

    /// <inheritdoc />
    public bool CanWrite { get; }

    /// <summary>
    /// When the property path is invalid for the current target (the intermediate object's runtime type does not
    /// match, e.g. RenderTransform is a RotateTransform but TranslateTransform.X is being read),
    /// <see cref="GetValue(object?)"/> returns this sentinel. Callers should skip the property rather than interpolate it
    /// as a null value — otherwise the invalid path would be treated as 0/identity, causing distortion (e.g.
    /// rotation/3D transforms incorrectly treated as starting from identity).
    /// </summary>
    public static readonly object UnreadablePath = new();

    public object? GetValue(object? target)
    {
        if (target is null)
        {
            throw new ArgumentNullException(nameof(target));
        }

        return (_compiledGetter ??= CompileGetter())(target, null);
    }

    public bool SetValue(object target, object? value)
    {
        if (target is null)
        {
            throw new ArgumentNullException(nameof(target));
        }

        return (_compiledSetter ??= CompileSetter())(target, null, value);
    }

    internal object? GetValue(object? target, object?[]? boundArguments)
        => (_compiledGetter ??= CompileGetter())(target!, boundArguments);

    internal bool SetValue(object target, object? value, object?[]? boundArguments)
        => (_compiledSetter ??= CompileSetter())(target, boundArguments, value);

    /// <summary>
    /// Resolves the arguments that are marked frozen against <paramref name="target"/>, once, and returns a property
    /// whose reads and writes reuse them for the whole animation.
    /// </summary>
    /// <remarks>
    /// Returns <c>this</c> when there is nothing to freeze, which is the common case: an argument with no
    /// <see cref="PathIndex.Frozen{T}"/> is re-evaluated on every frame, so nothing has to be captured. Only the
    /// frozen gear pays for a wrapper.
    /// <para>
    /// This has to be called from <c>InterpolatorCore.Prepare</c> (or any override of it) — that is the first moment
    /// a target exists. An override that does not call the base loses the freeze silently, because the unbound
    /// property resolves its arguments too and behaves identically until a frozen argument is involved.
    /// </para>
    /// </remarks>
    internal ITransitionProperty BindTo(object target)
    {
        if (_boundArgumentCount == 0) return this;

        var values = new object?[_boundArgumentCount];
        var slot = 0;
        foreach (var segment in _segments)
        {
            foreach (var argument in ArgumentsOf(segment))
            {
                if (argument.NeedsBinding) values[slot++] = argument.Resolve(target);
            }
        }

        return new BoundTransitionProperty(this, values);
    }

    /// <summary>Reads and writes through another instance's compiled accessors with a fixed set of index arguments.</summary>
    private sealed class BoundTransitionProperty(TransitionProperty inner, object?[] arguments) : ITransitionProperty
    {
        public string Path => inner.Path;
        public Type PropertyType => inner.PropertyType;
        public bool CanRead => inner.CanRead;
        public bool CanWrite => inner.CanWrite;

        public object? GetValue(object? target) => inner.GetValue(target, arguments);
        public bool SetValue(object target, object? value) => inner.SetValue(target, value, arguments);
    }

    private static IEnumerable<IndexArgument> ArgumentsOf(PathSegment segment) => segment switch
    {
        IndexerSegment indexer => indexer.Arguments,
        ArrayIndexSegment array => array.Arguments,
        _ => []
    };

    /// <summary>
    /// Whether every step of the path can be traversed. An indexer is a member like any other here: a get-only
    /// indexer is still perfectly readable, and is a normal way to reach a writable member
    /// (<c>x.ReadOnlyItems[0].Width</c>).
    /// </summary>
    private static bool IsReadable(PathSegment segment) => segment switch
    {
        PropertySegment property => property.Property.CanRead,
        IndexerSegment indexer => indexer.Indexer.GetMethod is not null,
        _ => true
    };

    /// <summary>
    /// Whether a path may be animated, asked of the <b>last segment only</b>.
    /// </summary>
    /// <remarks>
    /// Writability is a property of the member the path ends on, never of how the path got there: an indexer that
    /// cannot be assigned to is an ordinary read-only member — the same answer a read-only property gets — and it
    /// is not a reason to reject, truncate or throw. What is checked is the leaf, whatever it happens to be.
    /// <para>
    /// A path whose <em>last</em> member is declared on a value type cannot be written: reading it yields a copy of
    /// the enclosing value, so the assignment lands in a temporary. A value type earlier on the path is harmless,
    /// because a reference reached through it still points at the real object.
    /// </para>
    /// </remarks>
    private static bool IsWritable(PathSegment segment) => segment switch
    {
        PropertySegment property => property.Property.CanWrite,
        IndexerSegment indexer => indexer.Indexer.SetMethod is not null,
        _ => true
    };

    private static PathSegment[] ToPropertySegments(IEnumerable<PropertyInfo> segments)
    {
        var array = segments?.ToArray() ?? [];
        if (array.Length == 0)
        {
            throw new ArgumentException("Property path must contain at least one property.", nameof(segments));
        }

        if (array.Any(static property => property.GetIndexParameters().Length > 0))
        {
            throw new ArgumentException("Indexed properties are not supported by this constructor; write the indexer into an expression instead.", nameof(segments));
        }

        return [.. array.Select(static property => (PathSegment)new PropertySegment(property))];
    }

    /// <summary>
    /// Builds the path of an expression such as <c>x =&gt; x.Foo.Bar</c> or <c>x =&gt; x.Items[0].Width</c>.
    /// </summary>
    /// <remarks>
    /// Returns <c>false</c> for an expression this walk cannot describe — an intermediate method call, a declaration
    /// whose index argument has no stable identity. The path is dropped rather than approximated: a truncated
    /// identity would let two different paths collide on one dictionary entry.
    /// </remarks>
    public static bool TryCreate(LambdaExpression expression, out TransitionProperty? property)
    {
        property = null;
        if (expression is null)
        {
            return false;
        }

        var parameter = expression.Parameters.Count > 0 ? expression.Parameters[0] : null;
        var current = PathKey.Unwrap(expression.Body);
        var segments = new Stack<PathSegment>();

        while (true)
        {
            switch (current)
            {
                case MemberExpression member:
                {
                    if (member.Member is not PropertyInfo propertyInfo || propertyInfo.GetIndexParameters().Length > 0)
                    {
                        return false;
                    }

                    segments.Push(new PropertySegment(propertyInfo));

                    if (member.Expression is null) return false;
                    current = PathKey.Unwrap(member.Expression);
                    continue;
                }

                case MethodCallExpression call:
                {
                    if (call.Object is null) return false;

                    if (IsMultiDimensionalArrayGet(call, out var arrayType, out var elementType))
                    {
                        var indices = BuildArguments(call.Arguments, parameter);
                        if (indices is null) return false;

                        segments.Push(new ArrayIndexSegment(arrayType, elementType, indices));
                        current = PathKey.Unwrap(call.Object);
                        continue;
                    }

                    var indexer = FindIndexer(call);
                    if (indexer is null) return false;

                    var arguments = BuildArguments(call.Arguments, parameter);
                    if (arguments is null) return false;

                    segments.Push(new IndexerSegment(indexer, indexer.DeclaringType!, arguments));
                    current = PathKey.Unwrap(call.Object);
                    continue;
                }

                case BinaryExpression { NodeType: ExpressionType.ArrayIndex } arrayIndex:
                {
                    if (arrayIndex.Left.Type.GetElementType() is not { } elementType) return false;

                    var arguments = BuildArguments([arrayIndex.Right], parameter);
                    if (arguments is null) return false;

                    segments.Push(new ArrayIndexSegment(arrayIndex.Left.Type, elementType, arguments));
                    current = PathKey.Unwrap(arrayIndex.Left);
                    continue;
                }

                case IndexExpression indexExpression:
                {
                    // Indexer == null 是多维数组的元素访问，它也走数组段。
                    if (indexExpression.Object is null) return false;

                    var declaring = indexExpression.Object.Type;
                    var arguments = BuildArguments(indexExpression.Arguments, parameter);
                    if (arguments is null) return false;

                    segments.Push(indexExpression.Indexer is null
                        ? new ArrayIndexSegment(declaring, indexExpression.Type, arguments)
                        : new IndexerSegment(indexExpression.Indexer, declaring, arguments));

                    current = PathKey.Unwrap(indexExpression.Object);
                    continue;
                }

                case ParameterExpression parameterExpression:
                {
                    if (segments.Count == 0) return false;
                    if (!ReferenceEquals(parameterExpression, parameter)) return false;

                    property = new TransitionProperty([.. segments], parameter);
                    return true;
                }

                default:
                    return false;
            }
        }
    }

    /// <summary>
    /// 多维数组的元素访问在表达式树里<b>不是</b> <c>IndexExpression</c>，而是编译器生成的 <c>Array.Get(i, j)</c> 调用，
    /// 所以它得在这里认出来。<c>Get</c> 在 <see cref="Array"/> 上是 internal 的，用户代码写不出这个调用。
    /// </summary>
    private static bool IsMultiDimensionalArrayGet(MethodCallExpression call, out Type arrayType, out Type elementType)
    {
        arrayType = typeof(void);
        elementType = typeof(void);

        var type = call.Object!.Type;
        if (!type.IsArray || type.GetArrayRank() < 2) return false;

        // 数组类型自己声明访问器：声明类型等于数组类型本身，是这条路径只可能来自编译器生成的最佳证据。
        if (call.Method.DeclaringType != type || call.Method.Name != "Get") return false;
        if (call.Arguments.Count != type.GetArrayRank()) return false;

        arrayType = type;
        elementType = type.GetElementType()!;
        return true;
    }

    /// <summary>
    /// Finds the indexer a <c>get_Item</c> call belongs to, by name shape and parameter types. Comparing the getter
    /// by reference does not survive the interface/implementation split (an <c>IList&lt;T&gt;</c> call and the
    /// <c>List&lt;T&gt;</c> property are different <see cref="MethodInfo"/> instances).
    /// </summary>
    private static PropertyInfo? FindIndexer(MethodCallExpression call)
    {
        var method = call.Method;
        if (!method.IsSpecialName || !method.Name.StartsWith("get_", StringComparison.Ordinal)) return null;

        var declaring = method.DeclaringType;
        if (declaring is null) return null;

        var parameters = method.GetParameters();

        // 公开的优先，找不到再找非公开的：可访问性不是这个系统的判定条件（见类型注释），所以索引器不能因为
        // 是非公开的就成为唯一一种悄悄失效的成员。
        if (Find(declaring, parameters, BindingFlags.Public | BindingFlags.Instance) is { } declared) return declared;
        return Find(declaring, parameters, BindingFlags.NonPublic | BindingFlags.Instance);

        static PropertyInfo? Find(Type declaring, System.Reflection.ParameterInfo[] parameters, BindingFlags flags)
        {
            foreach (var candidate in declaring.GetProperties(flags))
            {
                var indexParameters = candidate.GetIndexParameters();
                if (indexParameters.Length == 0 || indexParameters.Length != parameters.Length) continue;

                var matches = true;
                for (var index = 0; index < parameters.Length; index++)
                {
                    if (indexParameters[index].ParameterType != parameters[index].ParameterType)
                    {
                        matches = false;
                        break;
                    }
                }

                if (matches) return candidate;
            }

            return null;
        }
    }

    private static IndexArgument[]? BuildArguments(IReadOnlyList<Expression> expressions, ParameterExpression? parameter)
    {
        var arguments = new IndexArgument[expressions.Count];
        for (var index = 0; index < arguments.Length; index++)
        {
            var argument = IndexArgumentFactory.TryCreate(expressions[index], parameter);
            if (argument is null) return null;

            arguments[index] = argument;
        }

        return arguments;
    }

    private static Expression BuildAccess(
        Expression instance,
        PathSegment segment,
        Expression target,
        Expression arguments,
        ref int slot)
    {
        switch (segment)
        {
            case PropertySegment property:
                return Expression.Property(instance, property.Property);

            case IndexerSegment indexer:
            {
                var built = new Expression[indexer.Arguments.Length];
                for (var position = 0; position < built.Length; position++)
                {
                    built[position] = indexer.Arguments[position].Build(target, arguments, ref slot, indexer.ParameterTypes[position]);
                }
                return Expression.MakeIndex(instance, indexer.Indexer, built);
            }

            case ArrayIndexSegment array:
            {
                var built = new Expression[array.Arguments.Length];
                for (var position = 0; position < built.Length; position++)
                {
                    built[position] = array.Arguments[position].Build(target, arguments, ref slot, typeof(int));
                }
                return Expression.ArrayAccess(instance, built);
            }

            default:
                throw new InvalidOperationException($"Unknown path segment {segment.GetType().Name}.");
        }
    }

    /// <summary>
    /// Turns an out-of-range or missing index into the silent outcome the caller already expects instead of an
    /// exception.
    /// </summary>
    /// <remarks>
    /// <c>Expression.MakeIndex</c> <em>throws</em> when an index is out of range — it does not return a miss. Without
    /// this the live gear would throw out of <c>Prepare</c> and then again on every frame, from inside a dispatcher
    /// callback on the UI thread, where nothing catches. A try/catch costs nothing while nothing is thrown, which is
    /// every frame but the pathological ones.
    /// </remarks>
    private static Expression GuardIndexExceptions(Expression body, object sentinel)
    {
        var fallback = Expression.Constant(sentinel, body.Type);
        return Expression.TryCatch(
            body,
            Expression.Catch(typeof(IndexOutOfRangeException), fallback),
            Expression.Catch(typeof(ArgumentOutOfRangeException), fallback),
            Expression.Catch(typeof(KeyNotFoundException), fallback));
    }

    /// <summary>
    /// Compiles the "per-segment navigation + type/null checks" into a single delegate, eliminating per-frame
    /// per-property reflection overhead (the hot path of <c>SamplerSet</c> and of the host's read).
    ///
    /// The semantics distinguish two kinds of "cannot read":
    /// - The intermediate object is null (the value is genuinely null) → returns null, and the interpolator starts
    ///   from identity/default.
    /// - The intermediate object is non-null but the type does not match (the path is invalid for the current
    ///   target) → returns <see cref="UnreadablePath"/>, and the caller skips the property to avoid distorting
    ///   interpolation by treating it as a null value.
    /// </summary>
    private Func<object, object?[]?, object?> CompileGetter()
    {
        if (!CanRead)
        {
            return static (_, _) => UnreadablePath;
        }

        var target = Expression.Parameter(typeof(object), "target");
        var arguments = Expression.Parameter(typeof(object?[]), "arguments");
        var slot = 0;

        Expression current = target;
        foreach (var segment in _segments)
        {
            var declaringType = segment.DeclaringType;
            var isNull = Expression.Equal(current, Expression.Constant(null));
            var isCorrectType = Expression.TypeIs(current, declaringType);

            var typed = declaringType.IsValueType
                ? Expression.Convert(current, declaringType)
                : (Expression)Expression.TypeAs(current, declaringType);

            var readBoxed = Expression.Convert(BuildAccess(typed, segment, target, arguments, ref slot), typeof(object));

            // Current object is non-null but type does not match → path invalid, return the sentinel (caller skips)
            var invalid = Expression.AndAlso(
                Expression.Not(isNull),
                Expression.Not(isCorrectType));

            current = Expression.Condition(
                invalid,
                Expression.Constant(UnreadablePath),
                Expression.Condition(isNull, Expression.Constant(null), readBoxed));
        }

        return Expression.Lambda<Func<object, object?[]?, object?>>(
            GuardIndexExceptions(current, UnreadablePath), target, arguments).Compile();
    }

    /// <summary>
    /// Compiles the "per-segment navigation + final assignment + type/null checks" into a single delegate.
    /// Preserves the original semantics: returns false when an intermediate object's type does not match or is
    /// null, instead of throwing TargetException.
    /// </summary>
    private Func<object, object?[]?, object?, bool> CompileSetter()
    {
        if (!CanWrite)
        {
            return static (_, _, _) => false;
        }

        var target = Expression.Parameter(typeof(object), "target");
        var arguments = Expression.Parameter(typeof(object?[]), "arguments");
        var value = Expression.Parameter(typeof(object), "value");

        var current = Expression.Variable(typeof(object), "current");
        var exit = Expression.Label(typeof(bool), "exit");
        var fail = Expression.Return(exit, Expression.Constant(false));
        var slot = 0;

        var statements = new List<Expression> { Expression.Assign(current, target) };

        for (var index = 0; index < _segments.Length - 1; index++)
        {
            var segment = _segments[index];
            var declaringType = segment.DeclaringType;

            statements.Add(Expression.IfThen(
                Expression.Not(Expression.TypeIs(current, declaringType)),
                fail));

            var typed = declaringType.IsValueType
                ? Expression.Convert(current, declaringType)
                : (Expression)Expression.TypeAs(current, declaringType);

            statements.Add(Expression.Assign(
                current,
                Expression.Convert(BuildAccess(typed, segment, target, arguments, ref slot), typeof(object))));
        }

        var finalSegment = _segments[_segments.Length - 1];
        var finalType = finalSegment.DeclaringType;

        statements.Add(Expression.IfThen(
            Expression.Not(Expression.TypeIs(current, finalType)),
            fail));

        // 末段当左值赋值：属性、索引器、数组元素都能就地写入，不再经过装箱副本。
        var finalAccess = BuildAccess(
            finalType.IsValueType
                ? Expression.Convert(current, finalType)
                : (Expression)Expression.TypeAs(current, finalType),
            finalSegment, target, arguments, ref slot);

        var propertyType = finalSegment.ValueType;
        Expression valueCheck = Expression.TypeIs(value, propertyType);
        if (!propertyType.IsValueType)
        {
            valueCheck = Expression.OrElse(valueCheck, Expression.Equal(value, Expression.Constant(null)));
        }

        statements.Add(Expression.IfThen(Expression.Not(valueCheck), fail));
        statements.Add(Expression.Assign(finalAccess, Expression.Convert(value, propertyType)));
        statements.Add(Expression.Label(exit, Expression.Constant(true)));

        var body = Expression.Block([current], statements);
        return Expression.Lambda<Func<object, object?[]?, object?, bool>>(
            GuardIndexExceptions(body, false), target, arguments, value).Compile();
    }

    /// <summary>
    /// Wraps one <see cref="PropertyInfo"/> as a path. Instances are memoized per property, so the same
    /// <see cref="PropertyInfo"/> always yields the same path.
    /// </summary>
    /// <remarks>
    /// Memoized because this is the reflection-driven entry point: the theme system rebuilds a path for every themed
    /// property of every registered target on <em>every</em> switch, where a declaration-based path is built once and
    /// held in a field. A fresh instance compiles its own getter and setter on first use, so without this a switch
    /// over N elements pays N × properties expression compilations each time — measured at roughly two seconds of
    /// UI-thread stall for a thousand two-property elements, before the first frame.
    /// <para>
    /// Sharing is safe: a property path is immutable, and <see cref="BindTo"/> returns the instance itself when there
    /// are no index arguments to freeze, which is always the case here. The lazy compile is idempotent, so the worst
    /// a concurrent first use can do is compile twice and discard one.
    /// </para>
    /// </remarks>
    public static TransitionProperty FromProperty(PropertyInfo propertyInfo)
    {
        if (propertyInfo is null)
        {
            throw new ArgumentNullException(nameof(propertyInfo));
        }

        return FromPropertyCache.GetValue(propertyInfo, static info => new TransitionProperty([info]));
    }

    /// <summary>
    /// Keyed weakly, and deliberately not a <c>ConcurrentDictionary</c>: the entry is a strong
    /// chain — path → segment → <see cref="PropertyInfo"/> → <c>Type</c> → <c>Assembly</c> — so a strong key would
    /// pin the assembly for the life of the process, and a collectible <c>AssemblyLoadContext</c> could never
    /// unload. The value referring back to the key is what a weak table is designed for; the memo still holds for
    /// as long as the property itself does, which is every case the cache exists for.
    /// </summary>
    private static readonly ConditionalWeakTable<PropertyInfo, TransitionProperty> FromPropertyCache = new();

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
    /// Declares a set of readable member paths from expressions (for struct <see cref="ISampleable"/> assembly).
    /// Unlike <see cref="Members{TSource}"/> it does not require writability — a struct's members are only read and
    /// reassembled through its constructor.
    /// </summary>
    public static IReadOnlyList<ITransitionProperty> ReadableMembers<TSource>(params Expression<Func<TSource, object?>>[] expressions)
    {
        List<ITransitionProperty> members = [];
        foreach (var expression in expressions)
        {
            if (TryCreate(expression, out var property) && property is not null && property.CanRead)
            {
                members.Add(property);
            }
        }
        return members;
    }

    /// <summary>Combines a prefix path with a suffix path: prefix = target.Foo, suffix = Foo.Bar → target.Foo.Bar.</summary>
    public static TransitionProperty Combine(TransitionProperty prefix, TransitionProperty suffix)
    {
        if (prefix is null)
        {
            throw new ArgumentNullException(nameof(prefix));
        }

        if (suffix is null)
        {
            throw new ArgumentNullException(nameof(suffix));
        }

        return new TransitionProperty([.. prefix._segments, .. suffix._segments], prefix._parameter);
    }

    public bool Equals(TransitionProperty? other)
    {
        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (other is null || _segments.Length != other._segments.Length)
        {
            return false;
        }

        for (var index = 0; index < _segments.Length; index++)
        {
            if (!_segments[index].SameAs(other._segments[index]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>True when this path sits strictly below <paramref name="other"/> (the same path is not a descendant).</summary>
    public bool IsDescendantOf(TransitionProperty other)
    {
        if (other is null)
        {
            throw new ArgumentNullException(nameof(other));
        }

        if (_segments.Length <= other._segments.Length)
        {
            return false;
        }

        for (var index = 0; index < other._segments.Length; index++)
        {
            if (!_segments[index].SameAs(other._segments[index]))
            {
                return false;
            }
        }

        return true;
    }

    public override bool Equals(object? obj) => obj is TransitionProperty other && Equals(other);

    /// <summary>
    /// Hashes exactly what <see cref="Equals(TransitionProperty?)"/> compares, segment by segment — the two must stay
    /// in step, otherwise a <c>HashSet</c> or a dictionary keeps equal paths as two separate entries and exclusion
    /// or conflict detection silently never matches.
    /// </summary>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var segment in _segments)
        {
            // 段的哈希逐段累加；每个段类型自己保证哈希字段与 SameAs 比较的字段一致。
            segment.Hash(ref hash);
        }

        return hash.ToHashCode();
    }

    /// <remarks>
    /// Diagnostic only — it is <b>not</b> the identity. Two paths that compare equal can render differently when the
    /// same index is written with a different lambda parameter name.
    /// </remarks>
    public override string ToString() => Path;
}
