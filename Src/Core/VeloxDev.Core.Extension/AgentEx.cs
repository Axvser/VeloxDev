using VeloxDev.WorkflowSystem;

namespace VeloxDev.AI.Workflow;

/// <summary>Extensions that attach the Agent surface to a workflow tree.</summary>
public static class AgentEx
{
    /// <summary>Creates an Agent scope over <paramref name="tree"/>.</summary>
    public static WorkflowAgentScope AsAgentScope(this IWorkflowTreeViewModel tree)
        => new(tree);
}
