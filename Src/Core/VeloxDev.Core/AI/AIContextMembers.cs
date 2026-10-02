namespace VeloxDev.AI;

/// <summary>
/// The lookups the agent helpers share: which type an object is described as, which accessor declares one of its
/// members, and what a node says in a language.
/// </summary>
/// <remarks>
/// <para>
/// Internal on purpose. It exists so <see cref="AgentPropertyAccessor"/>, <see cref="AgentMethodInvoker"/> and
/// <see cref="AgentCommandDiscoverer"/> answer these three questions one way rather than three, and so a caller
/// outside this assembly keeps using those helpers instead of the tree directly.
/// </para>
/// <para>
/// The declaring accessor is not always the target's own: a derived type's entry lists only the members it
/// declares, and an inherited member is acted on by the accessor of the type that declared it. Every member node
/// records that type, so the dispatch is a lookup rather than a guess.
/// </para>
/// </remarks>
internal static class AIContextMembers
{
    /// <summary>The full name the tree knows the object's type by, or <see langword="null"/> when it has no entry.</summary>
    internal static string? TypeNameOf(object? target)
    {
        if (target is null) return null;

        var type = target.GetType();
        var fullName = type.FullName ?? type.Name;
        return AIContextDirectory.Shared.PathFor(fullName) is null ? null : fullName;
    }

    /// <summary>
    /// Finds one member of a type by name, in one member directory, including what the type inherits.
    /// </summary>
    /// <param name="typeFullName">The type's full name.</param>
    /// <param name="memberDirectory">The member directory to search.</param>
    /// <param name="memberName">The member's name.</param>
    /// <returns>The node, or <see langword="null"/> when the type has no such member.</returns>
    internal static AIContextNode? Find(string typeFullName, string memberDirectory, string memberName)
        => AIContextDirectory.Shared.MemberAcross(typeFullName, memberDirectory, memberName);

    /// <summary>
    /// The accessor that acts on a member node.
    /// </summary>
    /// <param name="node">The member.</param>
    /// <returns>The declaring type's accessor, or <see langword="null"/> when nothing registered one.</returns>
    internal static IAIContextAccessor? AccessorFor(AIContextNode node)
        => node.OwnerTypeName is { Length: > 0 } owner ? AIContextTreeRegistry.FindAccessor(owner) : null;

    /// <summary>The descriptions a language should see for a node.</summary>
    internal static string[] DescriptionsFor(AIContextNode node, AgentLanguages language)
        => AgentTextSelection.Select(node.Descriptions, language);

    /// <summary>The name a cross-link carries, or <see langword="null"/> when the node has no such link.</summary>
    internal static string? ReferenceName(AIContextNode node, AIContextRefKind kind)
    {
        foreach (var reference in node.References)
        {
            if (reference.Kind != kind) continue;

            return reference.DeclaredName is { Length: > 0 } name ? name : null;
        }

        return null;
    }
}
