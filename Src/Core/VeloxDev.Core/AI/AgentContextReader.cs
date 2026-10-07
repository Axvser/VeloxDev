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
    /// <para>
    /// The member's own name and declaring type are read straight off the <see cref="MemberInfo"/> the caller
    /// already holds — reading those does not query metadata, which is the part that trimming removes.
    /// </para>
    /// <para>
    /// A <c>[VeloxProperty]</c> field is stored under the property name it is promoted to, so a
    /// <see cref="FieldInfo"/> is matched against the field-backed entries the tree carries rather than against
    /// its own name. The entry belongs to the <b>field</b>: the property the generator writes from it never
    /// carried the attribute, and asking that property for its annotation therefore answers with nothing.
    /// </para>
    /// </remarks>
    public static AIContextNode? EntryFor(MemberInfo member)
    {
        if (member?.DeclaringType?.FullName is not { } declaringType) return null;

        var byName = AIContextDirectory.Shared.Member(declaringType, member.Name);

        if (member is FieldInfo field)
            return byName ?? PromotedEntryFor(declaringType, field);

        // 提升条目属于**那个字段**，不属于生成出来的属性 —— `[VeloxProperty]` 从来没写在属性上，
        // 所以问一个属性「你自己的标注是什么」，答案是没有。不加这一条，同一个条目会被字段与属性
        // 各认领一次，而两条路都算「有描述」，于是每一行都变成两行。
        if (member is PropertyInfo && byName is not null && IsFieldBacked(byName)) return null;

        return byName;
    }

    /// <summary>Whether the entry stands for a field rather than for the member whose name it carries.</summary>
    private static bool IsFieldBacked(AIContextNode node)
        => node.Kind == AIContextNodeKind.Field
           || (node.Kind == AIContextNodeKind.Property && node.Has(AIContextFlags.IsPromotedField));

    /// <summary>The entry a field's annotation was recorded under, found by the name the tree gave it.</summary>
    private static AIContextNode? PromotedEntryFor(string declaringType, FieldInfo field)
    {
        AIContextNode? match = null;
        var declared = field.Name.TrimStart('_');

        foreach (var directory in FieldDirectories)
        {
            foreach (var candidate in AIContextDirectory.Shared.MembersAcross(declaringType, directory))
            {
                if (!IsFieldBacked(candidate)) continue;

                if (!string.Equals(candidate.Name.TrimStart('_'), declared, StringComparison.OrdinalIgnoreCase))
                    continue;

                // 同名两个条目（`volume` 与 `_volume` 会提升成同一个名字）时宁可不答，也不猜一个。
                if (match is not null) return null;
                match = candidate;
            }
        }

        return match;
    }

    /// <summary>The directories a field-backed member can live under.</summary>
    private static readonly string[] FieldDirectories = ["Properties", "Fields"];
}
