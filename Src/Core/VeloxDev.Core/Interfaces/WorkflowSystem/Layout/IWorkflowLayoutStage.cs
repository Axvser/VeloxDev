namespace VeloxDev.WorkflowSystem;

/// <summary>
/// One replaceable step of a layout pipeline, such as layering, ordering, coordinate assignment or separation.
/// </summary>
/// <remarks>
/// A stage reads <see cref="WorkflowLayoutState.Problem"/> and writes only the arrays it owns in
/// <see cref="WorkflowLayoutState"/>. Stages are composed by <see cref="IWorkflowLayoutPlanner"/>; adding a
/// new phase to the pipeline means implementing this interface and returning it from a planner, not editing
/// an existing stage.
/// </remarks>
public interface IWorkflowLayoutStage
{
    /// <summary>A stable name used in diagnostics.</summary>
    string Name { get; }

    /// <summary>Runs the stage against <paramref name="state"/>.</summary>
    /// <param name="state">The mutable workspace shared by the pipeline.</param>
    /// <param name="context">Cancellation, parallelism and observers.</param>
    void Execute(WorkflowLayoutState state, WorkflowLayoutContext context);
}
