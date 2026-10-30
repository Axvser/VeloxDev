namespace VeloxDev.WorkflowSystem;

/// <summary>The default planner: hierarchy alignment, then tile-separation; no layering, ordering or routing.</summary>
/// <remarks>
/// Replace the planner to insert phases around the alignment and separation projections. The separation
/// stage takes the position policy by constructor, so a stricter strategy can supply its own without
/// editing the stage.
/// </remarks>
public sealed class DefaultWorkflowLayoutPlanner : IWorkflowLayoutPlanner
{
    private readonly IWorkflowLayoutPositionPolicy _policy;

    /// <summary>Creates a planner.</summary>
    /// <param name="policy">The position policy; <see cref="TolerancePositionPolicy"/> when omitted.</param>
    public DefaultWorkflowLayoutPlanner(IWorkflowLayoutPositionPolicy? policy = null)
    {
        _policy = policy ?? new TolerancePositionPolicy();
    }

    /// <inheritdoc />
    /// <remarks>
    /// Alignment places every node on its parent hierarchy's centre line: a fan-out group becomes one column
    /// to the right of its parent, a fan-in merge is centred on the parents' bounding box, and a single child
    /// is centred on its parent. The separation stage then repairs whatever conflicts remain (crossing
    /// groups, pinned obstacles, frames supplied by another stage).
    /// </remarks>
    public IReadOnlyList<IWorkflowLayoutStage> CreateStages(
        WorkflowLayoutProblem problem, WorkflowLayoutOptions options)
        => [new HierarchyAlignmentStage(), new TileSeparationStage(_policy)];
}
