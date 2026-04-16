namespace VeloxDev.AI;

/// <summary>
/// Resolves .NET types by full name across all loaded assemblies.
/// Generic utility — not tied to any specific framework or domain.
/// </summary>
public static class AgentTypeResolver
{
    /// <summary>
    /// Resolves a <see cref="Type"/> by its full name, searching all loaded assemblies
    /// if <see cref="Type.GetType(string)"/> fails.
    /// </summary>
    /// <returns>The resolved type, or <c>null</c> if not found.</returns>
    public static Type? ResolveType(string fullTypeName)
    {
        if (string.IsNullOrWhiteSpace(fullTypeName)) return null;

        var type = Type.GetType(fullTypeName);
        if (type != null) return type;

        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                type = asm.GetType(fullTypeName, throwOnError: false);
                if (type != null) return type;
            }
            catch { /* skip unloadable assemblies */ }
        }

        return null;
    }
}
