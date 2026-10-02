namespace VeloxDev.AI;

/// <summary>
/// Reads the agent context tree: lists directories, fetches entries, and resolves cross-links.
/// </summary>
/// <remarks>
/// <para>
/// Navigation only — no rendering. What a caller does with a node is its own business, so this stays free of
/// any particular output format and both a Markdown renderer and a tool implementation can sit on top of it.
/// </para>
/// <para>
/// Every member is a lookup against the registered fragments, and a directory is built the first time it is
/// asked for. Nothing here walks a subtree to answer a question about its parent, which is what makes retrieval
/// incremental: listing <c>Components/Nodes</c> does not build any entry's member directories.
/// </para>
/// </remarks>
public sealed class AIContextDirectory
{
    private static readonly AIContextDirectory _shared = new();

    /// <summary>The directory over every registered fragment.</summary>
    public static AIContextDirectory Shared { get; } = _shared;

    /// <summary>
    /// Lists one directory's children.
    /// </summary>
    /// <param name="directoryPath">A slash-separated path; an empty string lists the roots.</param>
    /// <returns>The children, ordered by name.</returns>
    public IReadOnlyList<AIContextNode> List(string directoryPath)
        => AIContextTreeRegistry.List(directoryPath);

    /// <summary>
    /// Lists one directory's children's names.
    /// </summary>
    /// <param name="directoryPath">A slash-separated path; an empty string lists the roots.</param>
    /// <returns>The names, ordered.</returns>
    public IReadOnlyList<string> ListNames(string directoryPath)
    {
        var children = List(directoryPath);
        var names = new string[children.Count];
        for (var i = 0; i < children.Count; i++) names[i] = children[i].Name;
        return names;
    }

    /// <summary>
    /// Finds one node by path.
    /// </summary>
    /// <param name="entryPath">A slash-separated path to a directory, type entry or member.</param>
    /// <returns>The node, or <see langword="null"/> when no fragment has that path.</returns>
    public AIContextNode? Entry(string entryPath)
        => AIContextTreeRegistry.FindEntry(entryPath);

    /// <summary>
    /// Finds the tree path of a type entry.
    /// </summary>
    /// <param name="typeFullName">The type's full name, as <see cref="Type.FullName"/> reports it.</param>
    /// <returns>The path, or <see langword="null"/> when the tree has no entry for that type.</returns>
    public string? PathFor(string typeFullName)
        => AIContextTreeRegistry.PathFor(typeFullName);

    /// <summary>
    /// Finds the tree path of a type entry.
    /// </summary>
    /// <param name="type">The type.</param>
    /// <returns>The path, or <see langword="null"/> when the tree has no entry for that type.</returns>
    public string? PathFor(Type type)
        => type is null ? null : PathFor(type.FullName ?? type.Name);

    /// <summary>
    /// Lists one of an entry's member directories — <c>Properties</c>, <c>Fields</c>, <c>Commands</c>,
    /// <c>Methods</c> or <c>Members</c>.
    /// </summary>
    /// <param name="entryPath">The type entry's path.</param>
    /// <param name="memberDirectory">The member directory's name.</param>
    /// <returns>The members, ordered by name. Empty when the entry has no such directory.</returns>
    public IReadOnlyList<AIContextNode> Members(string entryPath, string memberDirectory)
        => List(entryPath + "/" + memberDirectory);

    /// <summary>
    /// Lists one of a type's member directories including everything it inherits, walking the base-type chain
    /// the tree records.
    /// </summary>
    /// <param name="typeFullName">The type's full name.</param>
    /// <param name="memberDirectory">The member directory's name.</param>
    /// <returns>
    /// The members, most-derived first. A name declared more than once in the chain appears once — the derived
    /// declaration hides the base one, which is what a caller enumerating the type's members would see.
    /// </returns>
    /// <remarks>
    /// The walk stops at the first base type the tree does not carry: a base outside the compiled tree contributes
    /// nothing, since nothing downstream could act on it either.
    /// </remarks>
    public IReadOnlyList<AIContextNode> MembersAcross(string typeFullName, string memberDirectory)
    {
        if (typeFullName is null) throw new ArgumentNullException(nameof(typeFullName));
        if (memberDirectory is null) throw new ArgumentNullException(nameof(memberDirectory));

        var visited = new HashSet<string>(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var members = new List<AIContextNode>();

        var current = typeFullName;
        while (current is not null && visited.Add(current))
        {
            var path = PathFor(current);
            if (path is null) break;

            foreach (var member in List(path + "/" + memberDirectory))
            {
                if (seen.Add(member.Name)) members.Add(member);
            }

            current = BaseTypeNameOf(path);
        }

        return members;
    }

    /// <summary>
    /// Finds one member of a type by name, in one member directory, including what the type inherits.
    /// </summary>
    /// <param name="typeFullName">The type's full name.</param>
    /// <param name="memberDirectory">The member directory to search — <c>Properties</c>, <c>Commands</c>, …</param>
    /// <param name="memberName">The member's name.</param>
    /// <returns>The node, or <see langword="null"/> when the type has no such member.</returns>
    public AIContextNode? MemberAcross(string typeFullName, string memberDirectory, string memberName)
    {
        foreach (var member in MembersAcross(typeFullName, memberDirectory))
        {
            if (string.Equals(member.Name, memberName, StringComparison.Ordinal)) return member;
        }

        return null;
    }

    /// <summary>The full name of the entry's base type, or <see langword="null"/> when the tree records none.</summary>
    private static string? BaseTypeNameOf(string entryPath)
    {
        var entry = AIContextTreeRegistry.FindEntry(entryPath);
        if (entry is null) return null;

        foreach (var reference in entry.References)
        {
            if (reference.Kind != AIContextRefKind.BaseType) continue;

            return reference.DeclaredName.Length > 0 ? reference.DeclaredName : null;
        }

        return null;
    }

    /// <summary>
    /// Finds one member of a type entry by name, wherever it sits.
    /// </summary>
    /// <param name="typeFullName">The declaring type's full name.</param>
    /// <param name="memberName">The member's name.</param>
    /// <returns>The member node, or <see langword="null"/> when the tree has no such member.</returns>
    /// <remarks>
    /// Searches the member directories rather than taking one, because a caller holding a
    /// <see cref="System.Reflection.MemberInfo"/> knows the member's declaring type and name but not which table
    /// the renderer would have put it in. A command property is the case that matters: it lives under
    /// <c>Commands</c>, not <c>Properties</c>.
    /// </remarks>
    public AIContextNode? Member(string typeFullName, string memberName)
    {
        if (typeFullName is null || memberName is null) return null;

        var path = PathFor(typeFullName);
        if (path is null) return null;

        foreach (var directory in MemberDirectories)
        {
            foreach (var member in List(path + "/" + directory))
            {
                if (string.Equals(member.Name, memberName, StringComparison.Ordinal)) return member;
            }
        }

        return null;
    }

    /// <summary>The directory names a type entry's members can live under.</summary>
    private static readonly string[] MemberDirectories = ["Properties", "Fields", "Commands", "Methods", "Members"];

    /// <summary>
    /// Resolves a cross-link to the node it names.
    /// </summary>
    /// <param name="reference">The reference, taken from a node's <see cref="AIContextNode.References"/>.</param>
    /// <returns>
    /// The node it points at, or <see langword="null"/> when the target is not in the tree — which is the
    /// documented case for a <c>[SlotSelectors]</c> name that matches no type in the compilation.
    /// </returns>
    public AIContextNode? Resolve(AIContextRef reference)
    {
        if (reference.Path.Length > 0) return Entry(reference.Path);

        // 只带名字的引用按类型全名反查一次：基类型与 typeof 形式的 [SlotSelectors] 都是这样 ——
        // 它们的条目可能落在另一个程序集的分片里，生成期算不出路径，只能在这里查。
        var path = PathFor(reference.DeclaredName);
        return path is null ? null : Entry(path);
    }
}
