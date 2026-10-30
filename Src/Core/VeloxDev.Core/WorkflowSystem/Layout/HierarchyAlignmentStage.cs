namespace VeloxDev.WorkflowSystem;

/// <summary>
/// The default canonical alignment: fan-out groups, fan-in merges and single-parent children are placed on
/// their parent hierarchy's centre line.
/// </summary>
/// <remarks>
/// <para>A parent with two or more children stacks them in one column to its right, centred on its vertical
/// centre, with <see cref="WorkflowLayoutOptions.RowGap"/> between them. A node with exactly one parent is
/// centred on that parent and placed one column to its right; a node with two or more parents is centred on
/// their bounding box and placed to the right of the rightmost parent. The pass runs in topological order, so
/// a parent is final before its children are aligned.</para>
/// <para>Fan-out wins over the single-parent and fan-in rules: a node that belongs to a two-or-more-child
/// group is not re-centred by its own parents. A fan-out group containing a pinned child is skipped
/// entirely.</para>
/// <para><see cref="WorkflowLayoutOptions.PositionTolerance"/> zero always writes the canonical position; a
/// positive value keeps an existing placement that is within that distance on each axis. Pinned nodes are
/// never written.</para>
/// </remarks>
public sealed class HierarchyAlignmentStage : IWorkflowLayoutStage
{
    private const double Epsilon = 1e-6d;

    /// <inheritdoc />
    public string Name => "hierarchy-alignment";

    /// <inheritdoc />
    public void Execute(WorkflowLayoutState state, WorkflowLayoutContext context)
    {
        if (state is null) throw new ArgumentNullException(nameof(state));
        if (context is null) throw new ArgumentNullException(nameof(context));

        var problem = state.Problem;
        if (problem.Nodes.Count < 2 || problem.Edges.Count == 0) return;

        var options = state.Options;
        var rowGap = Math.Max(0d, options.RowGap);
        var columnGap = Math.Max(0d, options.ColumnGap);
        var tolerance = Math.Max(0d, options.PositionTolerance);

        var children = CollectNeighbours(problem, senderSide: true);
        var parents = CollectNeighbours(problem, senderSide: false);

        // 属于「父节点有 2 个以上子节点」的节点由扇出规则定位，不再按自己的父节点重新居中。
        var fanOutChildren = new HashSet<int>();
        foreach (var group in children.Values)
        {
            if (group.Count < 2) continue;
            foreach (var child in group) fanOutChildren.Add(child);
        }

        var order = TopologicalOrder(problem);
        var aligned = 0;
        var groups = 0;
        var placed = 0;

        foreach (var node in order)
        {
            context.CancellationToken.ThrowIfCancellationRequested();

            if (!fanOutChildren.Contains(node))
            {
                aligned += AlignToParents(state, node, parents, columnGap, tolerance);
            }

            if (children.TryGetValue(node, out var group) && group.Count >= 2)
            {
                groups++;
                placed += PlaceFanOutGroup(state, node, group, rowGap, columnGap, tolerance);
            }
        }

        context.Report("layout.alignedNodes", aligned);
        context.Report("layout.fanOutGroups", groups);
        context.Report("layout.fanOutPlaced", placed);
    }

    private static int AlignToParents(
        WorkflowLayoutState state,
        int node,
        Dictionary<int, List<int>> parents,
        double columnGap,
        double tolerance)
    {
        if (!parents.TryGetValue(node, out var group) || group.Count == 0) return 0;

        var problem = state.Problem;
        var nodeInfo = problem.Nodes[node];
        if (nodeInfo.IsPinned) return 0;

        double centre;
        double right;
        if (group.Count == 1)
        {
            var parent = problem.Nodes[group[0]];
            centre = state.Y[group[0]] + (parent.Height * 0.5d);
            right = state.X[group[0]] + parent.Width;
        }
        else
        {
            // 多个父节点取整体包围盒中心，而不是各自中心的平均：高度不一时前者才是视觉中心线。
            var top = double.PositiveInfinity;
            var bottom = double.NegativeInfinity;
            right = double.NegativeInfinity;
            foreach (var parentIndex in group)
            {
                var parent = problem.Nodes[parentIndex];
                if (state.Y[parentIndex] < top) top = state.Y[parentIndex];
                var parentBottom = state.Y[parentIndex] + parent.Height;
                if (parentBottom > bottom) bottom = parentBottom;

                var parentRight = state.X[parentIndex] + parent.Width;
                if (parentRight > right) right = parentRight;
            }

            centre = (top + bottom) * 0.5d;
        }

        var moved = false;

        // 容差按轴判断：某一轴已经足够接近就不动那一轴，另一轴仍可对齐。
        var currentCentre = state.Y[node] + (nodeInfo.Height * 0.5d);
        if (tolerance <= 0d || Math.Abs(currentCentre - centre) > tolerance)
        {
            state.Y[node] = centre - (nodeInfo.Height * 0.5d);
            moved = true;
        }

        var targetX = right + columnGap;
        if (tolerance <= 0d || Math.Abs(state.X[node] - targetX) > tolerance)
        {
            state.X[node] = targetX;
            moved = true;
        }

        return moved ? 1 : 0;
    }

    private static int PlaceFanOutGroup(
        WorkflowLayoutState state,
        int parent,
        List<int> group,
        double rowGap,
        double columnGap,
        double tolerance)
    {
        var problem = state.Problem;

        // 组里有钉住节点就凑不出规范中心；整组交给分离段，而不是围着固定点挪其余子节点。
        if (group.Any(child => problem.Nodes[child].IsPinned)) return 0;

        var parentNode = problem.Nodes[parent];
        var targetX = state.X[parent] + parentNode.Width + columnGap;
        var totalHeight = rowGap * (group.Count - 1);
        foreach (var child in group) totalHeight += problem.Nodes[child].Height;

        var firstY = state.Y[parent] + (parentNode.Height * 0.5d) - (totalHeight * 0.5d);
        if (tolerance > 0d
            && IsWithinTolerance(state, parent, group, targetX, firstY, rowGap, columnGap, tolerance))
        {
            return 0;
        }

        var cursor = firstY;
        foreach (var child in group)
        {
            state.X[child] = targetX;
            state.Y[child] = cursor;
            cursor += problem.Nodes[child].Height + rowGap;
        }

        return 1;
    }

    private static bool IsWithinTolerance(
        WorkflowLayoutState state, int parent, List<int> group,
        double targetX, double firstY, double rowGap, double columnGap, double tolerance)
    {
        var problem = state.Problem;
        var cursor = firstY;

        for (var i = 0; i < group.Count; i++)
        {
            var child = group[i];
            if (Math.Abs(state.X[child] - targetX) > tolerance) return false;
            if (Math.Abs(state.Y[child] - cursor) > tolerance) return false;

            if (i > 0)
            {
                var previous = group[i - 1];
                var gap = state.Y[child] - (state.Y[previous] + problem.Nodes[previous].Height);
                if (gap < rowGap - Epsilon) return false;
            }

            cursor += problem.Nodes[child].Height + rowGap;
        }

        // 子列离父右边缘仍须留够列距，否则算硬冲突，不能当作「容差内」放过。
        foreach (var child in group)
        {
            var gap = state.X[child] - (state.X[parent] + problem.Nodes[parent].Width);
            if (gap < columnGap - Epsilon) return false;
        }

        return true;
    }

    private static Dictionary<int, List<int>> CollectNeighbours(WorkflowLayoutProblem problem, bool senderSide)
    {
        var map = new Dictionary<int, List<int>>();
        foreach (var edge in problem.Edges)
        {
            var key = senderSide ? edge.Sender : edge.Receiver;
            var neighbour = senderSide ? edge.Receiver : edge.Sender;
            if (!map.TryGetValue(key, out var list))
            {
                list = [];
                map[key] = list;
            }

            if (!list.Contains(neighbour)) list.Add(neighbour);
        }

        return map;
    }

    private static int[] TopologicalOrder(WorkflowLayoutProblem problem)
    {
        var count = problem.Nodes.Count;
        var indegree = new int[count];
        var outgoing = new List<int>[count];
        for (var i = 0; i < count; i++) outgoing[i] = [];

        foreach (var edge in problem.Edges)
        {
            indegree[edge.Receiver]++;
            outgoing[edge.Sender].Add(edge.Receiver);
        }

        var ready = new SortedSet<int>();
        for (var i = 0; i < count; i++)
        {
            if (indegree[i] == 0) ready.Add(i);
        }

        var order = new List<int>(count);
        var visited = new bool[count];
        while (ready.Count > 0)
        {
            var node = ready.Min;
            ready.Remove(node);
            order.Add(node);
            visited[node] = true;

            foreach (var next in outgoing[node])
            {
                if (--indegree[next] == 0) ready.Add(next);
            }
        }

        // 环不属于对齐规则的范围；剩余节点按下标补进，保证每次结果一致。
        for (var i = 0; i < count; i++)
        {
            if (!visited[i]) order.Add(i);
        }

        return order.ToArray();
    }
}
