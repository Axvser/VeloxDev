namespace VeloxDev.WorkflowSystem;

/// <summary>A complete layout solution that can be registered on one tree's helper.</summary>
/// <remarks>
/// <para>The host owns when a layout runs and on which node; the solver owns how the snapshot becomes
/// positions. A solver receives the whole tree and the options (including
/// <see cref="WorkflowLayoutOptions.StartNode"/> for a scoped run) and returns the apply result.</para>
/// <para>The default implementation is <see cref="DefaultWorkflowLayoutSolver"/>: snapshot, run the injected
/// planner/cost through <see cref="WorkflowLayoutPipeline"/>, then apply once. A custom solution can replace
/// the planner, the stages, the cost, the applier or the whole composition without changing callers.</para>
/// </remarks>
public interface IWorkflowLayoutSolver
{
    /// <summary>Arranges <paramref name="tree"/> according to <paramref name="options"/>.</summary>
    /// <param name="tree">The tree to arrange.</param>
    /// <param name="options">Gaps, tolerance, pinning and the optional start node.</param>
    /// <returns>The apply result for the solved layout.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="tree"/> is <see langword="null"/>.</exception>
    WorkflowLayoutResult Arrange(IWorkflowTreeViewModel tree, WorkflowLayoutOptions options);
}

/// <summary>
/// A tree helper that can host one layout solution per tree (one node editor).
/// </summary>
/// <remarks>
/// <para>The helper is the registration point because its lifetime matches the editor. Register a custom
/// <see cref="IWorkflowLayoutSolver"/> once; every <see cref="WorkflowLayoutEx.Arrange(IWorkflowNodeViewModel, WorkflowLayoutOptions?)"/>
/// call on a node of that tree reaches it through the node's parent.</para>
/// <para>The start node is only a scope: when it is supplied the solver should set
/// <see cref="WorkflowLayoutOptions.StartNode"/> (the helper does this) and leave everything outside its
/// reachable set untouched.</para>
/// </remarks>
public interface IWorkflowLayoutHost
{
    /// <summary>The solution used by this tree; the default when never replaced.</summary>
    IWorkflowLayoutSolver LayoutSolver { get; set; }

    /// <summary>Arranges this tree, optionally scoped to <paramref name="start"/>.</summary>
    /// <param name="start">The node to start from, or <see langword="null"/> for the whole tree.</param>
    /// <param name="options">Layout options; a default set when omitted.</param>
    /// <returns>The apply result.</returns>
    WorkflowLayoutResult Arrange(
        IWorkflowNodeViewModel? start, WorkflowLayoutOptions? options = null);
}
