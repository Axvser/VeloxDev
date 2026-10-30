namespace VeloxDev.WorkflowSystem;

/// <summary>Writes a solution back to the model in one undoable step.</summary>
/// <remarks>
/// <para>Implementations run on the calling thread after the solve: the solve may have happened elsewhere,
/// but the model is only touched here. Every changed anchor is submitted as a single
/// <see cref="IWorkflowActionPair"/>, so one undo restores the whole layout.</para>
/// <para>The solution is only valid for the model state it was solved from. A moved node whose live world
/// anchor, layer or parent no longer matches the snapshot is skipped instead of overwritten; the result
/// reports how many were skipped in <see cref="WorkflowLayoutResult.SkippedCount"/>.</para>
/// </remarks>
public interface IWorkflowLayoutApplier
{
    /// <summary>Applies <paramref name="solution"/> to the tree the problem was built from.</summary>
    /// <param name="problem">The snapshot the solution belongs to.</param>
    /// <param name="solution">The solution to write back.</param>
    /// <returns>The outcome, including how many nodes moved.</returns>
    WorkflowLayoutResult Apply(WorkflowLayoutProblem problem, WorkflowLayoutSolution solution);
}
