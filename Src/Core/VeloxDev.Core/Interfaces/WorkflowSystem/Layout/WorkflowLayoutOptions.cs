namespace VeloxDev.WorkflowSystem;

/// <summary>Which links a scoped layout follows from <see cref="WorkflowLayoutOptions.StartNode"/>.</summary>
public enum WorkflowLayoutTraversal
{
    /// <summary>Follow sender-to-receiver links only (the usual workflow direction).</summary>
    Downstream,

    /// <summary>Follow receiver-to-sender links only.</summary>
    Upstream,

    /// <summary>Follow links in both directions.</summary>
    Both,
}

/// <summary>Options that shape how a <see cref="WorkflowLayoutProblem"/> is built and projected.</summary>
/// <remarks>
/// The values are read by the problem builder (the pinned predicate) and by layout stages (the gaps, the
/// tile size and the sweep budget). A stage must not read the model to obtain them: everything it needs is
/// carried here or in the immutable snapshot.
/// </remarks>
public sealed class WorkflowLayoutOptions
{
    /// <summary>The edge length, in world units, of one tile in the separation grid.</summary>
    /// <remarks>
    /// The grid is only an acceleration structure: a node belongs to the tile that contains its centre, and
    /// neighbour queries look one tile around it. Changing this value changes performance, not the intended
    /// relationship between nodes.
    /// </remarks>
    public double TileSize { get; set; } = 256d;

    /// <summary>The minimum empty space, in world units, kept between two horizontally adjacent nodes.</summary>
    public double ColumnGap { get; set; } = 64d;

    /// <summary>The minimum empty space, in world units, kept between two vertically adjacent nodes.</summary>
    public double RowGap { get; set; } = 32d;

    /// <summary>The maximum number of vertical-plus-horizontal sweeps a separation stage may run.</summary>
    /// <remarks>A value below one is treated as one sweep.</remarks>
    public int MaxSweeps { get; set; } = 8;

    /// <summary>The number of bands at or above which a stage may run its bands in parallel.</summary>
    /// <remarks>
    /// Bands write disjoint nodes, so the result does not depend on this value. It only decides whether the
    /// work is fanned out to the thread pool.
    /// </remarks>
    public int ParallelThreshold { get; set; } = 64;

    /// <summary>
    /// The per-axis distance, in world units, within which an existing placement is accepted instead of
    /// being aligned to the canonical target.
    /// </summary>
    /// <remarks>
    /// <para><c>0</c> means strict canonical layout: every group is written to the computed target, so the
    /// result does not depend on how the user placed the children. Larger values preserve an existing
    /// placement while every member is within this distance of the canonical position and keeps the
    /// minimum spacing.</para>
    /// <para>Hard overlaps and minimum gaps are always repaired, regardless of this value.</para>
    /// </remarks>
    public double PositionTolerance { get; set; } = 8d;

    /// <summary>
    /// An optional node that scopes the layout to the subgraph reachable from it; <see langword="null"/> means
    /// the whole tree.
    /// </summary>
    /// <remarks>
    /// <para>With a start node, only the reachable nodes are movable. Every other finite node stays in the
    /// problem as a fixed obstacle: it constrains the moved nodes but is never written. The start node itself
    /// is pinned by default (<see cref="PinStartNode"/>), so the arrangement grows away from the node the user
    /// chose instead of translating the whole selected cluster.</para>
    /// <para>The node must belong to the tree and have finite world geometry; otherwise the problem builder
    /// rejects the snapshot.</para>
    /// </remarks>
    public IWorkflowNodeViewModel? StartNode { get; set; }

    /// <summary>Which links the scoped traversal follows; ignored when <see cref="StartNode"/> is null.</summary>
    public WorkflowLayoutTraversal Traversal { get; set; } = WorkflowLayoutTraversal.Downstream;

    /// <summary>
    /// The maximum number of link hops from <see cref="StartNode"/> to include; <c>0</c> means unlimited.
    /// Ignored when <see cref="StartNode"/> is null.
    /// </summary>
    public int MaxDepth { get; set; }

    /// <summary>Whether <see cref="StartNode"/> itself stays fixed; defaults to <see langword="true"/>.</summary>
    public bool PinStartNode { get; set; } = true;

    /// <summary>An optional predicate marking nodes that must never be moved.</summary>
    /// <remarks>
    /// Evaluated once per node, on the calling thread, while the snapshot is built. Pinned nodes are still
    /// obstacles for the nodes around them.
    /// </remarks>
    public Func<IWorkflowNodeViewModel, bool>? Pinned { get; set; }

    /// <summary>Whether solving should stop short of writing the result back to the model.</summary>
    /// <remarks>
    /// The pipeline always solves; this flag is honoured by <see cref="IWorkflowLayoutApplier"/>. A dry run
    /// therefore returns a normal <see cref="WorkflowLayoutResult"/> whose
    /// <see cref="WorkflowLayoutResult.Applied"/> is <see langword="false"/>.
    /// </remarks>
    public bool DryRun { get; set; }

    /// <summary>Whether the default cost measure counts overlaps and crossings.</summary>
    /// <remarks>
    /// Displacement and content bounds are always cheap and always reported. Overlap and crossing counts
    /// scale with the graph, so a host solving a very large graph may turn them off; the breakdown then
    /// reports <see cref="WorkflowLayoutCostBreakdown.CostMeasured"/> as <see langword="false"/>.
    /// </remarks>
    public bool MeasureCost { get; set; } = true;
}
