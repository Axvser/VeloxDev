namespace VeloxDev.WorkflowSystem.CSharp;

internal static class CSharpObjectMemberDiscoveryTool
{
    internal static CSharpObjectMembers Discover(
        Type hostType,
        IEnumerable<ICSharpObjectMemberProvider> providers)
    {
        foreach (var provider in providers)
        {
            if (provider.TryGetMembers(hostType, out var members)
                && members is not null)
            {
                return members;
            }
        }

        new ReflectionCSharpObjectMemberProvider()
            .TryGetMembers(hostType, out var fallback);
        return fallback;
    }
}
