namespace VeloxDev.AI;

/// <summary>
/// What a context tree node stands for.
/// </summary>
public enum AIContextNodeKind : byte
{
    /// <summary>A directory: a node whose children are other nodes, not facts about a type.</summary>
    Directory = 0,

    /// <summary>A type entry whose members are enumerated as <see cref="EnumMember"/>.</summary>
    EnumType = 1,

    /// <summary>A type entry whose members are properties, commands and methods.</summary>
    InterfaceType = 2,

    /// <summary>A type entry for a component — the shape <c>GetClassContext</c> renders.</summary>
    ComponentType = 3,

    /// <summary>A type entry rendered as data rather than as a component.</summary>
    DataType = 4,

    /// <summary>A property of a type entry.</summary>
    Property = 5,

    /// <summary>A field of a type entry.</summary>
    Field = 6,

    /// <summary>An <c>ICommand</c> property the Agent may execute.</summary>
    Command = 7,

    /// <summary>A method of a type entry.</summary>
    Method = 8,

    /// <summary>A parameter of a method or command.</summary>
    Parameter = 9,

    /// <summary>One member of an enum type.</summary>
    EnumMember = 10,
}

/// <summary>
/// Facts about a node that callers would otherwise have to reflect for.
/// </summary>
[Flags]
public enum AIContextFlags
{
    /// <summary>Nothing recorded.</summary>
    None = 0,

    /// <summary>The member can be read.</summary>
    CanRead = 1,

    /// <summary>The member can be written.</summary>
    CanWrite = 2,

    /// <summary>The method is static.</summary>
    Static = 4,

    /// <summary>The member is an <c>ICommand</c>.</summary>
    IsCommand = 8,

    /// <summary>The member is a slot enumerator — its port count follows its selector.</summary>
    IsSlotEnumerator = 16,

    /// <summary>The member is a single slot rather than a collection of them.</summary>
    IsSingleSlot = 32,

    /// <summary>The member carries <c>[SlotSelectors]</c>, with or without arguments.</summary>
    HasSlotSelectors = 64,

    /// <summary>The member stands for a <c>[VeloxProperty]</c> field rather than a declared property.</summary>
    IsPromotedField = 128,

    /// <summary>The member carries <c>[VeloxProperty]</c>.</summary>
    HasVeloxProperty = 256,

    /// <summary>The member carries <c>[VeloxCommand]</c>.</summary>
    HasVeloxCommand = 512,

    /// <summary>The entry describes a framework type rather than one from the consuming assembly.</summary>
    IsFramework = 1024,

    /// <summary>A parameter that may be omitted by the caller.</summary>
    Optional = 2048,

    /// <summary>The entry describes a value type rather than a reference type.</summary>
    IsValueType = 4096,

    /// <summary>The member is a collection of slots rather than one slot.</summary>
    IsSlotCollection = 8192,
}

/// <summary>
/// Why one node points at another.
/// </summary>
public enum AIContextRefKind : byte
{
    /// <summary>A type named by <c>[SlotSelectors]</c>.</summary>
    SlotSelectorType = 0,

    /// <summary>A type named by <c>[AgentCommandParameter]</c>.</summary>
    CommandParameterType = 1,

    /// <summary>The declared type of a property, field, parameter or return value.</summary>
    MemberType = 2,

    /// <summary>An interface this type implements or inherits.</summary>
    BaseInterface = 3,

    /// <summary>A type argument, such as the <c>T</c> of a slot enumerator.</summary>
    GenericArgument = 4,

    /// <summary>
    /// The entry's base type, so a caller can walk the inheritance chain.
    /// </summary>
    /// <remarks>
    /// Present because the reflection path enumerates inherited members too — a derived component's rendering
    /// shows the properties it gets from its base. A renderer that only read an entry's own members would lose
    /// them, so the chain has to be walkable from the tree.
    /// </remarks>
    BaseType = 5,
}

/// <summary>
/// A cross-link from one node to another, resolved on demand rather than at load.
/// </summary>
/// <remarks>
/// <para>
/// A reference is a kind, a path and the name as written. It deliberately carries no resolved node and no
/// resolver delegate: a delegate would be a managed pointer into generated code, and holding one would make
/// trimming depend on delegate construction rather than on reachable code.
/// </para>
/// <para>
/// <see cref="Path"/> is the canonical lookup key, and is empty when the named type is not one the tree knows —
/// <c>[SlotSelectors]</c> accepts names that do not exist in the compilation. Callers fall back to
/// <see cref="DeclaredName"/> in that case, which is what keeps a name-only annotation reportable.
/// </para>
/// </remarks>
public readonly struct AIContextRef
{
    /// <summary>
    /// Creates a reference.
    /// </summary>
    /// <param name="kind">Why the node points at the target.</param>
    /// <param name="path">The target's path in the tree, or an empty string when it has none.</param>
    /// <param name="declaredName">The target's name exactly as the source wrote it.</param>
    public AIContextRef(AIContextRefKind kind, string path, string declaredName)
    {
        Kind = kind;
        Path = path;
        DeclaredName = declaredName;
    }

    /// <summary>Why the node points at the target.</summary>
    public AIContextRefKind Kind { get; }

    /// <summary>The target's path in the tree, or an empty string when the target is not in the tree.</summary>
    public string Path { get; }

    /// <summary>The target's name as written, kept for the case where <see cref="Path"/> is empty.</summary>
    public string DeclaredName { get; }
}

/// <summary>
/// One node of the agent context tree — a directory, a type entry, or a member of one.
/// </summary>
/// <remarks>
/// <para>
/// A node holds data only: strings, enums and numbers. It carries no <see cref="Type"/>, no
/// <see cref="System.Reflection.MemberInfo"/> and no delegate, which is what lets the whole tree survive
/// trimming untouched. Type identity belongs to <see cref="IAIContextAccessor"/>, and cross-type links are
/// <see cref="AIContextRef"/> paths resolved through <see cref="AIContextTreeRegistry"/>.
/// </para>
/// <para>
/// Nodes are built lazily, one directory at a time, by code the generator emits. Nothing walks into a child
/// directory in order to build a parent, and resolving a <see cref="AIContextRef"/> is always a separate,
/// explicit call — that is what makes retrieval incremental.
/// </para>
/// </remarks>
public sealed class AIContextNode
{
    /// <summary>
    /// Creates a node.
    /// </summary>
    /// <param name="name">This node's own path segment.</param>
    /// <param name="kind">What the node stands for.</param>
    /// <param name="typeName">The declared type as a string, or <see langword="null"/> for a directory.</param>
    /// <param name="ownerTypeName">The full name of the type that declares this member, or <see langword="null"/>.</param>
    /// <param name="flags">Facts callers would otherwise reflect for.</param>
    /// <param name="ordinal">An enum member's value, or a stable index.</param>
    /// <param name="descriptions">The <c>[AgentContext]</c> texts, or <see langword="null"/> for none.</param>
    /// <param name="references">Cross-links to other nodes, or <see langword="null"/> for none.</param>
    /// <param name="children">Child nodes, or <see langword="null"/> for none.</param>
    public AIContextNode(
        string name,
        AIContextNodeKind kind,
        string? typeName = null,
        string? ownerTypeName = null,
        AIContextFlags flags = AIContextFlags.None,
        long ordinal = 0,
        AgentText[]? descriptions = null,
        AIContextRef[]? references = null,
        AIContextNode[]? children = null)
    {
        Name = name;
        Kind = kind;
        TypeName = typeName;
        OwnerTypeName = ownerTypeName;
        Flags = flags;
        Ordinal = ordinal;
        Descriptions = descriptions ?? [];
        References = references ?? [];
        Children = children ?? [];
    }

    /// <summary>This node's own path segment.</summary>
    public string Name { get; }

    /// <summary>What the node stands for.</summary>
    public AIContextNodeKind Kind { get; }

    /// <summary>The declared type as a string — never a <see cref="Type"/>. <see langword="null"/> for directories.</summary>
    public string? TypeName { get; }

    /// <summary>The full name of the type declaring this member, or <see langword="null"/> for directories and type entries.</summary>
    public string? OwnerTypeName { get; }

    /// <summary>Facts callers would otherwise reflect for.</summary>
    public AIContextFlags Flags { get; }

    /// <summary>An enum member's underlying value, or a stable index.</summary>
    public long Ordinal { get; }

    /// <summary>The <c>[AgentContext]</c> texts, in the order they were written. Empty when the member has none.</summary>
    public AgentText[] Descriptions { get; }

    /// <summary>Cross-links to other nodes. Never expanded at load.</summary>
    public AIContextRef[] References { get; }

    /// <summary>Child nodes. Empty for leaves.</summary>
    public AIContextNode[] Children { get; }

    /// <summary>Whether <paramref name="flag"/> is set.</summary>
    /// <param name="flag">The flag to test — pass a single flag, not a mask.</param>
    /// <returns><see langword="true"/> when the flag is set.</returns>
    public bool Has(AIContextFlags flag) => (Flags & flag) == flag;
}
