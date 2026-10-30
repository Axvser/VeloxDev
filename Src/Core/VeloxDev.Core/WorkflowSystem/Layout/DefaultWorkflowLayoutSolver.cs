namespace VeloxDev.WorkflowSystem;

/// <summary>The default layout solution: snapshot, solve through the injected pipeline, apply once.</summary>
/// <remarks>
/// Replace the planner and/or the cost to customise the algorithm, or implement
/// <see cref="IWorkflowLayoutSolver"/> directly to replace the whole composition. The solver does not own
/// the start node or the editor policy; those live in <see cref="WorkflowLayoutOptions"/> and on the helper.
/// </remarks>
public sealed class DefaultWorkflowLayoutSolver : IWorkflowLayoutSolver
{
    private readonly IWorkflowLayoutPlanner? _planner;
    private readonly IWorkflowLayoutCost? _cost;

    /// <summary>Creates the default solver.</summary>
    /// <param name="planner">Stage composition; <see cref="DefaultWorkflowLayoutPlanner"/> when omitted.</param>
    /// <param name="cost">Cost measure; <see cref="DefaultWorkflowLayoutCost"/> when omitted.</param>
    public DefaultWorkflowLayoutSolver(
        IWorkflowLayoutPlanner? planner = null, IWorkflowLayoutCost? cost = null)
    {
        _planner = planner;
        _cost = cost;
    }

    /// <inheritdoc />
    public WorkflowLayoutResult Arrange(IWorkflowTreeViewModel tree, WorkflowLayoutOptions options)
    {
        if (tree is null) throw new ArgumentNullException(nameof(tree));
        options ??= new WorkflowLayoutOptions();

        var problem = WorkflowLayoutProblemBuilder.Build(tree, options);
        var pipeline = new WorkflowLayoutPipeline(_planner, _cost);
        var solution = pipeline.Solve(problem, options);
        return new WorkflowLayoutApplier(tree).Apply(problem, solution);
    }
}
