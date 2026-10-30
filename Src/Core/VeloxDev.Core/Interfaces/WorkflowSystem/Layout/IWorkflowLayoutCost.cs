namespace VeloxDev.WorkflowSystem;

/// <summary>Measures the quality of a solved layout.</summary>
/// <remarks>
/// <para>The measure must be a pure function of the snapshot and the solved arrays: it may not read a live
/// node, view or link, because the pipeline promises that solving never touches the model. Crossings are
/// constructed from the solved rectangles (the default implementation uses the segment between node
/// centres) and marked approximate in the breakdown.</para>
/// <para>Overlap and crossing counts should use a temporary tile index; only displacement and content bounds
/// are expected to be linear without one.</para>
/// </remarks>
public interface IWorkflowLayoutCost
{
    /// <summary>Measures <paramref name="state"/> against its problem.</summary>
    /// <param name="problem">The snapshot that was solved.</param>
    /// <param name="state">The solved workspace.</param>
    /// <param name="context">Cancellation and observers.</param>
    /// <returns>The decomposed cost.</returns>
    WorkflowLayoutCostBreakdown Measure(
        WorkflowLayoutProblem problem, WorkflowLayoutState state, WorkflowLayoutContext context);
}
