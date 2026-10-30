namespace VeloxDev.WorkflowSystem;

/// <summary>
/// Removes overlaps and minimum-gap violations column by column and row by row. Canonical placement belongs
/// to another stage; this one only resolves conflicts.
/// </summary>
/// <remarks>
/// <para>Each node belongs to exactly one tile by its centre; a <b>column band</b> is every node whose centre
/// shares a tile column, and a <b>row band</b> shares a tile row. Bands write disjoint nodes, so they are the
/// unit of parallel work and the result does not depend on whether they ran serially or in parallel.</para>
/// <para>Each sweep walks every column band top to bottom and pushes nodes apart, then every row band left to
/// right. A node starts from its current position: this stage invents no alignment target, so a compliant
/// node stays where the user placed it. Neither pass clamps to the original content bounds, so a layout may
/// grow in any direction.</para>
/// <para>Every proposed move on a compliant node goes through the injected
/// <see cref="IWorkflowLayoutPositionPolicy"/>; a node that currently violates the minimum gap moves
/// regardless of the policy's answer. Pinned nodes are never written.</para>
/// </remarks>
public sealed class TileSeparationStage : IWorkflowLayoutStage
{
    private const double Epsilon = 1e-6d;
    private readonly IWorkflowLayoutPositionPolicy _policy;

    /// <summary>Creates the stage.</summary>
    /// <param name="policy">The policy consulted for soft alignment moves.</param>
    public TileSeparationStage(IWorkflowLayoutPositionPolicy? policy = null)
    {
        _policy = policy ?? new TolerancePositionPolicy();
    }

    /// <inheritdoc />
    public string Name => "tile-separation";

    /// <inheritdoc />
    public void Execute(WorkflowLayoutState state, WorkflowLayoutContext context)
    {
        if (state is null) throw new ArgumentNullException(nameof(state));
        if (context is null) throw new ArgumentNullException(nameof(context));

        var problem = state.Problem;
        if (problem.Nodes.Count == 0) return;

        var options = state.Options;
        var rowGap = Math.Max(0d, options.RowGap);
        var columnGap = Math.Max(0d, options.ColumnGap);
        var tileSize = options.TileSize > 0d ? options.TileSize : 256d;
        var tolerance = options.PositionTolerance;
        var bandsPerSweep = Math.Max(1, options.ParallelThreshold);

        var originX = problem.ContentBounds.IsEmpty ? 0d : problem.ContentBounds.Left;
        var originY = problem.ContentBounds.IsEmpty ? 0d : problem.ContentBounds.Top;

        var count = problem.Nodes.Count;
        var previousX = new double[count];
        var previousY = new double[count];
        var maxSweeps = Math.Max(1, options.MaxSweeps);
        var sweeps = 0;

        for (var sweep = 0; sweep < maxSweeps; sweep++)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            Array.Copy(state.X, previousX, count);
            Array.Copy(state.Y, previousY, count);

            var verticalIndex = BuildTileIndex(state, tileSize, originX, originY);
            var verticalBands = BuildBands(state, verticalIndex, vertical: true);
            SweepVertical(state, context, verticalBands, verticalIndex, rowGap, tolerance, bandsPerSweep);

            var horizontalIndex = BuildTileIndex(state, tileSize, originX, originY);
            var horizontalBands = BuildBands(state, horizontalIndex, vertical: false);
            SweepHorizontal(state, context, horizontalBands, horizontalIndex, columnGap, tolerance, bandsPerSweep);

            sweeps = sweep + 1;
            if (MaxDelta(state, previousX, previousY) <= Epsilon) break;
        }

        // 两次扫描都按 band 做硬违规判定，中心既不在同一列带也不在同一行带的两个矩形会被两边一起漏掉。
        // 这里补一遍全局正面积重叠修复；它只处理硬重叠，不碰 tolerance 的软对齐语义。
        ResolveGlobalOverlaps(state, context, rowGap, columnGap, tolerance);

        context.Report("layout.sweeps", sweeps);
    }

    private void SweepVertical(
        WorkflowLayoutState state,
        WorkflowLayoutContext context,
        List<Band> bands,
        LayoutTileIndex tileIndex,
        double rowGap,
        double tolerance,
        int parallelThreshold)
    {
        var problem = state.Problem;

        RunBands(bands, context, parallelThreshold, band =>
        {
            band.Items.Sort((a, b) =>
            {
                var byPosition = state.Y[a].CompareTo(state.Y[b]);
                if (byPosition != 0) return byPosition;
                var byPin = (problem.Nodes[a].IsPinned ? 0 : 1).CompareTo(problem.Nodes[b].IsPinned ? 0 : 1);
                return byPin != 0 ? byPin : a.CompareTo(b);
            });

            var placed = new List<int>(band.Items.Count);
            foreach (var i in band.Items)
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                var node = problem.Nodes[i];
                if (node.IsPinned)
                {
                    placed.Add(i);
                    continue;
                }

                // 本阶段只解冲突；规范位置由 HierarchyAlignmentStage 负责，这里不另造软目标。
                var floor = state.Y[i];

                // Only a neighbour that also shares the horizontal span constrains the vertical axis.
                foreach (var p in placed)
                {
                    if (!HorizontalOverlap(state, i, p)) continue;
                    var required = state.Y[p] + problem.Nodes[p].Height + rowGap;
                    if (required > floor) floor = required;
                }

                var proposed = floor;

                // A pinned node below acts as a ceiling: pull the proposal above it instead of moving the pin.
                var ceiling = double.PositiveInfinity;
                foreach (var p in band.Items)
                {
                    if (p == i || !problem.Nodes[p].IsPinned) continue;
                    if (state.Y[p] < state.Y[i] - Epsilon) continue;
                    if (!HorizontalOverlap(state, i, p)) continue;
                    var candidate = state.Y[p] - rowGap - node.Height;
                    if (candidate < ceiling) ceiling = candidate;
                }

                if (!double.IsPositiveInfinity(ceiling) && proposed > ceiling)
                {
                    proposed = Math.Max(floor, ceiling);
                }

                var hard = HasVerticalViolation(state, i, band.Members, tileIndex, rowGap);
                var decision = new WorkflowLayoutPositionDecision(
                    i, state.X[i], state.Y[i], state.X[i], proposed,
                    node.IsPinned, hard, tolerance, WorkflowLayoutAxis.Vertical);
                if (hard || _policy.ShouldApply(decision))
                {
                    state.Y[i] = proposed;
                }

                placed.Add(i);
            }
        });
    }

    private void SweepHorizontal(
        WorkflowLayoutState state,
        WorkflowLayoutContext context,
        List<Band> bands,
        LayoutTileIndex tileIndex,
        double columnGap,
        double tolerance,
        int parallelThreshold)
    {
        var problem = state.Problem;

        RunBands(bands, context, parallelThreshold, band =>
        {
            band.Items.Sort((a, b) =>
            {
                var byPosition = state.X[a].CompareTo(state.X[b]);
                if (byPosition != 0) return byPosition;
                var byPin = (problem.Nodes[a].IsPinned ? 0 : 1).CompareTo(problem.Nodes[b].IsPinned ? 0 : 1);
                return byPin != 0 ? byPin : a.CompareTo(b);
            });

            var placed = new List<int>(band.Items.Count);
            foreach (var i in band.Items)
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                var node = problem.Nodes[i];
                if (node.IsPinned)
                {
                    placed.Add(i);
                    continue;
                }

                // 只解冲突：从节点自己的当前左边缘起步，不再按父右边缘拉齐。
                var floor = state.X[i];
                foreach (var p in placed)
                {
                    if (!VerticalOverlap(state, i, p)) continue;
                    var required = state.X[p] + problem.Nodes[p].Width + columnGap;
                    if (required > floor) floor = required;
                }

                var proposed = floor;

                var ceiling = double.PositiveInfinity;
                foreach (var p in band.Items)
                {
                    if (p == i || !problem.Nodes[p].IsPinned) continue;
                    if (state.X[p] < state.X[i] - Epsilon) continue;
                    if (!VerticalOverlap(state, i, p)) continue;
                    var candidate = state.X[p] - columnGap - node.Width;
                    if (candidate < ceiling) ceiling = candidate;
                }

                if (!double.IsPositiveInfinity(ceiling) && proposed > ceiling)
                {
                    proposed = Math.Max(floor, ceiling);
                }

                var hard = HasHorizontalViolation(state, i, band.Members, tileIndex, columnGap);
                var decision = new WorkflowLayoutPositionDecision(
                    i, state.X[i], state.Y[i], proposed, state.Y[i],
                    node.IsPinned, hard, tolerance, WorkflowLayoutAxis.Horizontal);
                if (hard || _policy.ShouldApply(decision))
                {
                    state.X[i] = proposed;
                }

                placed.Add(i);
            }
        });
    }

    private static bool HasVerticalViolation(
        WorkflowLayoutState state, int index, HashSet<int> members,
        LayoutTileIndex tileIndex, double rowGap)
    {
        var bounds = Rectangle(state, index);

        foreach (var other in tileIndex.QueryHalo(bounds, 1))
        {
            if (other == index || !members.Contains(other)) continue;
            if (!HorizontalOverlap(state, index, other)) continue;
            if (VerticalGap(state, index, other) < rowGap - Epsilon) return true;
        }

        return false;
    }

    private static bool HasHorizontalViolation(
        WorkflowLayoutState state, int index, HashSet<int> members,
        LayoutTileIndex tileIndex, double columnGap)
    {
        var bounds = Rectangle(state, index);

        foreach (var other in tileIndex.QueryHalo(bounds, 1))
        {
            if (other == index || !members.Contains(other)) continue;
            if (!VerticalOverlap(state, index, other)) continue;
            if (HorizontalGap(state, index, other) < columnGap - Epsilon) return true;
        }

        return false;
    }

    private static bool HorizontalOverlap(WorkflowLayoutState state, int a, int b)
    {
        var problem = state.Problem;
        return state.X[a] < state.X[b] + problem.Nodes[b].Width
            && state.X[b] < state.X[a] + problem.Nodes[a].Width;
    }

    private static bool VerticalOverlap(WorkflowLayoutState state, int a, int b)
    {
        var problem = state.Problem;
        return state.Y[a] < state.Y[b] + problem.Nodes[b].Height
            && state.Y[b] < state.Y[a] + problem.Nodes[a].Height;
    }

    private static double VerticalGap(WorkflowLayoutState state, int a, int b)
    {
        var problem = state.Problem;
        var aBottom = state.Y[a] + problem.Nodes[a].Height;
        var bBottom = state.Y[b] + problem.Nodes[b].Height;
        if (aBottom <= state.Y[b]) return state.Y[b] - aBottom;
        if (bBottom <= state.Y[a]) return state.Y[a] - bBottom;
        return 0d;
    }

    private static double HorizontalGap(WorkflowLayoutState state, int a, int b)
    {
        var problem = state.Problem;
        var aRight = state.X[a] + problem.Nodes[a].Width;
        var bRight = state.X[b] + problem.Nodes[b].Width;
        if (aRight <= state.X[b]) return state.X[b] - aRight;
        if (bRight <= state.X[a]) return state.X[a] - bRight;
        return 0d;
    }

    /// <summary>
    /// Repairs positive-area overlaps the band-local sweeps cannot see. Runs after the sweeps, is
    /// single-threaded (the result must not depend on scheduling), and is bounded by a pass budget.
    /// </summary>
    /// <remarks>
    /// Two rectangles whose centres fall in different tile columns <b>and</b> different tile rows are
    /// invisible to both column-band and row-band passes. This pass finds them through the tile index,
    /// chooses the deterministic mover (a pinned partner moves instead; two pins are left alone), and pushes
    /// it along the axis that needs the least displacement to satisfy the row/column gap. Hard overlaps are
    /// not subject to <see cref="WorkflowLayoutOptions.PositionTolerance"/>.
    /// </remarks>
    private void ResolveGlobalOverlaps(
        WorkflowLayoutState state, WorkflowLayoutContext context,
        double rowGap, double columnGap, double tolerance)
    {
        var problem = state.Problem;
        var options = state.Options;
        var tileSize = options.TileSize > 0d ? options.TileSize : 256d;
        var originX = problem.ContentBounds.IsEmpty ? 0d : problem.ContentBounds.Left;
        var originY = problem.ContentBounds.IsEmpty ? 0d : problem.ContentBounds.Top;
        var maxPasses = Math.Max(4, options.MaxSweeps);

        var repairs = 0;
        var passes = 0;
        var unresolvedPinned = 0;

        for (var pass = 0; pass < maxPasses; pass++)
        {
            context.CancellationToken.ThrowIfCancellationRequested();

            var index = BuildTileIndex(state, tileSize, originX, originY);
            var repairedThisPass = 0;
            var pinnedThisPass = 0;

            for (var i = 0; i < problem.Nodes.Count; i++)
            {
                var candidates = new List<int>(index.QueryHalo(Rectangle(state, i), 0));
                candidates.Sort();

                foreach (var j in candidates)
                {
                    if (j <= i) continue;
                    if (IntersectionArea(state, i, j) <= 0d) continue;

                    var mover = ChooseMover(problem, i, j);
                    if (mover < 0)
                    {
                        pinnedThisPass++;
                        continue;
                    }

                    var other = mover == i ? j : i;
                    if (IntersectionArea(state, mover, other) <= 0d) continue;

                    var (axis, delta) = MinimalSeparation(state, mover, other, rowGap, columnGap);
                    var currentX = state.X[mover];
                    var currentY = state.Y[mover];
                    var targetX = axis == WorkflowLayoutAxis.Horizontal ? currentX + delta : currentX;
                    var targetY = axis == WorkflowLayoutAxis.Vertical ? currentY + delta : currentY;

                    var decision = new WorkflowLayoutPositionDecision(
                        mover, currentX, currentY, targetX, targetY,
                        isPinned: false, hasHardViolation: true, tolerance, axis);

                    // Hard overlaps cannot be vetoed. The policy is still consulted so "hard wins, pinned
                    // never moves" stays the one semantic boundary shared with the band sweeps.
                    var allowed = _policy.ShouldApply(decision);
                    if (decision.HasHardViolation || allowed)
                    {
                        state.X[mover] = targetX;
                        state.Y[mover] = targetY;
                        repairedThisPass++;
                    }
                }
            }

            repairs += repairedThisPass;
            passes = pass + 1;
            unresolvedPinned = pinnedThisPass;
            if (repairedThisPass == 0) break;
        }

        context.Report("layout.globalRepairs", repairs);
        context.Report("layout.globalRepairPasses", passes);
        if (repairs > 0)
        {
            context.ReportDiagnostic(
                $"Global overlap repair separated {repairs} overlapping pair(s) in {passes} pass(es).");
        }
        if (unresolvedPinned > 0)
        {
            context.ReportDiagnostic(
                $"{unresolvedPinned} overlapping pair(s) involve two pinned nodes and were left in place.");
        }
    }

    private static int ChooseMover(WorkflowLayoutProblem problem, int a, int b)
    {
        var aPinned = problem.Nodes[a].IsPinned;
        var bPinned = problem.Nodes[b].IsPinned;
        if (aPinned && bPinned) return -1;
        if (aPinned) return b;
        if (bPinned) return a;

        // 两个都可动时固定选下标大的，结果不依赖遍历顺序。
        return b;
    }

    private static (WorkflowLayoutAxis Axis, double Delta) MinimalSeparation(
        WorkflowLayoutState state, int moverIndex, int otherIndex, double rowGap, double columnGap)
    {
        var problem = state.Problem;
        var mover = problem.Nodes[moverIndex];
        var other = problem.Nodes[otherIndex];

        var right = state.X[otherIndex] + other.Width + columnGap - state.X[moverIndex];
        var left = state.X[otherIndex] - columnGap - (state.X[moverIndex] + mover.Width);
        var down = state.Y[otherIndex] + other.Height + rowGap - state.Y[moverIndex];
        var up = state.Y[otherIndex] - rowGap - (state.Y[moverIndex] + mover.Height);

        // 平局按 右 → 左 → 下 → 上 的固定顺序；只选绝对位移最小的那一支。
        var axis = WorkflowLayoutAxis.Horizontal;
        var delta = right;
        var best = Math.Abs(right);
        if (Math.Abs(left) < best)
        {
            best = Math.Abs(left);
            axis = WorkflowLayoutAxis.Horizontal;
            delta = left;
        }
        if (Math.Abs(down) < best)
        {
            best = Math.Abs(down);
            axis = WorkflowLayoutAxis.Vertical;
            delta = down;
        }
        if (Math.Abs(up) < best)
        {
            axis = WorkflowLayoutAxis.Vertical;
            delta = up;
        }
        return (axis, delta);
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

    private static LayoutTileIndex BuildTileIndex(
        WorkflowLayoutState state, double tileSize, double originX, double originY)
    {
        var problem = state.Problem;
        var index = new LayoutTileIndex(tileSize, originX, originY);
        for (var i = 0; i < problem.Nodes.Count; i++)
        {
            index.Add(i, Rectangle(state, i));
        }
        return index;
    }

    private static List<Band> BuildBands(WorkflowLayoutState state, LayoutTileIndex index, bool vertical)
    {
        var problem = state.Problem;
        var map = new Dictionary<int, Band>();

        for (var i = 0; i < problem.Nodes.Count; i++)
        {
            var node = problem.Nodes[i];
            var cell = index.CellOf(
                state.X[i] + (node.Width * 0.5d),
                state.Y[i] + (node.Height * 0.5d));
            var key = vertical ? cell.X : cell.Y;

            if (!map.TryGetValue(key, out var band))
            {
                band = new Band(key);
                map[key] = band;
            }

            band.Items.Add(i);
            band.Members.Add(i);
        }

        var bands = new List<Band>(map.Values);
        bands.Sort((a, b) => a.Key.CompareTo(b.Key));
        return bands;
    }

    private static void RunBands(
        List<Band> bands, WorkflowLayoutContext context, int parallelThreshold, Action<Band> body)
    {
        if (!context.EnableParallelism || bands.Count < Math.Max(1, parallelThreshold))
        {
            foreach (var band in bands)
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                body(band);
            }
            return;
        }

        Parallel.ForEach(bands, new ParallelOptions { CancellationToken = context.CancellationToken }, body);
    }

    private static double MaxDelta(WorkflowLayoutState state, double[] previousX, double[] previousY)
    {
        var max = 0d;
        for (var i = 0; i < state.X.Length; i++)
        {
            var dx = Math.Abs(state.X[i] - previousX[i]);
            var dy = Math.Abs(state.Y[i] - previousY[i]);
            if (dx > max) max = dx;
            if (dy > max) max = dy;
        }
        return max;
    }

    private sealed class Band(int key)
    {
        public int Key { get; } = key;
        public List<int> Items { get; } = [];
        public HashSet<int> Members { get; } = [];
    }
}
