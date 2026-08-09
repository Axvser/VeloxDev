namespace VeloxDev.WorkflowSystem.Compilation;

/// <summary>
/// Workflow compiler.
/// Traverses the graph topology from a start node and produces an ordered list
/// of CompiledItem. Supports BFS and DFS traversal, cross-domain detection,
/// and three cycle-handling strategies (Throw / Trim / Allow).
/// </summary>
public sealed class WorkflowCompiler : IWorkflowCompiler
{
    private readonly IDiagnosticLogger? _logger;
    private Guid _machineId;

    /// <summary>
    /// Creates a compiler with optional debug diagnostics.
    /// </summary>
    /// <param name="logger">Optional diagnostic logger for tracing compilation steps.</param>
    public WorkflowCompiler(IDiagnosticLogger logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Creates a compiler without diagnostic logging.
    /// Pass a logger to <see cref="WorkflowCompiler(IDiagnosticLogger)"/> or use
    /// <see cref="CompilationResult.ExecuteAsync(object?, System.IO.TextWriter?, System.Threading.CancellationToken)"/>
    /// for execution-phase debug output.
    /// </summary>
    public WorkflowCompiler()
    {
        _logger = null;
    }

// ── DiagnosticContext helpers ─────────────────────────────────────────

    private DiagnosticContext Ctx(string contentType, string message) =>
        new(DateTimeOffset.UtcNow, _machineId, contentType, "Info", message);

    private DiagnosticContext CtxWarn(string contentType, string message) =>
        new(DateTimeOffset.UtcNow, _machineId, contentType, "Warning", message);

    private DiagnosticContext CtxErr(string contentType, string message) =>
        new(DateTimeOffset.UtcNow, _machineId, contentType, "Error", message);

    /// <summary>
    /// Compile
    /// For <see cref="CompileScope.FromNode"/> returns a single-element list.
    /// For <see cref="CompileScope.Omni"/> returns one result per discovered
    /// boundary node, each representing an independent subgraph.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Cross-domain node detected, or cycle found with <see cref="CycleHandling.Throw"/>.
    /// </exception>
    public IReadOnlyList<CompilationResult> Compile(IWorkflowNodeViewModel startNode, CompileMode mode,
        CompileDirection direction = CompileDirection.Forward,
        CompileScope scope = CompileScope.FromNode,
        CycleHandling cycleHandling = CycleHandling.Throw)
    {
        _machineId = Guid.NewGuid();

        var tree = startNode.Parent
            ?? throw new InvalidOperationException("Start node has no Parent (Tree) assigned.");

        _logger?.Log(Ctx("Compiler", $"{startNode.GetType().Name} {mode} {direction} {scope} {cycleHandling}"));

        var allNodes = tree.Nodes.ToArray();
        var nodeToIndex = allNodes.Select((n, i) => (n, i)).ToDictionary(x => x.n, x => x.i);

        if (!nodeToIndex.TryGetValue(startNode, out var startIdx))
            throw new InvalidOperationException("Start node is not a member of its Parent Tree.");

        // 根据方向构建邻接表
        var adj = direction switch
        {
            CompileDirection.Reverse => BuildReverseAdjacency(allNodes, nodeToIndex),
            _ => BuildForwardAdjacency(allNodes, nodeToIndex),
        };

        // 检测环路（基于全图，一次检测供所有入口使用）
        var globalCycleInfo = DetectCycle(allNodes, adj);
        if (globalCycleInfo.HasCycle && cycleHandling == CycleHandling.Throw)
        {
            _logger?.LogError(CtxErr("Cycle", $"cycle {startNode.GetType().Name} ({allNodes.Length})"));
            throw new InvalidOperationException(
                $"Cycle detected in workflow graph. " +
                $"Start node '{startNode.GetType().Name}' belongs to a graph with {allNodes.Length} nodes.");
        }

        _logger?.Log(Ctx("Cycle", $"{(globalCycleInfo.HasCycle ? "cycle" : "ok")} ({allNodes.Length})"));

        // 根据范围确定遍历起点
        var startIndices = scope switch
        {
            CompileScope.Omni => direction == CompileDirection.Reverse
                ? FindOmniExits(allNodes)
                : FindOmniEntries(allNodes, nodeToIndex),
            _ => [startIdx],
        };

        if (startIndices.Count == 0)
            return [];

		// 每个起点独立构建可执行计划
        var results = new List<CompilationResult>(startIndices.Count);
        var globalVisited = new HashSet<int>();

        foreach (var si in startIndices)
        {
            var (Indices, Depth) = mode == CompileMode.BFS
                ? TraverseBfsFrom(si, allNodes, adj, globalVisited)
                : TraverseDfsFrom(si, allNodes, adj, globalVisited);

            if (Indices.Count == 0) continue;

            // 跨域检查
            foreach (var idx in Indices)
            {
                if (allNodes[idx].Parent != tree)
                    throw new InvalidOperationException(
                        $"Cross-domain node detected: node at index {idx} belongs to a different Tree.");
            }

            // 构建 Item（每个结果独立 UID：Omni 模式下各结果的顺序 ID 从 0 重新开始，
            // 复合 ID = UID + 顺序 ID 在单次 Compile 内全局唯一，无冲突）
            int idCounter = 0;
            var uid = Guid.NewGuid();
            var items = Indices.Select(idx =>
                new CompiledItem(idCounter++, allNodes[idx], 0, new CompiledIdentity(uid, idCounter - 1))
            ).ToList();

            for (int i = 0; i < items.Count; i++)
            {
                items[i].Order = i;
                items[i].Depth = Depth[Indices[i]];
            }

            // 环路标记（每个入口独立处理）
            if (cycleHandling == CycleHandling.Allow && globalCycleInfo.HasCycle)
                MarkLoopEntry(items, allNodes, globalCycleInfo);

            CollectSlotRoutes(items);
            ComputeBranchExclusives(items, allNodes, nodeToIndex);

            // 条件分支剪除（编译期）：路由元数据（路由表 + 当前选中分支）能判定走不到的分支，
            // 其独占节点在编译时就被标记为 IsSkipped —— 不拥有流程身份（Order/OrderId = -1），
            // 执行时也不会运行。但它们仍收到 OnCompiled 通知，得知自己被编译时略过（UID 保留）。
            MarkCompileTimeSkipped(items);

            // 编译期通知：节点立即获知自己的编译身份（ICompileTimeNotifier）
            // 被略过的节点也会收到 —— 但拿到的是跳过标记，而非流程顺序。
            foreach (var item in items)
                if (item.Node is ICompileTimeNotifier notifier)
                    notifier.OnCompiled(item);

            results.Add(new CompilationResult(items.AsReadOnly(), mode, direction, scope,
                globalCycleInfo.HasCycle, cycleHandling, _logger, _machineId, uid));
        }

        _logger?.Log(Ctx("Compiler", $"done {results.Count}r {results.Sum(r => r.Items.Count)}i"));

        return results.AsReadOnly();
    }

    // ── Adjacency ──────────────────────────────────────────────────────────

    private List<int>[] BuildForwardAdjacency(
        IWorkflowNodeViewModel[] nodes, Dictionary<IWorkflowNodeViewModel, int> nodeToIndex)
    {
        var adj = new List<int>[nodes.Length];
        for (int i = 0; i < nodes.Length; i++) adj[i] = [];

        int edgeCount = 0;
        for (int i = 0; i < nodes.Length; i++)
        {
            foreach (var slot in nodes[i].Slots)
            {
                foreach (var target in slot.Targets)
                {
                    if (target.Parent is { } parent &&
                        nodeToIndex.TryGetValue(parent, out var j))
                    {
                        adj[i].Add(j); edgeCount++;
                    }
                }
            }
        }

        _logger?.Log(Ctx("Adjacency", $"fwd {nodes.Length}n {edgeCount}e"));
        return adj;
    }

    private List<int>[] BuildReverseAdjacency(
        IWorkflowNodeViewModel[] nodes, Dictionary<IWorkflowNodeViewModel, int> nodeToIndex)
    {
        var adj = new List<int>[nodes.Length];
        for (int i = 0; i < nodes.Length; i++) adj[i] = [];

        int edgeCount = 0;
        for (int i = 0; i < nodes.Length; i++)
        {
            foreach (var slot in nodes[i].Slots)
            {
                foreach (var source in slot.Sources)
                {
                    if (source.Parent is { } parent &&
                        nodeToIndex.TryGetValue(parent, out var j))
                    {
                        adj[i].Add(j); edgeCount++;
                    }
                }
            }
        }

        _logger?.Log(Ctx("Adjacency", $"rev {nodes.Length}n {edgeCount}e"));
        return adj;
    }

    /// <summary>
    /// Find all true entry points (in-degree = 0) in the graph.
    /// For Forward + Omni: these are natural starting nodes.
    /// </summary>
    private List<int> FindOmniEntries(
        IWorkflowNodeViewModel[] nodes, Dictionary<IWorkflowNodeViewModel, int> nodeToIndex)
    {
        var inDegree = new int[nodes.Length];
        for (int i = 0; i < nodes.Length; i++)
        {
            foreach (var slot in nodes[i].Slots)
            {
                foreach (var target in slot.Targets)
                {
                    if (target.Parent is { } parent &&
                        nodeToIndex.TryGetValue(parent, out var j))
                    {
                        inDegree[j]++;
                    }
                }
            }
        }

        var entries = new List<int>();
        for (int i = 0; i < inDegree.Length; i++)
            if (inDegree[i] == 0) entries.Add(i);

        _logger?.Log(Ctx("Omni", $"entries {entries.Count}"));
        return entries;
    }

    /// <summary>
    /// Find all true exit points (out-degree = 0) in the graph.
    /// For Reverse + Omni: these are natural ending nodes to start reverse traversal from.
    /// </summary>
    private List<int> FindOmniExits(IWorkflowNodeViewModel[] nodes)
    {
        var outDegree = new int[nodes.Length];
        for (int i = 0; i < nodes.Length; i++)
        {
            foreach (var slot in nodes[i].Slots)
            {
                outDegree[i] += slot.Targets.Count;
            }
        }

        var exits = new List<int>();
        for (int i = 0; i < outDegree.Length; i++)
            if (outDegree[i] == 0) exits.Add(i);

        _logger?.Log(Ctx("Omni", $"exits {exits.Count}"));
        return exits;
    }

    // ── BFS ────────────────────────────────────────────────────────────────

    private (List<int> Indices, int[] Depth) TraverseBfsFrom(int start, IWorkflowNodeViewModel[] nodes,
        List<int>[] adj, HashSet<int> globalVisited)
    {
        var result = new List<int>();
        var depth = new int[nodes.Length];
        // 使用 LinkedList 支持路由子节点插队到队首
        var queue = new LinkedList<int>();
        queue.AddLast(start);
        depth[start] = 0;

        _logger?.Log(Ctx("BFS", $"start [{start}]"));

        while (queue.Count > 0)
        {
            var u = queue.First!.Value;
            queue.RemoveFirst();
            if (!globalVisited.Add(u)) continue;
            result.Add(u);

            var isRouter = nodes[u] is ICompileTimeRouter;
            var childDepth = isRouter ? depth[u] : depth[u] + 1;

            // 按优先级排序同层邻居
            var neighbors = adj[u]
                .Where(v => !globalVisited.Contains(v))
                .OrderBy(v => GetPriority(nodes[v]))
                .ToList();

            if (neighbors.Count > 0)
                _logger?.Log(Ctx("BFS", $"[{u}] {neighbors.Count}nb d{depth[u]} r{isRouter}"));

            // 路由节点的子节点插到队首（它们有效深度 = 路由深度，应优先于同层其他节点）
            foreach (var v in neighbors)
            {
                depth[v] = childDepth;
                if (isRouter)
                    queue.AddFirst(v);
                else
                    queue.AddLast(v);
            }
        }

        _logger?.Log(Ctx("BFS", $"done {result.Count}n"));
        return (result, depth);
    }

    // ── DFS ────────────────────────────────────────────────────────────────

    private (List<int> Indices, int[] Depth) TraverseDfsFrom(int start, IWorkflowNodeViewModel[] nodes,
        List<int>[] adj, HashSet<int> globalVisited)
    {
        var result = new List<int>();
        var depth = new int[nodes.Length];

        _logger?.Log(Ctx("DFS", $"start [{start}]"));

        void Dfs(int u, int currentDepth)
        {
            if (!globalVisited.Add(u)) return;

            // 前序遍历：父节点优先进入结果
            result.Add(u);
            depth[u] = currentDepth;

            var isRouter = nodes[u] is ICompileTimeRouter;
            var childDepth = isRouter ? currentDepth : currentDepth + 1;

            // 按优先级排序子节点
            var children = adj[u]
                .Where(v => !globalVisited.Contains(v))
                .OrderBy(v => GetPriority(nodes[v]))
                .ToList();

            if (children.Count > 0)
                _logger?.Log(Ctx("DFS", $"[{u}] {children.Count}ch d{currentDepth} r{isRouter}"));

            foreach (var v in children)
                Dfs(v, childDepth);
        }

        Dfs(start, 0);

        _logger?.Log(Ctx("DFS", $"done {result.Count}n"));
        return (result, depth);
    }

    /// <summary>
    /// 获取节点的编译优先级。未实现 ICompileTimePriority 的节点默认返回 0。
    /// </summary>
    private static int GetPriority(IWorkflowNodeViewModel node)
        => node is ICompileTimePriority p ? p.CompilePriority : 0;

    // ── Cycle detection ────────────────────────────────────────────────────

    private sealed class CycleDetectionResult
    {
        public bool HasCycle;
        public int LoopEntryIndex = -1;
        public int LoopTailIndex = -1;
    }

    private static CycleDetectionResult DetectCycle(IWorkflowNodeViewModel[] nodes, List<int>[] adj)
    {
        var state = new byte[nodes.Length];
        var parent = new int[nodes.Length];
        for (int i = 0; i < parent.Length; i++) parent[i] = -1;
        var result = new CycleDetectionResult();

        for (int i = 0; i < nodes.Length; i++)
            if (state[i] == 0 && DfsFindCycle(i, adj, state, parent, result))
                break;

        return result;
    }

    private static bool DfsFindCycle(int u, List<int>[] adj, byte[] state,
        int[] parent, CycleDetectionResult result)
    {
        state[u] = 1;
        foreach (var v in adj[u])
        {
            if (state[v] == 1)
            {
                result.HasCycle = true;
                result.LoopEntryIndex = v;
                result.LoopTailIndex = u;
                return true;
            }
            if (state[v] == 0)
            {
                parent[v] = u;
                if (DfsFindCycle(v, adj, state, parent, result))
                    return true;
            }
        }
        state[u] = 2;
        return false;
    }

    // ── Loop marking ───────────────────────────────────────────────────────

    private void MarkLoopEntry(
        List<CompiledItem> items, IWorkflowNodeViewModel[] nodes,
        CycleDetectionResult cycleInfo)
    {
        if (cycleInfo.LoopEntryIndex < 0 || cycleInfo.LoopTailIndex < 0) return;

        var entryNode = nodes[cycleInfo.LoopEntryIndex];
        var tailNode = nodes[cycleInfo.LoopTailIndex];
        var entryItem = items.FirstOrDefault(i => i.Node == entryNode);
        var tailItem = items.FirstOrDefault(i => i.Node == tailNode);

        if (entryItem is not null)
        {
            entryItem.IsLoopEntry = true;
            entryItem.LoopTailId = tailItem?.Id;
            _logger?.Log(Ctx("Loop", $"entry[{entryItem.Id}] tail[{tailItem?.Id}]"));
        }
    }

    // ── Slot route collection ──────────────────────────────────────────────

    /// <summary>
    /// 收集实现了 <see cref="ICompileTimeRouter"/> 的节点的路由表，
    /// 并将路由表挂到对应的 <see cref="CompiledItem.RouteTable"/> 上。
    /// </summary>
    private void CollectSlotRoutes(List<CompiledItem> items)
    {
        int routerCount = 0;
        foreach (var item in items)
        {
            if (item.Node is ICompileTimeRouter router)
            {
                item.RouteTable = router.GetRouteTable();
                routerCount++;
            }
        }

        _logger?.Log(Ctx("Routes", $"{routerCount}r"));
    }

    // ── Branch exclusive computation ─────────────────────────────────────

    /// <summary>
    /// 为每个路由器节点计算分支独占项。
    /// 对 RouteTable 中的每个分支做 BFS，找出仅通过该分支可达的节点，
    /// 存入 <see cref="CompiledItem.BranchExclusiveItems"/>。
    /// 执行时，未选中分支的独占项将被跳过。
    /// </summary>
    private void ComputeBranchExclusives(
        List<CompiledItem> items, IWorkflowNodeViewModel[] nodes,
        Dictionary<IWorkflowNodeViewModel, int> nodeToIndex)
    {
        var forwardAdj = BuildForwardAdjacency(nodes, nodeToIndex);
        var nodeToItemId = new Dictionary<IWorkflowNodeViewModel, int>();
        foreach (var item in items)
            nodeToItemId[item.Node] = item.Id;

        int routerWithExclusiveCount = 0;
        foreach (var item in items)
        {
            if (item.RouteTable is null || item.RouteTable.Count == 0)
                continue;

            if (!nodeToIndex.TryGetValue(item.Node, out var routerIdx)) continue;

            // 1) 收集每个分支的所有下游节点
            //    一个分支可指向多个目标（1:N fan-out），每个目标都要做 BFS，
            //    并集才是该分支的下游集合。
            var branchDescendants = new Dictionary<object, HashSet<int>>();
            foreach (var kv in item.RouteTable)
            {
                var descendants = new HashSet<int>();
                foreach (var target in kv.Value)
                {
                    if (!nodeToIndex.TryGetValue(target, out var targetIdx)) continue;

                    var queue = new Queue<int>();
                    queue.Enqueue(targetIdx);
                    while (queue.Count > 0)
                    {
                        var u = queue.Dequeue();
                        if (!descendants.Add(u)) continue;
                        foreach (var v in forwardAdj[u])
                            queue.Enqueue(v);
                    }
                }
                branchDescendants[kv.Key] = descendants;
            }

            // 2) 对每个分支，找出仅属于该分支的节点（不在其他分支的下游集合中）
            var allKeys = branchDescendants.Keys.ToArray();
            var exclusive = new Dictionary<object, HashSet<int>>();

            foreach (var kv1 in branchDescendants)
            {
                // 收集所有其他分支的节点
                var otherNodes = new HashSet<int>();
                foreach (var kv2 in branchDescendants)
                {
                    if (Equals(kv2.Key, kv1.Key)) continue;
                    otherNodes.UnionWith(kv2.Value);
                }

                // 本分支中不在其他分支里的节点 = 独占节点
                var exclusiveNodes = new HashSet<int>();
                foreach (var idx in kv1.Value)
                {
                    if (!otherNodes.Contains(idx))
                        exclusiveNodes.Add(idx);
                }

                // 将节点索引映射为 Item ID
                var exclusiveItemIds = new HashSet<int>();
                foreach (var idx in exclusiveNodes)
                {
                    if (nodeToItemId.TryGetValue(nodes[idx], out var id))
                        exclusiveItemIds.Add(id);
                }

                if (exclusiveItemIds.Count > 0)
                    exclusive[kv1.Key] = exclusiveItemIds;
            }

            if (exclusive.Count > 0)
            {
                item.BranchExclusiveItems = exclusive;
                routerWithExclusiveCount++;
            }
        }

        _logger?.Log(Ctx("Exclusive", $"{routerWithExclusiveCount}r"));
    }

    // ── Compile-time branch pruning ─────────────────────────────────────

    /// <summary>
    /// 编译期条件分支剪除。
    /// 对每个实现 <see cref="ICompileTimeRouter"/> 的节点，读取其当前选中的分支
    /// （<see cref="ICompileTimeRouter.GetCurrentRouteKey"/>），把未选中分支的独占项
    /// （<see cref="CompiledItem.BranchExclusiveItems"/> 中非选中 key 对应的 ID 集）
    /// 标记为 <see cref="CompiledItem.IsSkipped"/>。
    ///
    /// 被略过的节点不拥有流程身份：<see cref="CompiledItem.Order"/> 与
    /// <see cref="CompiledItem.CompositeId"/> 的 OrderId 置为 -1（UID 保留）。
    /// 它们仍会收到 <see cref="ICompileTimeNotifier.OnCompiled"/> 通知，
    /// 以便节点知道自己「被编译但被略过」；执行时不会被运行。
    ///
    /// 注：一个节点可能同时属于多个路由器的不同分支（例如钻石图的汇合点），
    /// 只要它不是任何「未选中分支」的独占项，就不会被标记。因此这里逐路由器
    /// 累加跳过集，再统一判定。
    /// </summary>
    private void MarkCompileTimeSkipped(List<CompiledItem> items)
    {
        var skippedIds = new HashSet<int>();
        foreach (var item in items)
        {
            if (item.RouteTable is null || item.Node is not ICompileTimeRouter router)
                continue;
            if (item.BranchExclusiveItems is null || item.BranchExclusiveItems.Count == 0)
                continue;

            var chosenKey = router.GetCurrentRouteKey();
            foreach (var kv in item.BranchExclusiveItems)
            {
                if (Equals(kv.Key, chosenKey)) continue;   // 选中分支 → 保留
                skippedIds.UnionWith(kv.Value);            // 未选中分支 → 略过
            }
        }

        if (skippedIds.Count == 0)
            return;

        int skippedCount = 0;
        foreach (var item in items)
        {
            if (!skippedIds.Contains(item.Id))
                continue;

            item.IsSkipped = true;
            item.Order = -1;
            item.CompositeId = new CompiledIdentity(item.CompositeId.Uid, -1);
            skippedCount++;
        }

        // 紧凑重建保留项的顺序号：被略过的节点不占位，流程顺序连续无空洞。
        // 内部 Id（遍历序号）保持不变，供 BranchExclusiveItems / ErrorRedirect 等引用。
        int next = 0;
        foreach (var item in items)
        {
            if (item.IsSkipped)
                continue;

            item.Order = next;
            item.CompositeId = new CompiledIdentity(item.CompositeId.Uid, next);
            next++;
        }

        _logger?.Log(Ctx("Prune", $"{skippedCount}i"));
    }
}