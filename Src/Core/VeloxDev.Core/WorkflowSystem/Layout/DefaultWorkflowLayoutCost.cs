namespace VeloxDev.WorkflowSystem;

/// <summary>The default cost measure: overlap, displacement, content bounds and solved crossing count.</summary>
/// <remarks>
/// <para>Everything here is a pure function of the snapshot and the solved arrays. Crossings are built from
/// the <b>solved</b> rectangles — each edge is the straight segment between its nodes' solved centres — so a
/// node that moved during the solve changes the count. Published view curves are deliberately not read: they
/// describe the pre-layout drawing and would answer a different question.</para>
/// <para>Overlap and crossing pairs are found through temporary <see cref="LayoutTileIndex"/> instances
/// rather than an all-pairs walk. A very large graph can disable both counts with
/// <see cref="WorkflowLayoutOptions.MeasureCost"/>; displacement and content bounds remain linear and are
/// always reported.</para>
/// </remarks>
public sealed class DefaultWorkflowLayoutCost : IWorkflowLayoutCost
{
    private const double Epsilon = 1e-9d;

    /// <inheritdoc />
    public WorkflowLayoutCostBreakdown Measure(
        WorkflowLayoutProblem problem, WorkflowLayoutState state, WorkflowLayoutContext context)
    {
        if (problem is null) throw new ArgumentNullException(nameof(problem));
        if (state is null) throw new ArgumentNullException(nameof(state));
        if (context is null) throw new ArgumentNullException(nameof(context));

        var count = problem.Nodes.Count;
        var displacement = 0d;
        var maxDisplacement = 0d;

        for (var i = 0; i < count; i++)
        {
            context.CancellationToken.ThrowIfCancellationRequested();

            var node = problem.Nodes[i];
            var dx = state.X[i] - node.X;
            var dy = state.Y[i] - node.Y;
            var distance = Math.Sqrt((dx * dx) + (dy * dy));
            displacement += distance;
            if (distance > maxDisplacement) maxDisplacement = distance;
        }

        var bounds = ComputeBounds(state);
        var measured = state.Options.MeasureCost;
        var overlaps = 0;
        var overlapArea = 0d;
        var crossings = 0;

        if (measured)
        {
            overlaps = CountOverlaps(state, context, out overlapArea);
            crossings = CountCrossings(state, context);
        }

        return new WorkflowLayoutCostBreakdown
        {
            CostMeasured = measured,
            Crossings = crossings,
            CrossingsApproximate = measured && problem.Edges.Count >= 2,
            Overlaps = overlaps,
            OverlapArea = overlapArea,
            Displacement = displacement,
            MaxDisplacement = maxDisplacement,
            Bounds = bounds,
        };
    }

    private static int CountOverlaps(WorkflowLayoutState state, WorkflowLayoutContext context, out double area)
    {
        var problem = state.Problem;
        var index = new LayoutTileIndex(TileSize(state), 0d, 0d);
        for (var i = 0; i < problem.Nodes.Count; i++)
        {
            index.Add(i, Rectangle(state, i));
        }

        var overlaps = 0;
        area = 0d;
        for (var i = 0; i < problem.Nodes.Count; i++)
        {
            context.CancellationToken.ThrowIfCancellationRequested();

            // Any positive overlap shares a cell, so the node's own cells are enough to find its partners.
            foreach (var j in index.QueryHalo(Rectangle(state, i), 0))
            {
                if (j <= i) continue;
                var overlap = IntersectionArea(state, i, j);
                if (overlap <= 0d) continue;
                overlaps++;
                area += overlap;
            }
        }

        return overlaps;
    }

    private static int CountCrossings(WorkflowLayoutState state, WorkflowLayoutContext context)
    {
        var edges = state.Problem.Edges;
        if (edges.Count < 2) return 0;

        var index = new LayoutTileIndex(TileSize(state), 0d, 0d);
        var segments = new Segment[edges.Count];
        for (var i = 0; i < edges.Count; i++)
        {
            var segment = CentreLine(state, edges[i]);
            segments[i] = segment;
            index.Add(i, segment.Bounds);
        }

        var crossings = 0;
        for (var i = 0; i < edges.Count; i++)
        {
            context.CancellationToken.ThrowIfCancellationRequested();

            foreach (var j in index.QueryHalo(segments[i].Bounds, 1))
            {
                if (j <= i) continue;
                if (ShareEndpoint(edges[i], edges[j])) continue;
                if (SegmentsCross(segments[i], segments[j])) crossings++;
            }
        }

        return crossings;
    }

    private static Segment CentreLine(WorkflowLayoutState state, WorkflowLayoutEdge edge)
    {
        var problem = state.Problem;
        var sender = problem.Nodes[edge.Sender];
        var receiver = problem.Nodes[edge.Receiver];

        var ax = state.X[edge.Sender] + (sender.Width * 0.5d);
        var ay = state.Y[edge.Sender] + (sender.Height * 0.5d);
        var bx = state.X[edge.Receiver] + (receiver.Width * 0.5d);
        var by = state.Y[edge.Receiver] + (receiver.Height * 0.5d);

        var left = Math.Min(ax, bx);
        var top = Math.Min(ay, by);
        return new Segment(ax, ay, bx, by, new WorkflowBounds
        {
            Left = left,
            Top = top,
            Width = Math.Abs(bx - ax),
            Height = Math.Abs(by - ay),
        });
    }

    private static WorkflowBounds Rectangle(WorkflowLayoutState state, int index)
    {
        var node = state.Problem.Nodes[index];
        return new WorkflowBounds
        {
            Left = state.X[index],
            Top = state.Y[index],
            Width = node.Width,
            Height = node.Height,
        };
    }

    private static double IntersectionArea(WorkflowLayoutState state, int a, int b)
    {
        var problem = state.Problem;
        var left = Math.Max(state.X[a], state.X[b]);
        var right = Math.Min(state.X[a] + problem.Nodes[a].Width, state.X[b] + problem.Nodes[b].Width);
        if (right <= left) return 0d;

        var top = Math.Max(state.Y[a], state.Y[b]);
        var bottom = Math.Min(state.Y[a] + problem.Nodes[a].Height, state.Y[b] + problem.Nodes[b].Height);
        if (bottom <= top) return 0d;

        return (right - left) * (bottom - top);
    }

    private static WorkflowBounds ComputeBounds(WorkflowLayoutState state)
    {
        var problem = state.Problem;
        if (problem.Nodes.Count == 0) return default;

        var left = double.MaxValue;
        var top = double.MaxValue;
        var right = double.MinValue;
        var bottom = double.MinValue;
        for (var i = 0; i < problem.Nodes.Count; i++)
        {
            var node = problem.Nodes[i];
            if (state.X[i] < left) left = state.X[i];
            if (state.Y[i] < top) top = state.Y[i];
            if (state.X[i] + node.Width > right) right = state.X[i] + node.Width;
            if (state.Y[i] + node.Height > bottom) bottom = state.Y[i] + node.Height;
        }

        return new WorkflowBounds { Left = left, Top = top, Width = right - left, Height = bottom - top };
    }

    private static double TileSize(WorkflowLayoutState state)
        => state.Options.TileSize > 0d ? state.Options.TileSize : 256d;

    private static bool ShareEndpoint(WorkflowLayoutEdge a, WorkflowLayoutEdge b)
        => a.Sender == b.Sender || a.Sender == b.Receiver
           || a.Receiver == b.Sender || a.Receiver == b.Receiver;

    // Proper crossing only: a shared endpoint or a collinear overlap is not counted (links that meet at a
    // port are not crossings).
    private static bool SegmentsCross(Segment a, Segment b)
    {
        var d1 = Orientation(b.Ax, b.Ay, b.Bx, b.By, a.Ax, a.Ay);
        var d2 = Orientation(b.Ax, b.Ay, b.Bx, b.By, a.Bx, a.By);
        var d3 = Orientation(a.Ax, a.Ay, a.Bx, a.By, b.Ax, b.Ay);
        var d4 = Orientation(a.Ax, a.Ay, a.Bx, a.By, b.Bx, b.By);

        return ((d1 > Epsilon && d2 < -Epsilon) || (d1 < -Epsilon && d2 > Epsilon))
            && ((d3 > Epsilon && d4 < -Epsilon) || (d3 < -Epsilon && d4 > Epsilon));
    }

    private static double Orientation(
        double ax, double ay, double bx, double by, double px, double py)
        => ((bx - ax) * (py - ay)) - ((by - ay) * (px - ax));

    private readonly struct Segment
    {
        public Segment(double ax, double ay, double bx, double by, WorkflowBounds bounds)
        {
            Ax = ax;
            Ay = ay;
            Bx = bx;
            By = by;
            Bounds = bounds;
        }

        public double Ax { get; }
        public double Ay { get; }
        public double Bx { get; }
        public double By { get; }
        public WorkflowBounds Bounds { get; }
    }
}
