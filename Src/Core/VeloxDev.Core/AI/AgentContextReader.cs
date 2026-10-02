using System.Reflection;

namespace VeloxDev.AI;

/// <summary>
/// Reads <c>[AgentContext]</c> descriptions for types and members out of the compiled context tree.
/// </summary>
/// <remarks>
/// <para>
/// The annotations are not read here — they were read at compile time, by the generator that built the tree.
/// This type is the lookup: a type or member in, the descriptions worth showing for a language out. Nothing
/// below reflects over metadata, which is what lets it survive trimming and NativeAOT.
/// </para>
/// <para>
/// A type or member with no entry answers with nothing. That is the contract now: the tree is the only source,
/// so a type whose assembly was never compiled with the generator is not described. See
/// <see cref="AIContextDirectory"/> for the paths that answer that question directly.
/// </para>
/// </remarks>
public static class AgentContextReader
{
    /// <summary>
    /// Gets the descriptions a language should see for a type.
    /// </summary>
    /// <param name="type">The type.</param>
    /// <param name="language">The requested language.</param>
    /// <returns>
    /// The matching descriptions in order; the English ones when none match and <paramref name="language"/> is not
    /// English; an empty array when the type has no entry or no English either.
    /// </returns>
    public static string[] GetContexts(Type type, AgentLanguages language)
    {
        if (type is null) return [];

        return AgentTextSelection.Select(EntryFor(type)?.Descriptions, language);
    }

    /// <summary>
    /// Gets the descriptions a language should see for a member — a property, field, method or event.
    /// </summary>
    /// <param name="member">The member.</param>
    /// <param name="language">The requested language.</param>
    /// <returns>The descriptions, with the same fallback <see cref="GetContexts(Type, AgentLanguages)"/> applies.</returns>
    public static string[] GetContexts(MemberInfo member, AgentLanguages language)
        => AgentTextSelection.Select(EntryFor(member)?.Descriptions, language);

    /// <summary>
    /// Returns <c>true</c> when the type or member carries at least one description, in any language.
    /// </summary>
    /// <param name="member">The member.</param>
    /// <returns><see langword="true"/> when it has one.</returns>
    public static bool HasAgentContext(MemberInfo member)
        => EntryFor(member)?.Descriptions.Length > 0;

    /// <summary>
    /// The tree entry for a type, or null when the tree has none.
    /// </summary>
    /// <param name="type">The type.</param>
    /// <returns>The entry, or <see langword="null"/>.</returns>
    public static AIContextNode? EntryFor(Type type)
    {
        if (type?.FullName is not { } fullName) return null;

        var path = AIContextDirectory.Shared.PathFor(fullName);
        return path is null ? null : AIContextDirectory.Shared.Entry(path);
    }

    /// <summary>
    /// The tree entry for a member, found through the type that declares it.
    /// </summary>
    /// <param name="member">The member.</param>
    /// <returns>The entry, or <see langword="null"/> when the declaring type or the member itself has none.</returns>
    /// <remarks>
    /// The member's own name and declaring type are read straight off the <see cref="MemberInfo"/> the caller
    /// already holds — reading those does not query metadata, which is the part that trimming removes.
    /// </remarks>
    public static AIContextNode? EntryFor(MemberInfo member)
    {
        if (member?.DeclaringType?.FullName is not { } declaringType) return null;

        return AIContextDirectory.Shared.Member(declaringType, member.Name);
    }
}
