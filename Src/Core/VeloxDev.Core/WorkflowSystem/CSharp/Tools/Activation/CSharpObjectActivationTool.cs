namespace VeloxDev.WorkflowSystem.CSharp;

internal static class CSharpObjectActivationTool
{
    internal static object CreateHost(
        Type hostType,
        IEnumerable<ICSharpObjectActivator> activators)
    {
        foreach (var activator in activators)
        {
            if (!activator.TryCreate(hostType, out var host)
                || host is null)
            {
                continue;
            }

            if (!hostType.IsInstanceOfType(host))
            {
                throw new InvalidOperationException(
                    $"Activator returned '{host.GetType().FullName}' for '{hostType.FullName}'.");
            }

            return host;
        }

        return Activator.CreateInstance(hostType)
            ?? throw new InvalidOperationException(
                $"Unable to create host type '{hostType.FullName}'.");
    }
}
