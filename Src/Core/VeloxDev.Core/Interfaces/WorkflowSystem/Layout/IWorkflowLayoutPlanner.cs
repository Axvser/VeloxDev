namespace VeloxDev.WorkflowSystem;

/// <summary>Composes the ordered stages a pipeline runs for a given problem and option set.</summary>
/// <remarks>
/// This is the seam for replacing a whole layout strategy. The first pipeline version returns a single
/// separation stage; a Sugiyama planner would return cycle breaking, layering, ordering, coordinate
/// assignment, separation and routing stages instead — without changing the pipeline.
/// </remarks>
public interface IWorkflowLayoutPlanner
{
    /// <summary>Returns the stages to run, in order.</summary>
    /// <param name="problem">The snapshot about to be solved.</param>
    /// <param name="options">The options for this solve.</param>
    IReadOnlyList<IWorkflowLayoutStage> CreateStages(
        WorkflowLayoutProblem problem, WorkflowLayoutOptions options);
}
