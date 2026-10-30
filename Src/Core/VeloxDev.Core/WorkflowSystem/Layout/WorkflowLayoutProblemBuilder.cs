using VeloxDev.WorkflowSystem.StandardEx;

namespace VeloxDev.WorkflowSystem;

/// <summary>Builds an immutable <see cref="WorkflowLayoutProblem"/> from a live tree.</summary>
/// <remarks>
/// <para>This is the only component in the layout pipeline that reads the model during the snapshot phase.
/// It runs on the calling thread and converts the collapsed geometry the node getters expose back to world
/// coordinates (<c>world = collapsed × scale</c>), because the applier writes world coordinates through
/// <c>StandardSetAnchor</c>.</para>
/// <para>A node participates in at most one snapshot entry; links whose endpoints are not in the tree are
/// ignored. Self-links are not layout input and are dropped.</para>
/// <para>Nodes whose world geometry is <c>NaN</c> or infinite — an unmeasured or programmatically created
/// node — are omitted together with their edges. Their original indices are recorded on the problem; the
/// remaining <see cref="WorkflowLayoutProblem.Sources"/> stay aligned with
/// <see cref="WorkflowLayoutProblem.Nodes"/>.</para>
/// </remarks>
public static class WorkflowLayoutProblemBuilder
{
    /// <summary>Snapshots <paramref name="tree"/> into a layout problem.</summary>
    /// <param name="tree">The tree to snapshot.</param>
    /// <param name="options">The options whose pinned predicate is evaluated once per node.</param>
    /// <returns>The immutable snapshot.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="tree"/> is <see langword="null"/>.</exception>
    public static WorkflowLayoutProblem Build(
        IWorkflowTreeViewModel tree, WorkflowLayoutOptions? options = null)
    {
        if (tree is null) throw new ArgumentNullException(nameof(tree));

        options ??= new WorkflowLayoutOptions();

        // collapapsed = world / scale, so world = collapsed * scale. Scale 0 is the identity, matching
        // Anchor.Collapse / Size.Collapse.
        var scale = tree.Layout?.Scale;
        var scaleX = scale is null || scale.Horizontal == 0d ? 1d : scale.Horizontal;
        var scaleY = scale is null || scale.Vertical == 0d ? 1d : scale.Vertical;

        var nodes = new List<WorkflowLayoutNode>();
        var sources = new List<IWorkflowNodeViewModel>();
        var skipped = new List<int>();
        var indexOf = new Dictionary<IWorkflowNodeViewModel, int>(
            WorkflowReferenceEqualityComparer<IWorkflowNodeViewModel>.Instance);
        var links = new List<(int Sender, int Receiver)>();

        if (tree.Nodes is not null)
        {
            var originalIndex = 0;
            foreach (var node in tree.Nodes)
            {
                var sourceIndex = originalIndex++;
                if (node is null) continue;

                var anchor = node.Anchor;
                var size = node.Size;
                var x = anchor.Horizontal * scaleX;
                var y = anchor.Vertical * scaleY;
                var width = size.Width * scaleX;
                var height = size.Height * scaleY;

                // Unmeasured nodes (NaN anchors/sizes) must not poison the tile index or the cost.
                if (!IsFinite(x) || !IsFinite(y) || !IsFinite(width) || !IsFinite(height))
                {
                    skipped.Add(sourceIndex);
                    continue;
                }

                var pinned = options.Pinned?.Invoke(node) ?? false;
                var index = nodes.Count;

                nodes.Add(new WorkflowLayoutNode(index, x, y, width, height, anchor.Layer, pinned));
                sources.Add(node);
                indexOf[node] = index;
            }
        }

        if (tree.Links is not null)
        {
            foreach (var link in tree.Links)
            {
                if (link is null) continue;

                var senderNode = link.Sender?.Parent;
                var receiverNode = link.Receiver?.Parent;
                if (senderNode is null || receiverNode is null) continue;
                if (!indexOf.TryGetValue(senderNode, out var sender)) continue;
                if (!indexOf.TryGetValue(receiverNode, out var receiver)) continue;
                if (sender == receiver) continue;

                links.Add((sender, receiver));
            }
        }

        // 起点把可动集缩到可达子图；集外节点仍留在问题里当钉住障碍，选中节点因此撞不出去。
        HashSet<int>? selected = null;
        var startIndex = -1;
        if (options.StartNode is { } start)
        {
            if (!indexOf.TryGetValue(start, out startIndex))
            {
                throw new ArgumentException(
                    "The layout start node must belong to the tree and have finite world geometry.",
                    nameof(options));
            }

            selected = Traverse(links, startIndex, options.Traversal, options.MaxDepth);
        }

        var finalNodes = new List<WorkflowLayoutNode>(nodes.Count);
        for (var i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];
            var pinned = node.IsPinned
                || (selected is not null && !selected.Contains(i))
                || (selected is not null && options.PinStartNode && i == startIndex);

            finalNodes.Add(new WorkflowLayoutNode(
                node.Index, node.X, node.Y, node.Width, node.Height, node.Layer, pinned));
        }

        var edges = new List<WorkflowLayoutEdge>();
        foreach (var (sender, receiver) in links)
        {
            if (selected is not null && (!selected.Contains(sender) || !selected.Contains(receiver))) continue;
            edges.Add(new WorkflowLayoutEdge(sender, receiver));
        }

        return new WorkflowLayoutProblem(finalNodes, edges, sources, skipped);
    }

    private static HashSet<int> Traverse(
        IReadOnlyList<(int Sender, int Receiver)> links,
        int start,
        WorkflowLayoutTraversal traversal,
        int maxDepth)
    {
        var forward = new Dictionary<int, List<int>>();
        var backward = new Dictionary<int, List<int>>();
        foreach (var (sender, receiver) in links)
        {
            if (!forward.TryGetValue(sender, out var outgoing))
            {
                outgoing = [];
                forward[sender] = outgoing;
            }

            outgoing.Add(receiver);

            if (!backward.TryGetValue(receiver, out var incoming))
            {
                incoming = [];
                backward[receiver] = incoming;
            }

            incoming.Add(sender);
        }

        var depthLimit = Math.Max(0, maxDepth);
        var selected = new HashSet<int> { start };
        var queue = new Queue<(int Node, int Depth)>();
        queue.Enqueue((start, 0));

        while (queue.Count > 0)
        {
            var (node, depth) = queue.Dequeue();
            if (depthLimit > 0 && depth >= depthLimit) continue;

            if (traversal is WorkflowLayoutTraversal.Downstream or WorkflowLayoutTraversal.Both
                && forward.TryGetValue(node, out var outgoing))
            {
                Enqueue(outgoing);
            }

            if (traversal is WorkflowLayoutTraversal.Upstream or WorkflowLayoutTraversal.Both
                && backward.TryGetValue(node, out var incoming))
            {
                Enqueue(incoming);
            }

            void Enqueue(List<int> neighbours)
            {
                foreach (var next in neighbours)
                {
                    if (selected.Add(next)) queue.Enqueue((next, depth + 1));
                }
            }
        }

        return selected;
    }

    private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
}
