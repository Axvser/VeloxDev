namespace VeloxDev.WorkflowSystem;

/// <summary>Where one node ended up, compared with where the snapshot found it.</summary>
public readonly struct WorkflowLayoutPlacement
{
    /// <summary>Creates a placement.</summary>
    /// <param name="index">The node index in the problem.</param>
    /// <param name="originalX">The snapshot's world-space left edge.</param>
    /// <param name="originalY">The snapshot's world-space top edge.</param>
    /// <param name="x">The solved world-space left edge.</param>
    /// <param name="y">The solved world-space top edge.</param>
    /// <param name="width">The world-space width.</param>
    /// <param name="height">The world-space height.</param>
    /// <param name="isPinned">Whether the node is pinned.</param>
    public WorkflowLayoutPlacement(
        int index, double originalX, double originalY, double x, double y,
        double width, double height, bool isPinned)
    {
        Index = index;
        OriginalX = originalX;
        OriginalY = originalY;
        X = x;
        Y = y;
        Width = width;
        Height = height;
        IsPinned = isPinned;
    }

    /// <summary>The node index in the problem.</summary>
    public int Index { get; }

    /// <summary>The snapshot's world-space left edge.</summary>
    public double OriginalX { get; }

    /// <summary>The snapshot's world-space top edge.</summary>
    public double OriginalY { get; }

    /// <summary>The solved world-space left edge.</summary>
    public double X { get; }

    /// <summary>The solved world-space top edge.</summary>
    public double Y { get; }

    /// <summary>The world-space width.</summary>
    public double Width { get; }

    /// <summary>The world-space height.</summary>
    public double Height { get; }

    /// <summary>Whether the node is pinned.</summary>
    public bool IsPinned { get; }

    /// <summary>The horizontal displacement the solution asks for.</summary>
    public double DeltaX => X - OriginalX;

    /// <summary>The vertical displacement the solution asks for.</summary>
    public double DeltaY => Y - OriginalY;

    /// <summary>Whether the solution moves the node.</summary>
    public bool Moved => X != OriginalX || Y != OriginalY;
}

/// <summary>The cost a solution pays, decomposed so a planner can compare alternatives.</summary>
/// <remarks>
/// Crossings are measured on <b>solved</b> geometry, never on a curve the view published before the solve:
/// each edge becomes the straight segment between its nodes' solved centres, and a shared endpoint does not
/// count. <see cref="CrossingsApproximate"/> therefore records that the count describes the cost model's
/// centre-line approximation, not the eventual drawn curve. Crossings remain diagnostic only in the first
/// pipeline version: no position decision reads them.
/// </remarks>
public readonly struct WorkflowLayoutCostBreakdown
{
    /// <summary>Whether overlaps and crossings were measured; false when disabled by the options.</summary>
    public bool CostMeasured { get; init; }

    /// <summary>The number of crossing pairs of links, measured on solved centre lines.</summary>
    public int Crossings { get; init; }

    /// <summary>Whether crossings were estimated from solved centre lines rather than published view curves.</summary>
    public bool CrossingsApproximate { get; init; }

    /// <summary>The number of node pairs whose rectangles overlap with positive area.</summary>
    public int Overlaps { get; init; }

    /// <summary>The total overlapping area, in squared world units.</summary>
    public double OverlapArea { get; init; }

    /// <summary>The sum of the Euclidean displacement of every moved node.</summary>
    public double Displacement { get; init; }

    /// <summary>The largest Euclidean displacement of any node.</summary>
    public double MaxDisplacement { get; init; }

    /// <summary>The union of the solved node rectangles.</summary>
    public WorkflowBounds Bounds { get; init; }
}

/// <summary>The data computed from a problem: final positions, cost and diagnostics.</summary>
/// <remarks>
/// A solution owns no model reference beyond <see cref="WorkflowLayoutProblem.Sources"/>; it is safe to inspect
/// or discard without touching the tree. <see cref="IWorkflowLayoutApplier"/> is the only component that writes
/// it back.
/// </remarks>
public sealed class WorkflowLayoutSolution
{
    /// <summary>Creates a solution.</summary>
    /// <param name="problem">The snapshot that was solved.</param>
    /// <param name="options">The options used for the solve.</param>
    /// <param name="placements">One placement per problem node.</param>
    /// <param name="cost">The measured cost.</param>
    /// <param name="diagnostics">Human-readable notes about the solve.</param>
    /// <exception cref="ArgumentNullException">Any reference argument is <see langword="null"/>.</exception>
    public WorkflowLayoutSolution(
        WorkflowLayoutProblem problem,
        WorkflowLayoutOptions options,
        IReadOnlyList<WorkflowLayoutPlacement> placements,
        WorkflowLayoutCostBreakdown cost,
        IReadOnlyList<string> diagnostics)
    {
        Problem = problem ?? throw new ArgumentNullException(nameof(problem));
        Options = options ?? throw new ArgumentNullException(nameof(options));
        Placements = placements ?? throw new ArgumentNullException(nameof(placements));
        Cost = cost;
        Diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
    }

    /// <summary>The snapshot that was solved.</summary>
    public WorkflowLayoutProblem Problem { get; }

    /// <summary>The options used for the solve.</summary>
    public WorkflowLayoutOptions Options { get; }

    /// <summary>One placement per problem node, in problem order.</summary>
    public IReadOnlyList<WorkflowLayoutPlacement> Placements { get; }

    /// <summary>The measured cost of the solution.</summary>
    public WorkflowLayoutCostBreakdown Cost { get; }

    /// <summary>Human-readable notes about the solve.</summary>
    public IReadOnlyList<string> Diagnostics { get; }

    /// <summary>Whether the problem contains no nodes.</summary>
    public bool IsEmpty => Placements.Count == 0;

    /// <summary>Whether at least one placement asks to move its node.</summary>
    public bool HasMoves
    {
        get
        {
            for (var i = 0; i < Placements.Count; i++)
            {
                if (Placements[i].Moved) return true;
            }
            return false;
        }
    }
}
