namespace VeloxDev.WorkflowSystem;

/// <summary>One node of a layout problem: its world rectangle, layer and pin state.</summary>
/// <remarks>
/// The rectangle is in world coordinates, not the collapsed space the node getter exposes. A stage must treat
/// this as an immutable value; positions being solved live in <see cref="WorkflowLayoutState"/>.
/// </remarks>
public readonly struct WorkflowLayoutNode
{
    /// <summary>Creates a snapshot node.</summary>
    /// <param name="index">The node's position in the problem's node list.</param>
    /// <param name="x">The world-space left edge.</param>
    /// <param name="y">The world-space top edge.</param>
    /// <param name="width">The world-space width.</param>
    /// <param name="height">The world-space height.</param>
    /// <param name="layer">The node's layer, preserved when the result is applied.</param>
    /// <param name="isPinned">Whether the node is pinned and must never move.</param>
    public WorkflowLayoutNode(int index, double x, double y, double width, double height, int layer, bool isPinned)
    {
        Index = index;
        X = x;
        Y = y;
        Width = width;
        Height = height;
        Layer = layer;
        IsPinned = isPinned;
    }

    /// <summary>The node's position in the problem's node list.</summary>
    public int Index { get; }

    /// <summary>The world-space left edge.</summary>
    public double X { get; }

    /// <summary>The world-space top edge.</summary>
    public double Y { get; }

    /// <summary>The world-space width.</summary>
    public double Width { get; }

    /// <summary>The world-space height.</summary>
    public double Height { get; }

    /// <summary>The node's layer.</summary>
    public int Layer { get; }

    /// <summary>Whether the node is pinned and must never move.</summary>
    public bool IsPinned { get; }

    /// <summary>The world-space right edge, <see cref="X"/> plus <see cref="Width"/>.</summary>
    public double Right => X + Width;

    /// <summary>The world-space bottom edge, <see cref="Y"/> plus <see cref="Height"/>.</summary>
    public double Bottom => Y + Height;

    /// <summary>The world-space centre along the horizontal axis.</summary>
    public double CentreX => X + (Width * 0.5d);

    /// <summary>The world-space centre along the vertical axis.</summary>
    public double CentreY => Y + (Height * 0.5d);
}

/// <summary>One directed link of a layout problem, expressed as two node indices.</summary>
/// <remarks>
/// The edge carries indices only. The snapshot deliberately keeps no link reference: a stage or cost must
/// never reach back into the live model to read geometry it did not snapshot itself.
/// </remarks>
public readonly struct WorkflowLayoutEdge
{
    /// <summary>Creates a snapshot edge.</summary>
    /// <param name="sender">The index of the node that owns the sender slot.</param>
    /// <param name="receiver">The index of the node that owns the receiver slot.</param>
    public WorkflowLayoutEdge(int sender, int receiver)
    {
        Sender = sender;
        Receiver = receiver;
    }

    /// <summary>The index of the node that owns the sender slot.</summary>
    public int Sender { get; }

    /// <summary>The index of the node that owns the receiver slot.</summary>
    public int Receiver { get; }
}

/// <summary>
/// An immutable snapshot of the nodes, links and constraints a layout pipeline works on.
/// </summary>
/// <remarks>
/// <para>The snapshot is built on the calling thread and then handed to stages that may run on worker
/// threads. Stages read <see cref="Nodes"/> and <see cref="Edges"/> only; <see cref="Sources"/> exists for the
/// applier and must never be consulted while solving.</para>
/// <para>Nodes whose world geometry is not finite (a <c>NaN</c>/<c>Infinity</c> anchor or size, typically a
/// node the view has not measured yet) are omitted together with their edges. Their positions in the
/// original node enumeration are recorded in <see cref="SkippedIndices"/>; <see cref="Sources"/> stays
/// position-for-position aligned with <see cref="Nodes"/> so applier indices remain valid.</para>
/// <para><see cref="ContentBounds"/> is the union of the node rectangles — deliberately not the bounds the
/// tree's spatial index reports, which also cover links.</para>
/// </remarks>
public sealed class WorkflowLayoutProblem
{
    /// <summary>Creates a problem from already-snapshotted data.</summary>
    /// <param name="nodes">The node rectangles, in world space.</param>
    /// <param name="edges">The directed links between those nodes.</param>
    /// <param name="sources">The live nodes matching <paramref name="nodes"/>, position for position.</param>
    /// <param name="skippedIndices">
    /// Indices in the original node enumeration that were omitted because their geometry was not finite.
    /// </param>
    /// <exception cref="ArgumentNullException">Any argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The source list does not match the node list in length.</exception>
    public WorkflowLayoutProblem(
        IReadOnlyList<WorkflowLayoutNode> nodes,
        IReadOnlyList<WorkflowLayoutEdge> edges,
        IReadOnlyList<IWorkflowNodeViewModel> sources,
        IReadOnlyList<int>? skippedIndices = null)
    {
        if (nodes is null) throw new ArgumentNullException(nameof(nodes));
        if (edges is null) throw new ArgumentNullException(nameof(edges));
        if (sources is null) throw new ArgumentNullException(nameof(sources));
        if (sources.Count != nodes.Count)
        {
            throw new ArgumentException("Every snapshot node needs exactly one source.", nameof(sources));
        }

        var nodeArray = new WorkflowLayoutNode[nodes.Count];
        for (var i = 0; i < nodes.Count; i++)
        {
            nodeArray[i] = nodes[i];
        }

        var edgeArray = new WorkflowLayoutEdge[edges.Count];
        for (var i = 0; i < edges.Count; i++)
        {
            edgeArray[i] = edges[i];
        }

        var sourceArray = new IWorkflowNodeViewModel[sources.Count];
        for (var i = 0; i < sources.Count; i++)
        {
            sourceArray[i] = sources[i];
        }

        var skipped = skippedIndices ?? Array.Empty<int>();
        var skippedArray = new int[skipped.Count];
        for (var i = 0; i < skipped.Count; i++)
        {
            skippedArray[i] = skipped[i];
        }

        Nodes = nodeArray;
        Edges = edgeArray;
        Sources = sourceArray;
        SkippedIndices = skippedArray;
        ContentBounds = ComputeContentBounds(nodeArray);
    }

    /// <summary>The node rectangles, in world space.</summary>
    public IReadOnlyList<WorkflowLayoutNode> Nodes { get; }

    /// <summary>The directed links between the snapshot nodes.</summary>
    public IReadOnlyList<WorkflowLayoutEdge> Edges { get; }

    /// <summary>The live nodes, position for position with <see cref="Nodes"/>; for the applier only.</summary>
    public IReadOnlyList<IWorkflowNodeViewModel> Sources { get; }

    /// <summary>Indices in the original node enumeration that were skipped for non-finite geometry.</summary>
    public IReadOnlyList<int> SkippedIndices { get; }

    /// <summary>Whether any node was omitted because its geometry was not finite.</summary>
    public bool HasSkippedNodes => SkippedIndices.Count > 0;

    /// <summary>The union of the node rectangles, in world space.</summary>
    public WorkflowBounds ContentBounds { get; }

    /// <summary>Whether the problem contains no nodes.</summary>
    public bool IsEmpty => Nodes.Count == 0;

    private static WorkflowBounds ComputeContentBounds(IReadOnlyList<WorkflowLayoutNode> nodes)
    {
        if (nodes.Count == 0) return default;

        var left = double.MaxValue;
        var top = double.MaxValue;
        var right = double.MinValue;
        var bottom = double.MinValue;
        foreach (var node in nodes)
        {
            if (node.X < left) left = node.X;
            if (node.Y < top) top = node.Y;
            if (node.Right > right) right = node.Right;
            if (node.Bottom > bottom) bottom = node.Bottom;
        }

        return new WorkflowBounds { Left = left, Top = top, Width = right - left, Height = bottom - top };
    }
}
