namespace VeloxDev.WorkflowSystem;

/// <summary>The outcome of applying a solution to a tree.</summary>
public sealed class WorkflowLayoutResult
{
    /// <summary>Creates a result.</summary>
    /// <param name="solution">The solution that was applied or skipped.</param>
    /// <param name="applied">Whether any anchor was written.</param>
    /// <param name="appliedCount">The number of nodes written.</param>
    /// <param name="dryRun">Whether the apply was skipped because the solution was a dry run.</param>
    /// <param name="skippedCount">
    /// The number of moved nodes skipped because their live anchor, layer or parent no longer matched the
    /// snapshot.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="solution"/> is <see langword="null"/>.</exception>
    public WorkflowLayoutResult(
        WorkflowLayoutSolution solution, bool applied, int appliedCount, bool dryRun, int skippedCount = 0)
    {
        Solution = solution ?? throw new ArgumentNullException(nameof(solution));
        Applied = applied;
        AppliedCount = appliedCount;
        DryRun = dryRun;
        SkippedCount = skippedCount;
    }

    /// <summary>The solution that was applied or skipped.</summary>
    public WorkflowLayoutSolution Solution { get; }

    /// <summary>Whether any anchor was written.</summary>
    public bool Applied { get; }

    /// <summary>The number of nodes written.</summary>
    public int AppliedCount { get; }

    /// <summary>Whether the apply was skipped because the solution was a dry run.</summary>
    public bool DryRun { get; }

    /// <summary>
    /// The number of moved nodes skipped because the live model no longer matched the snapshot.
    /// </summary>
    /// <remarks>
    /// A skip protects a concurrent edit made between <c>Solve</c> and <c>Apply</c>: the applier will not
    /// overwrite an anchor that has already changed, nor write a node that has left the tree.
    /// </remarks>
    public int SkippedCount { get; }
}
