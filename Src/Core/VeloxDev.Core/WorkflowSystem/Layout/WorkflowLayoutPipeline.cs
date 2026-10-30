namespace VeloxDev.WorkflowSystem;

/// <summary>Runs a planner's stages, re-asserts pinned positions, and measures the result.</summary>
/// <remarks>
/// <para>The pipeline owns no model state: it reads the snapshot, runs stages (which may parallelise), and
/// returns data. It never dereferences a live node or link — even the cost measure works from the snapshot
/// and the solved arrays. Applying is a separate, calling-thread step.</para>
/// <para>Pinned positions are copied back from the snapshot after the last stage, so a misbehaving stage
/// cannot move them.</para>
/// </remarks>
public sealed class WorkflowLayoutPipeline
{
    private readonly IWorkflowLayoutPlanner _planner;
    private readonly IWorkflowLayoutCost _cost;

    /// <summary>Creates a pipeline.</summary>
    /// <param name="planner">The stage planner; <see cref="DefaultWorkflowLayoutPlanner"/> when omitted.</param>
    /// <param name="cost">The cost measure; <see cref="DefaultWorkflowLayoutCost"/> when omitted.</param>
    public WorkflowLayoutPipeline(
        IWorkflowLayoutPlanner? planner = null, IWorkflowLayoutCost? cost = null)
    {
        _planner = planner ?? new DefaultWorkflowLayoutPlanner();
        _cost = cost ?? new DefaultWorkflowLayoutCost();
    }

    /// <summary>Solves <paramref name="problem"/>.</summary>
    /// <param name="problem">The immutable snapshot to solve.</param>
    /// <param name="options">The options; a default set when omitted.</param>
    /// <param name="context">Cancellation and observers; a default context when omitted.</param>
    /// <returns>The solved positions, cost and diagnostics. The model is untouched.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="problem"/> is <see langword="null"/>.</exception>
    public WorkflowLayoutSolution Solve(
        WorkflowLayoutProblem problem,
        WorkflowLayoutOptions? options = null,
        WorkflowLayoutContext? context = null)
    {
        if (problem is null) throw new ArgumentNullException(nameof(problem));

        options ??= new WorkflowLayoutOptions();
        context ??= new WorkflowLayoutContext();
        context.ResetDiagnostics();

        var state = new WorkflowLayoutState(problem, options);
        var stages = _planner.CreateStages(problem, options);

        foreach (var stage in stages)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            stage.Execute(state, context);
        }

        RestorePinnedPositions(state);

        var cost = _cost.Measure(problem, state, context);
        var placements = new WorkflowLayoutPlacement[problem.Nodes.Count];
        var moved = 0;
        for (var i = 0; i < placements.Length; i++)
        {
            var node = problem.Nodes[i];
            placements[i] = new WorkflowLayoutPlacement(
                i, node.X, node.Y, state.X[i], state.Y[i], node.Width, node.Height, node.IsPinned);
            if (placements[i].Moved) moved++;
        }

        if (problem.HasSkippedNodes)
        {
            context.ReportDiagnostic(
                $"{problem.SkippedIndices.Count} node(s) were skipped because their world geometry was not finite.");
        }

        if (!cost.CostMeasured)
        {
            context.ReportDiagnostic("Cost measurement was disabled by WorkflowLayoutOptions.MeasureCost.");
        }
        else if (cost.CrossingsApproximate)
        {
            context.ReportDiagnostic(
                "Crossings are approximate: measured on solved centre-line segments, not on view-published curves.");
        }

        var diagnostics = new List<string>(context.CollectedDiagnostics);

        context.Report("layout.nodes", problem.Nodes.Count);
        context.Report("layout.moved", moved);
        context.Report("layout.crossings", cost.Crossings);
        context.Report("layout.overlaps", cost.Overlaps);
        context.Report("layout.displacement", cost.Displacement);

        return new WorkflowLayoutSolution(problem, options, placements, cost, diagnostics);
    }

    private static void RestorePinnedPositions(WorkflowLayoutState state)
    {
        for (var i = 0; i < state.Problem.Nodes.Count; i++)
        {
            if (!state.Problem.Nodes[i].IsPinned) continue;
            var node = state.Problem.Nodes[i];
            state.X[i] = node.X;
            state.Y[i] = node.Y;
        }
    }
}
