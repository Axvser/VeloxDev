namespace VeloxDev.AI;

/// <summary>
/// One assembly's contribution to the agent context tree.
/// </summary>
/// <remarks>
/// <para>
/// Every assembly that carries agent-annotated types contributes a fragment, and the generator emits a
/// <c>[ModuleInitializer]</c> that registers it. Several assemblies contribute under the same root, so nothing
/// here owns "the" tree — the registry unions what the fragments offer.
/// </para>
/// <para>
/// Directories are built one at a time. <see cref="ChildrenOf"/> is expected to materialise only the directory
/// it was asked for, which is what keeps an unused branch of the tree from ever being constructed.
/// </para>
/// </remarks>
public abstract class AIContextFragment
{
    /// <summary>The assembly whose types this fragment describes.</summary>
    public abstract string AssemblyName { get; }

    /// <summary>
    /// Every directory path this fragment contributes, including its root
    /// (<c>Framework</c> or <c>Customer</c>) and every nested directory beneath it.
    /// </summary>
    /// <remarks>
    /// Paths are slash-separated, with no leading or trailing slash — <c>Customer/Components/Nodes</c>.
    /// </remarks>
    public abstract IReadOnlyList<string> DirectoryPaths { get; }

    /// <summary>
    /// The children of one directory, or an empty list when this fragment has nothing there.
    /// </summary>
    /// <param name="directoryPath">A slash-separated directory path.</param>
    /// <returns>The directory's child nodes; built on this call, not earlier.</returns>
    public abstract IReadOnlyList<AIContextNode> ChildrenOf(string directoryPath);

    /// <summary>Full type names this fragment can resolve, parallel to <see cref="TypePaths"/>.</summary>
    public virtual IReadOnlyList<string> TypeNames => [];

    /// <summary>Paths of the type entries named in <see cref="TypeNames"/>, parallel and same length.</summary>
    public virtual IReadOnlyList<string> TypePaths => [];
}

/// <summary>
/// The agent context tree: every registered fragment, plus the accessors that act on the types it describes.
/// </summary>
/// <remarks>
/// <para>
/// Registration comes from generated module initializers, so there is no host wiring and no discovery step —
/// the tree is complete by the time any caller can reach it.
/// </para>
/// <para>
/// Everything a caller asks for is computed on demand from the fragments' directory listings. The type-name
/// index and the directory lookup tables are built on first use rather than at registration, so a process that
/// never touches the agent surface pays nothing for the tree existing.
/// </para>
/// </remarks>
public static class AIContextTreeRegistry
{
    /// <summary>The root under which framework types live.</summary>
    public const string FrameworkRoot = "Framework";

    /// <summary>The root under which types from consuming assemblies live.</summary>
    public const string CustomerRoot = "Customer";

    private static readonly object _lock = new();
    private static readonly List<AIContextFragment> _fragments = [];
    private static readonly Dictionary<string, IAIContextAccessor> _accessors = new(StringComparer.Ordinal);
    private static Dictionary<string, string>? _pathByTypeName;
    private static int _fragmentVersion;

    /// <summary>
    /// Adds one assembly's contribution.
    /// </summary>
    /// <param name="fragment">The fragment to add. Fragments are keyed by assembly name and replace one another.</param>
    public static void RegisterFragment(AIContextFragment fragment)
    {
        if (fragment is null) throw new ArgumentNullException(nameof(fragment));

        lock (_lock)
        {
            _fragments.RemoveAll(f => string.Equals(f.AssemblyName, fragment.AssemblyName, StringComparison.Ordinal));
            _fragments.Add(fragment);
            _pathByTypeName = null;
            _fragmentVersion++;
        }
    }

    // 注册在生产里是单向的：模块初始化器每个程序集只跑一次，也没有程序集会被卸载。这一条不给消费者用 ——
    // 它是 internal，只对 Core.Test 可见；那条并发用例要注册一个探针分片来观察注册表的行为，而它必须能把
    // 注册表**原样还回去**，不能给同进程后面每条用例留一份残留。
    internal static void UnregisterFragment(string assemblyName)
    {
        if (assemblyName is null) throw new ArgumentNullException(nameof(assemblyName));

        lock (_lock)
        {
            _fragments.RemoveAll(f => string.Equals(f.AssemblyName, assemblyName, StringComparison.Ordinal));
            _pathByTypeName = null;
            _fragmentVersion++;
        }
    }

    /// <summary>
    /// Adds the accessor that acts on one type.
    /// </summary>
    /// <param name="accessor">The accessor. Accessors are keyed by <see cref="IAIContextAccessor.TypeName"/>.</param>
    public static void RegisterAccessor(IAIContextAccessor accessor)
    {
        if (accessor is null) throw new ArgumentNullException(nameof(accessor));

        lock (_lock)
        {
            _accessors[accessor.TypeName] = accessor;
        }
    }

    /// <summary>Every registered fragment, in registration order.</summary>
    public static IReadOnlyList<AIContextFragment> Fragments
    {
        get { lock (_lock) return [.. _fragments]; }
    }

    /// <summary>
    /// Lists one directory's children across every fragment.
    /// </summary>
    /// <param name="directoryPath">
    /// A slash-separated directory path, or an empty string for the roots
    /// (<see cref="FrameworkRoot"/> and <see cref="CustomerRoot"/>).
    /// </param>
    /// <returns>The directory's children, ordered by name.</returns>
    public static IReadOnlyList<AIContextNode> List(string directoryPath)
    {
        if (directoryPath is null) throw new ArgumentNullException(nameof(directoryPath));

        var path = Normalize(directoryPath);
        if (path.Length == 0) return Roots();

        // 保持分片给的顺序，不排序：那是声明顺序，而渲染出来的表格逐字复现反射的输出。
        // 跨分片时按注册顺序拼接 —— 一个类型只属于一个分片，所以这里不会真的交错。
        var fragments = Fragments;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var children = new List<AIContextNode>();

        foreach (var fragment in fragments)
        {
            foreach (var child in fragment.ChildrenOf(path))
            {
                if (seen.Add(child.Name)) children.Add(child);
            }
        }

        return children;
    }

    /// <summary>
    /// Finds one node by its full path.
    /// </summary>
    /// <param name="path">A slash-separated path to a directory, type entry or member.</param>
    /// <returns>The node, or <see langword="null"/> when no fragment has that path.</returns>
    public static AIContextNode? FindEntry(string path)
    {
        if (path is null) throw new ArgumentNullException(nameof(path));

        var normalized = Normalize(path);
        if (normalized.Length == 0) return null;

        var separator = normalized.LastIndexOf('/');
        var parent = separator < 0 ? string.Empty : normalized.Substring(0, separator);
        var name = separator < 0 ? normalized : normalized.Substring(separator + 1);

        foreach (var node in List(parent))
        {
            if (string.Equals(node.Name, name, StringComparison.Ordinal)) return node;
        }

        return null;
    }

    /// <summary>
    /// Gets the tree path of a type entry from its full CLR name.
    /// </summary>
    /// <param name="typeFullName">The type's full name, as <see cref="Type.FullName"/> reports it.</param>
    /// <returns>The path, or <see langword="null"/> when the tree has no entry for that type.</returns>
    public static string? PathFor(string typeFullName)
    {
        if (typeFullName is null) throw new ArgumentNullException(nameof(typeFullName));

        // 索引的构建**不能**在 _lock 里做：读一个分片的 TypeNames 会把那个程序集的模块初始化器拉起来
        // （分片类型是另一个程序集的静态类），而那个初始化器自己会回来调 RegisterFragment/RegisterAccessor
        // 拿 _lock —— 于是「持锁等模块初始化器、初始化器所在线程等锁」是一个 ABBA 死锁：单线程下不会出现
        // （Monitor 可重入），两个线程各碰一半就会。这里改成锁内取快照、锁外构建，与 List 的写法一致。
        // 快照之后可能有分片注册（那时 _fragmentVersion 变了、这份索引少一个分片），所以按版本校验后重来；
        // 分片只在模块初始化期注册，循环次数自然很小。
        for (var attempt = 0; attempt < 4; attempt++)
        {
            var published = Volatile.Read(ref _pathByTypeName);
            if (published is not null)
                return published.TryGetValue(typeFullName, out var known) ? known : null;

            IReadOnlyList<AIContextFragment> fragments;
            int version;
            lock (_lock)
            {
                fragments = [.. _fragments];
                version = _fragmentVersion;
            }

            var built = BuildTypeIndex(fragments);

            lock (_lock)
            {
                if (_fragmentVersion != version) continue;
                _pathByTypeName ??= built;
            }
        }

        return null;
    }

    /// <summary>
    /// Gets the accessor that acts on the named type.
    /// </summary>
    /// <param name="typeFullName">The type's full name.</param>
    /// <returns>The accessor, or <see langword="null"/> when the type has none registered.</returns>
    public static IAIContextAccessor? FindAccessor(string typeFullName)
    {
        if (typeFullName is null) throw new ArgumentNullException(nameof(typeFullName));

        lock (_lock)
        {
            return _accessors.TryGetValue(typeFullName, out var accessor) ? accessor : null;
        }
    }

    /// <summary>
    /// Gets the accessor for an object, when its type has one.
    /// </summary>
    /// <param name="target">The object to act on.</param>
    /// <returns>The accessor, or <see langword="null"/> when the target's type has none registered.</returns>
    public static IAIContextAccessor? FindAccessor(object? target)
        => target is null ? null : FindAccessor(target.GetType().FullName ?? target.GetType().Name);

    private static Dictionary<string, string> BuildTypeIndex(IReadOnlyList<AIContextFragment> fragments)
    {
        var index = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var fragment in fragments)
        {
            var names = fragment.TypeNames;
            var paths = fragment.TypePaths;

            for (var i = 0; i < names.Count && i < paths.Count; i++)
            {
                index[names[i]] = paths[i];
            }
        }

        return index;
    }

    private static AIContextNode[] Roots()
    {
        var fragments = Fragments;
        if (fragments.Count == 0) return [];

        var hasFramework = fragments.Any(static f => f.DirectoryPaths.Any(static p => p == FrameworkRoot));
        var hasCustomer = fragments.Any(static f => f.DirectoryPaths.Any(static p => p == CustomerRoot));

        var roots = new List<AIContextNode>(2);
        if (hasFramework) roots.Add(Directory(FrameworkRoot));
        if (hasCustomer) roots.Add(Directory(CustomerRoot));
        return [.. roots];
    }

    private static AIContextNode Directory(string name) => new(name, AIContextNodeKind.Directory);

    // 路径分隔与大小写按 Ordinal 处理：路径是生成期写死的字面量，不做任何规范化猜测。
    // 不用 Range/Index 与 StartsWith(char)：本程序集要编到 netstandard2.0，两者都要 netstandard2.1。
    private static string Normalize(string path)
    {
        var trimmed = path.Trim();

        var start = 0;
        while (start < trimmed.Length && trimmed[start] == '/') start++;

        var end = trimmed.Length;
        while (end > start && trimmed[end - 1] == '/') end--;

        return trimmed.Substring(start, end - start);
    }
}
