namespace VeloxDev.WorkflowSystem;

/// <summary>
/// The mutable workspace a layout pipeline hands from stage to stage: current positions plus the arrays
/// later stages (layering, ordering, routing) will fill in.
/// </summary>
/// <remarks>
/// <para><see cref="Problem"/> and <see cref="Options"/> are read-only to stages. The four position arrays
/// are the only state a stage may write, and each stage documents which of them it owns.</para>
/// <para>The arrays are indexed exactly like <see cref="WorkflowLayoutProblem.Nodes"/>. A stage that writes
/// positions must not write the entries of pinned nodes; the pipeline re-asserts pinned positions after the
/// last stage regardless.</para>
/// </remarks>
public sealed class WorkflowLayoutState
{
    /// <summary>Creates a state positioned at the snapshot's original rectangles.</summary>
    /// <param name="problem">The snapshot being solved.</param>
    /// <param name="options">The options that were used to build it.</param>
    /// <exception cref="ArgumentNullException">Either argument is <see langword="null"/>.</exception>
    public WorkflowLayoutState(WorkflowLayoutProblem problem, WorkflowLayoutOptions options)
    {
        Problem = problem ?? throw new ArgumentNullException(nameof(problem));
        Options = options ?? throw new ArgumentNullException(nameof(options));

        var count = problem.Nodes.Count;
        X = new double[count];
        Y = new double[count];
        Rank = new int[count];
        Order = new int[count];
        RouteX = new double[problem.Edges.Count];
        RouteY = new double[problem.Edges.Count];

        for (var i = 0; i < count; i++)
        {
            var node = problem.Nodes[i];
            X[i] = node.X;
            Y[i] = node.Y;
            Rank[i] = -1;
            Order[i] = -1;
        }

        for (var i = 0; i < RouteX.Length; i++)
        {
            RouteX[i] = double.NaN;
            RouteY[i] = double.NaN;
        }
    }

    /// <summary>The immutable snapshot the state belongs to.</summary>
    public WorkflowLayoutProblem Problem { get; }

    /// <summary>The options that were used to build the snapshot.</summary>
    public WorkflowLayoutOptions Options { get; }

    /// <summary>The current world-space left edges.</summary>
    public double[] X { get; }

    /// <summary>The current world-space top edges.</summary>
    public double[] Y { get; }

    /// <summary>Reserved for a layering stage: the rank of each node.</summary>
    public int[] Rank { get; }

    /// <summary>Reserved for an ordering stage: the position of each node inside its rank.</summary>
    public int[] Order { get; }

    /// <summary>Reserved for a routing stage: one X per edge.</summary>
    public double[] RouteX { get; }

    /// <summary>Reserved for a routing stage: one Y per edge.</summary>
    public double[] RouteY { get; }

    /// <summary>Whether the node at <paramref name="index"/> is pinned.</summary>
    /// <param name="index">The node index.</param>
    public bool IsPinned(int index) => Problem.Nodes[index].IsPinned;

    /// <summary>Whether the node at <paramref name="index"/> has moved away from its snapshot position.</summary>
    /// <param name="index">The node index.</param>
    public bool HasMoved(int index)
    {
        var node = Problem.Nodes[index];
        return X[index] != node.X || Y[index] != node.Y;
    }

    /// <summary>Restores every position to the snapshot's original rectangle.</summary>
    public void ResetToOriginal()
    {
        for (var i = 0; i < Problem.Nodes.Count; i++)
        {
            var node = Problem.Nodes[i];
            X[i] = node.X;
            Y[i] = node.Y;
        }
    }
}
