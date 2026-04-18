using System.Reflection;

namespace VeloxDev.AI;

/// <summary>
/// Reads <see cref="AgentContextAttribute"/> from types and members.
/// Generic utility — not tied to any specific framework or domain.
/// </summary>
public static class AgentContextReader
{
    /// <summary>
    /// Gets all <see cref="AgentContextAttribute.Context"/> values for the specified language on a type.
    /// </summary>
    public static string[] GetContexts(Type type, AgentLanguages language)
    {
        return [.. type.GetCustomAttributes<AgentContextAttribute>(inherit: false)
            .Where(c => c.Language == language)
            .Select(c => c.Context)];
    }

    /// <summary>
    /// Gets all <see cref="AgentContextAttribute.Context"/> values for the specified language on a member.
    /// </summary>
    public static string[] GetContexts(MemberInfo member, AgentLanguages language)
    {
        return [.. member.GetCustomAttributes<AgentContextAttribute>(inherit: false)
            .Where(c => c.Language == language)
            .Select(c => c.Context)];
    }

    /// <summary>
    /// Returns <c>true</c> if the type or member has at least one <see cref="AgentContextAttribute"/>.
    /// </summary>
    public static bool HasAgentContext(MemberInfo member)
    {
        return member.GetCustomAttributes<AgentContextAttribute>(inherit: false).Any();
    }
}
