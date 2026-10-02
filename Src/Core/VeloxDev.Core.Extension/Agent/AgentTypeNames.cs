namespace VeloxDev.AI;

/// <summary>
/// Derives the short spellings the agent tools put in their JSON from the full names the context tree carries.
/// </summary>
/// <remarks>
/// The tree stores declared types the way <see cref="System.Type.FullName"/> reports them, so a tool that used
/// to print <c>node.GetType().Name</c> has to shorten the name itself rather than asking the runtime. These are
/// labels — nothing routes on them — so the rule is deliberately simple: the last segment, without the generic
/// arity that <see cref="System.Type.Name"/> would have kept.
/// </remarks>
internal static class AgentTypeNames
{
    /// <summary>
    /// The short name of a type given its full name.
    /// </summary>
    /// <param name="fullName">A full name such as <c>VeloxDev.WorkflowSystem.NodeDefaultViewModel</c>.</param>
    /// <returns>The last segment, or an empty string when there is no name to shorten.</returns>
    internal static string Simple(string? fullName)
    {
        if (string.IsNullOrEmpty(fullName)) return string.Empty;

        var end = fullName!.Length;
        var angle = fullName.IndexOf('<');
        if (angle >= 0) end = angle;

        var start = 0;
        for (var i = end - 1; i >= 0; i--)
        {
            if (fullName[i] == '.' || fullName[i] == '+')
            {
                start = i + 1;
                break;
            }
        }

        return fullName.Substring(start, end - start);
    }

    /// <summary>The short name of an object's type, or an empty string when its type is not in the tree.</summary>
    internal static string SimpleOf(object? target)
        => Simple(AIContextTreeRegistry.FindAccessor(target)?.TypeName);
}
