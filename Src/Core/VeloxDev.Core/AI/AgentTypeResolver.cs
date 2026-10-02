namespace VeloxDev.AI;

/// <summary>
/// Resolves .NET types by full name from the compiled context tree.
/// </summary>
/// <remarks>
/// <para>
/// A name is resolved by finding the accessor registered for it, which is also what roots the type for the
/// trimmer. There is deliberately no assembly scan behind this: <c>Assembly.GetType</c> is exactly the operation
/// that cannot be made trim-safe, and an assembly scan answers with types whose members nothing has guaranteed
/// are still there.
/// </para>
/// <para>
/// The consequence is a closed world — a type the tree does not carry does not resolve. That is the same trade
/// the rest of the agent surface makes, and it is what makes the answer trustworthy under NativeAOT.
/// </para>
/// </remarks>
public static class AgentTypeResolver
{
    /// <summary>
    /// Resolves a <see cref="Type"/> by its full name.
    /// </summary>
    /// <param name="fullTypeName">The type's full name, as <see cref="Type.FullName"/> reports it.</param>
    /// <returns>The resolved type, or <see langword="null"/> when the tree carries no such type.</returns>
    public static Type? ResolveType(string fullTypeName)
    {
        if (string.IsNullOrWhiteSpace(fullTypeName)) return null;

        return AIContextTreeRegistry.FindAccessor(fullTypeName)?.TargetType;
    }
}
