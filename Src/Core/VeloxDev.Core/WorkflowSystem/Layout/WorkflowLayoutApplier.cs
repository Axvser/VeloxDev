using VeloxDev.WorkflowSystem.StandardEx;

namespace VeloxDev.WorkflowSystem;

/// <summary>Writes a solved layout back to the tree as one undoable action.</summary>
/// <remarks>
/// <para>Pinned and unmoved nodes are skipped. Each changed node receives its solved world anchor through
/// <see cref="WorkflowNodeEx.StandardSetAnchor"/>, preserving the node layer, and all writes are wrapped in
/// a single <see cref="IWorkflowActionPair"/> so one <c>Undo</c> restores every original anchor.</para>
/// <para>The tree is marked dirty once, after the submit, rather than once per node.</para>
/// </remarks>
public sealed class WorkflowLayoutApplier : IWorkflowLayoutApplier
{
    private const double Epsilon = 1e-6d;
    private readonly IWorkflowTreeViewModel _tree;

    /// <summary>Creates an applier for <paramref name="tree"/>.</summary>
    /// <param name="tree">The tree that owns the snapshot nodes.</param>
    /// <exception cref="ArgumentNullException"><paramref name="tree"/> is <see langword="null"/>.</exception>
    public WorkflowLayoutApplier(IWorkflowTreeViewModel tree)
    {
        _tree = tree ?? throw new ArgumentNullException(nameof(tree));
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentNullException"><paramref name="problem"/> or <paramref name="solution"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="solution"/> does not belong to <paramref name="problem"/>.</exception>
    public WorkflowLayoutResult Apply(WorkflowLayoutProblem problem, WorkflowLayoutSolution solution)
    {
        if (problem is null) throw new ArgumentNullException(nameof(problem));
        if (solution is null) throw new ArgumentNullException(nameof(solution));
        if (!ReferenceEquals(solution.Problem, problem))
        {
            throw new ArgumentException("The solution was produced for a different problem.", nameof(solution));
        }

        if (solution.Options.DryRun || problem.Nodes.Count == 0)
        {
            return new WorkflowLayoutResult(solution, applied: false, appliedCount: 0, dryRun: solution.Options.DryRun);
        }

        var moves = new List<NodeMove>();
        var staleSkipped = 0;
        foreach (var placement in solution.Placements)
        {
            var node = problem.Nodes[placement.Index];
            if (node.IsPinned || !placement.Moved) continue;

            var source = problem.Sources[placement.Index];

            // 解是对快照求的；快照之后模型被别人改过就不要拿旧 From 覆盖。父树变了同理。
            if (!IsStillAtSnapshot(problem, placement.Index))
            {
                staleSkipped++;
                continue;
            }

            moves.Add(new NodeMove(
                source,
                new Anchor(node.X, node.Y, node.Layer),
                new Anchor(placement.X, placement.Y, node.Layer)));
        }

        if (moves.Count == 0)
        {
            return new WorkflowLayoutResult(
                solution, applied: false, appliedCount: 0, dryRun: false, skippedCount: staleSkipped);
        }

        var helper = _tree.GetHelper();
        helper.Submit(new WorkflowActionPair(
            () =>
            {
                foreach (var move in moves) move.Node.StandardSetAnchor(move.To);
            },
            () =>
            {
                foreach (var move in moves) move.Node.StandardSetAnchor(move.From);
            }));
        helper.MarkDirty();

        return new WorkflowLayoutResult(
            solution, applied: true, appliedCount: moves.Count, dryRun: false, skippedCount: staleSkipped);
    }

    private bool IsStillAtSnapshot(WorkflowLayoutProblem problem, int index)
    {
        var source = problem.Sources[index];
        if (!ReferenceEquals(source.Parent, _tree)) return false;

        var node = problem.Nodes[index];
        var anchor = source.Anchor;
        if (anchor.Layer != node.Layer) return false;

        // The getter is collapsed by the live scale, so recover the world value before comparing it with
        // the snapshot (the snapshot stores world coordinates).
        var scale = source.Parent?.Layout?.Scale ?? _tree.Layout?.Scale;
        var scaleX = scale is null || scale.Horizontal == 0d ? 1d : scale.Horizontal;
        var scaleY = scale is null || scale.Vertical == 0d ? 1d : scale.Vertical;
        var x = anchor.Horizontal * scaleX;
        var y = anchor.Vertical * scaleY;

        return Math.Abs(x - node.X) <= Epsilon && Math.Abs(y - node.Y) <= Epsilon;
    }

    private readonly struct NodeMove
    {
        public NodeMove(IWorkflowNodeViewModel node, Anchor from, Anchor to)
        {
            Node = node;
            From = from;
            To = to;
        }

        public IWorkflowNodeViewModel Node { get; }
        public Anchor From { get; }
        public Anchor To { get; }
    }
}
