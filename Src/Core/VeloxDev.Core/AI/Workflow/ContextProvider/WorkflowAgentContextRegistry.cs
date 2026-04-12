using System.Reflection;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.AI.Workflow;

public static class WorkflowAgentContextRegistry
{
    private static readonly object SyncRoot = new();
    private static readonly HashSet<Type> RegisteredTypes = [];

    public static void RegisterWorkflowAgentContext(this Type type)
    {
        if (type is null)
        {
            throw new ArgumentNullException(nameof(type));
        }

        lock (SyncRoot)
        {
            RegisteredTypes.Add(type);
        }
    }

    public static void RegisterWorkflowAgentContexts(this IEnumerable<Type> types)
    {
        if (types is null)
        {
            throw new ArgumentNullException(nameof(types));
        }

        foreach (var type in types)
        {
            type.RegisterWorkflowAgentContext();
        }
    }

    public static void RegisterWorkflowAgentContexts(this Assembly assembly)
    {
        if (assembly is null)
        {
            throw new ArgumentNullException(nameof(assembly));
        }

        assembly
            .GetLoadableTypes()
            .Where(IsWorkflowAgentContextCandidate)
            .RegisterWorkflowAgentContexts();
    }

    public static bool UnregisterWorkflowAgentContext(this Type type)
    {
        if (type is null)
        {
            throw new ArgumentNullException(nameof(type));
        }

        lock (SyncRoot)
        {
            return RegisteredTypes.Remove(type);
        }
    }

    public static void ClearWorkflowAgentContexts()
    {
        lock (SyncRoot)
        {
            RegisteredTypes.Clear();
        }
    }

    public static IReadOnlyList<Type> GetRegisteredWorkflowAgentContextTypes()
    {
        lock (SyncRoot)
        {
            return [.. RegisteredTypes.OrderBy(static type => type.FullName, StringComparer.Ordinal)];
        }
    }

    internal static IEnumerable<Type> GetLoadableTypes(this Assembly assembly)
    {
        try
        {
            return assembly.GetTypes().Where(static type => type is not null)!;
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(static type => type is not null)!;
        }
    }

    private static bool IsWorkflowAgentContextCandidate(Type type)
    {
        if (type.IsGenericTypeDefinition)
        {
            return false;
        }

        return typeof(IWorkflowTreeViewModel).IsAssignableFrom(type)
               || typeof(IWorkflowNodeViewModel).IsAssignableFrom(type)
               || typeof(IWorkflowSlotViewModel).IsAssignableFrom(type)
               || typeof(IWorkflowLinkViewModel).IsAssignableFrom(type)
               || type.GetCustomAttributes(typeof(AgentContextAttribute), false).Length > 0
               || type.GetMembers(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                   .Any(static member => member.GetCustomAttributes(typeof(AgentContextAttribute), false).Length > 0);
    }
}
