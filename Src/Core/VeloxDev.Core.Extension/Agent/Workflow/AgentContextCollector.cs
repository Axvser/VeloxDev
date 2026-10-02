using System;
using System.Reflection;
using VeloxDev.AI;

namespace VeloxDev.AI.Workflow;

/// <summary>
/// Renders the prompt blocks that describe a workflow type: an enum's members, an interface's members, a
/// component's members, or a data type's fields.
/// </summary>
/// <remarks>
/// <para>
/// Every block comes from the compiled context tree. There is no reflection behind this and no fallback for a
/// type the tree does not carry: such a type renders as nothing, which is the same closed world the rest of the
/// agent surface lives in. Before the migration each method here had a <c>…ByReflection</c> sibling that read
/// metadata directly; those bodies now live in the test project, where they are the oracle the tree-backed
/// rendering is compared against rather than a shipping path.
/// </para>
/// <para>
/// The shapes are load-bearing — the model reads them — so they are reproduced exactly, including the blank
/// lines and the two different description joinings (the enum table concatenates, every other table joins with
/// <c>"; "</c>).
/// </para>
/// </remarks>
public static class AgentContextCollector
{
    /// <summary>
    /// Gets the <c>[AgentContext]</c> descriptions of a type.
    /// </summary>
    /// <param name="type">The type.</param>
    /// <param name="language">The language to read them in.</param>
    /// <returns>The descriptions, with the tree's language fallback applied.</returns>
    public static string[] GetAgentContext(Type type, AgentLanguages language)
        => AgentContextReader.GetContexts(type, language);

    /// <summary>
    /// Gets the <c>[AgentContext]</c> descriptions of a member.
    /// </summary>
    /// <param name="member">The member.</param>
    /// <param name="language">The language to read them in.</param>
    /// <returns>The descriptions, with the tree's language fallback applied.</returns>
    public static string[] GetAgentContext(MemberInfo member, AgentLanguages language)
        => AgentContextReader.GetContexts(member, language);

    /// <summary>
    /// Renders an enum's context.
    /// </summary>
    /// <param name="enumType">The enum.</param>
    /// <param name="language">The language to render descriptions in.</param>
    /// <returns>The Markdown block, or an empty string when the tree carries no entry for the type.</returns>
    public static string GetEnumContext(Type enumType, AgentLanguages language)
        => AgentContextTreeRenderer.TryEntry(enumType, out var entry)
            ? AgentContextTreeRenderer.Enum(entry, language)
            : string.Empty;

    /// <summary>
    /// Renders an interface's context.
    /// </summary>
    /// <param name="type">The interface.</param>
    /// <param name="language">The language to render descriptions in.</param>
    /// <returns>The Markdown block, or an empty string when the tree carries no entry for the type.</returns>
    /// <exception cref="ArgumentException"><paramref name="type"/> is not an interface.</exception>
    public static string GetInterfaceContext(Type type, AgentLanguages language)
    {
        if (!type.IsInterface)
            throw new ArgumentException("Type must be an interface.", nameof(type));

        return AgentContextTreeRenderer.TryEntry(type, out var entry)
            ? AgentContextTreeRenderer.Interface(entry, language)
            : string.Empty;
    }

    /// <summary>
    /// Renders a component's context.
    /// </summary>
    /// <param name="type">The component type.</param>
    /// <param name="language">The language to render descriptions in.</param>
    /// <returns>The Markdown block, or an empty string when the tree carries no entry for the type.</returns>
    public static string GetClassContext(Type type, AgentLanguages language)
        => AgentContextTreeRenderer.TryEntry(type, out var entry)
            ? AgentContextTreeRenderer.Class(entry, language)
            : string.Empty;

    /// <summary>
    /// Builds a compact context block for a value-object / data type (e.g. Anchor, Size, Offset).
    /// Unlike <see cref="GetClassContext"/>, this method does not look for commands or slot
    /// enumerators — it only surfaces public properties and <c>[AgentContext]</c>-annotated fields so
    /// the Agent understands the data structure without any operational noise.
    /// </summary>
    /// <param name="type">The data type.</param>
    /// <param name="language">The language to render descriptions in.</param>
    /// <returns>The Markdown block, or an empty string when the tree carries no entry for the type.</returns>
    public static string GetDataContext(Type type, AgentLanguages language)
        => AgentContextTreeRenderer.TryEntry(type, out var entry)
            ? AgentContextTreeRenderer.Data(entry, language)
            : string.Empty;
}
