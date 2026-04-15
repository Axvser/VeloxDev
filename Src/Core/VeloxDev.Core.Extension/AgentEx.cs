using VeloxDev.WorkflowSystem;

namespace VeloxDev.AI.Workflow;

public static class AgentEx
{
    public static WorkflowAgentScope AsAgentScope(this IWorkflowTreeViewModel tree)
        => new(tree);
}
