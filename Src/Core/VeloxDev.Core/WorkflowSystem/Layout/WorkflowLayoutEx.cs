namespace VeloxDev.WorkflowSystem;

/// <summary>One-call entry points for layout: the whole tree, or one node's reachable subgraph.</summary>
/// <remarks>
/// <para>The node overload is the primary entry point: it finds the tree through <see cref="IWorkflowNodeViewModel.Parent"/>
/// and asks that tree's helper for the registered <see cref="IWorkflowLayoutSolver"/>. That keeps custom
/// solutions per editor: replacing the solver on one tree's helper never affects another tree.</para>
/// <para>The tree overload uses the same helper, so a host only needs one registration point. If a helper does
/// not implement <see cref="IWorkflowLayoutHost"/>, the default solver is used as a fallback.</para>
/// <para><see cref="WorkflowLayoutOptions.DryRun"/> is honoured by the applier, so a dry run returns a normal
/// result with <see cref="WorkflowLayoutResult.Applied"/> set to <see langword="false"/>.</para>
/// </remarks>
public static class WorkflowLayoutEx
{
    /// <summary>Arranges the whole of <paramref name="tree"/> through its helper's solver.</summary>
    /// <param name="tree">The tree to arrange.</param>
    /// <param name="options">Gaps, tolerance and pinning; a default set when omitted.</param>
    /// <returns>The apply result for the solved layout.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="tree"/> is <see langword="null"/>.</exception>
    public static WorkflowLayoutResult Arrange(
        this IWorkflowTreeViewModel tree, WorkflowLayoutOptions? options = null)
    {
        if (tree is null) throw new ArgumentNullException(nameof(tree));

        var helper = tree.GetHelper();
        if (helper is IWorkflowLayoutHost host)
        {
            return host.Arrange(start: null, options);
        }

        return new DefaultWorkflowLayoutSolver().Arrange(tree, options ?? new WorkflowLayoutOptions());
    }

    /// <summary>Arranges the subgraph reachable from <paramref name="node"/> through its tree's helper.</summary>
    /// <param name="node">The layout start node; it must currently belong to a tree.</param>
    /// <param name="options">Gaps, tolerance and traversal; a default set when omitted.</param>
    /// <returns>The apply result for the solved layout.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="node"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="node"/> is not attached to a tree.</exception>
    /// <remarks>
    /// The node becomes <see cref="WorkflowLayoutOptions.StartNode"/>; pass <see langword="false"/> in
    /// <see cref="WorkflowLayoutOptions.PinStartNode"/> when the start node should be allowed to move too.
    /// </remarks>
    public static WorkflowLayoutResult Arrange(
        this IWorkflowNodeViewModel node, WorkflowLayoutOptions? options = null)
    {
        if (node is null) throw new ArgumentNullException(nameof(node));

        var tree = node.Parent
            ?? throw new ArgumentException(
                "A node can only be laid out while it belongs to a tree.", nameof(node));

        options ??= new WorkflowLayoutOptions();
        options.StartNode = node;

        var helper = tree.GetHelper();
        if (helper is IWorkflowLayoutHost host)
        {
            return host.Arrange(node, options);
        }

        return new DefaultWorkflowLayoutSolver().Arrange(tree, options);
    }
}
