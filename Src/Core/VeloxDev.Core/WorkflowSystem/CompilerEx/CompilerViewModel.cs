using System.Collections.ObjectModel;
using VeloxDev.MVVM;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.WorkflowSystem.CompilerEx;

/// <summary>
/// 编译入口：把一个起点（Controller）可达的子图分解成若干编译图（多图语义）。
/// v1 分解算法（直接裁剪，无环路）：
///  - 线性段（单入单出）→ ExecuteEntry；
///  - 实现 <see cref="ICompileTimeRouter"/> 的节点 → BranchEntry（静态分支按当前 key 剪枝，动态分支全保留）；
///  - 路由 key 指向多个下游 → ParallelEntry（扇出/汇聚）；无下游 → IsTerminal 终端分支；
///  - 分支后所有出口共同指向的节点 → 汇合点，作为父图下一段链的起点（序号带偏移，不归零）；
///  - 编译完给每个实现 <see cref="ICompileTimeAware"/> 的节点注入 <see cref="CompileContext"/>。
/// 运行期回退由节点实现 <see cref="IRedirectable"/>（链内回退）承担，编译图本身是无环的。
/// </summary>
public sealed partial class CompilerViewModel
{
    [VeloxProperty] private ObservableCollection<CompiledGraph> _graphs = [];

    public async Task<IReadOnlyList<CompiledGraph>> CompileAsync<T>(T component, CancellationToken ct = default)
        where T : IWorkflowViewModel
    {
        if (component is not IWorkflowNodeViewModel start)
            throw new ArgumentException(
                $"CompileAsync 需要 IWorkflowNodeViewModel 作为起点，收到 {component?.GetType().Name}。");

        var state = new CompileState();
        var graphs = new List<CompiledGraph> { await CompileGraphAsync(start, state, ct) };

        _graphs.Clear();
        foreach (var g in graphs) _graphs.Add(g);
        return graphs;
    }

    private async Task<CompiledGraph> CompileGraphAsync(
        IWorkflowNodeViewModel start, CompileState state, CancellationToken ct)
    {
        var entries = new List<ActionEntry>();
        var chain = new List<IWorkflowNodeViewModel>();
        var offset = state.Counter;
        var node = start;
        var resumedAfterBranch = false;   // 当前 node 是否刚由分支汇合点续上（应作为新链起点处理）

        while (node != null)
        {
            // 汇合边界：线性走到一个多输入节点（非本图起点、非分支续接点）→ 停止，
            // 交回父图从它继续。边界节点不标记 visited（它尚未被编译，属于父图）。
            if (!ReferenceEquals(node, start) && !resumedAfterBranch && HasMultipleInputs(node))
                break;

            // 已编译节点不再处理（无环图，避免重复编译）。
            if (!state.Visited.Add(node))
                break;
            resumedAfterBranch = false;

            if (node is ICompileTimeRouter router)
            {
                FlushChain(entries, chain, state, offset);
                AttachCompileContext(node, state.Counter, 0, offset);
                state.Counter++;

                var routeTable = await router.GetRouteTable();
                var currentKey = await router.ResolveRouteKey(null);   // 编译期 payload = null
                var isDynamic = currentKey is null;
                var options = new ObservableCollection<BranchOption>();
                var exits = new List<IWorkflowNodeViewModel?>();

                // 通用路由编译：每个 key 的子图可能是 单一路径 / 扇出(ParallelEntry)。
                // 无下游 → 终端分支；单目标 → 普通子图；多目标 → 扇出(并行组，汇聚点=CommonNext)。
                foreach (var kv in routeTable)
                {
                    var label = kv.Key?.ToString() ?? "?";
                    if (kv.Value is null || kv.Value.Count == 0)
                    {
                        options.Add(new BranchOption { Key = kv.Key, Label = label, Graph = null, IsTerminal = true });
                        continue;
                    }
                    if (kv.Value.Count == 1)
                    {
                        var target = kv.Value[0];
                        if (target is null) continue;
                        var sub = await CompileGraphAsync(target, state, ct);
                        options.Add(new BranchOption { Key = kv.Key, Label = label, Graph = sub });
                        exits.Add(LastNode(sub));
                        continue;
                    }
                    // 多目标 → 扇出：每路子图编译进 ParallelEntry。
                    var branches = new List<CompiledGraph>();
                    var subExits = new List<IWorkflowNodeViewModel?>();
                    foreach (var t in kv.Value)
                    {
                        if (t is null) continue;
                        var sub = await CompileGraphAsync(t, state, ct);
                        branches.Add(sub);
                        subExits.Add(LastNode(sub));
                    }
                    options.Add(new BranchOption
                    {
                        Key = kv.Key,
                        Label = label,
                        Graph = new CompiledGraph
                        {
                            Entries = new ObservableCollection<ActionEntry>
                            {
                                new ParallelEntry { Branches = new ObservableCollection<CompiledGraph>(branches) },
                            }
                        },
                    });
                    // 扇出各路的末尾节点加入出口；最终 CommonNext 会算出它们的公共下游（汇聚点）。
                    exits.AddRange(subExits);
                }

                // 静态模式（编译期已知 key）：全拓扑里不在活跃分支的下游节点，走一遍并发「重置信号」
                // （CompileContext.Order = -1，绝对停止）——两种模式下每个节点都会被编译器走到。
                if (!isDynamic)
                {
                    var liveTargets = new HashSet<IWorkflowNodeViewModel>(
                        routeTable.Values.Where(v => v is not null).SelectMany(v => v!)
                            .Where(t => t is not null));
                    foreach (var target in AllTargets(node))
                    {
                        if (target is null || liveTargets.Contains(target)) continue;
                        MarkStoppedBranch(target, state);
                    }
                }

                entries.Add(new BranchEntry
                {
                    Router = node,
                    Options = options,
                    IsDynamic = isDynamic,
                    // 编译期锁定的路由 key：Static 下运行期以此为准（编译瞬间的选中值）；
                    // Dynamic 下为 null，运行期重新解析。
                    CompileKey = currentKey,
                });

                // 分支后的汇合点：所有活跃分支出口共同指向的下一个节点。
                node = CommonNext(exits);
                resumedAfterBranch = node is not null;
                continue;
            }

            // 线性节点
            chain.Add(node);
            var next = await SingleTargetValidAsync(node, state, ct);
            if (next is null || ReferenceEquals(next, node))
            {
                FlushChain(entries, chain, state, offset);
                break;
            }
            node = next;
        }

        FlushChain(entries, chain, state, offset);
        return new CompiledGraph { Entries = new ObservableCollection<ActionEntry>(entries) };
    }

    /// <summary>把当前线性链 flush 成 ExecuteEntry，并给链内每个节点分配编译身份。</summary>
    private static void FlushChain(List<ActionEntry> entries, List<IWorkflowNodeViewModel> chain,
        CompileState state, int offset)
    {
        if (chain.Count == 0) return;
        for (int i = 0; i < chain.Count; i++)
            AttachCompileContext(chain[i], state.Counter + i, i, offset);
        state.Counter += chain.Count;
        entries.Add(new ExecuteEntry { Nodes = new ObservableCollection<IWorkflowNodeViewModel>(chain) });
        chain.Clear();
    }

    private static void AttachCompileContext(IWorkflowNodeViewModel node, int order, int chainIndex, int offset)
    {
        if (node is ICompileTimeAware aware)
            aware.AttachCompileTimeContext(new CompileContext
            {
                Order = order,
                ChainIndex = chainIndex,
                Offset = offset,
            });
    }

    /// <summary>
    /// 静态分支下，从被略过目标出发，沿全拓扑走一遍，给每个节点发送「重置信号」（Order = -1）。
    /// 到汇合点（多输入、属主线）或已活跃节点处停止；节点加入 visited，避免主线再处理。
    /// </summary>
    private static void MarkStoppedBranch(IWorkflowNodeViewModel start, CompileState state)
    {
        var queue = new Queue<IWorkflowNodeViewModel>();
        queue.Enqueue(start);
        while (queue.Count > 0)
        {
            var n = queue.Dequeue();
            if (state.Visited.Contains(n)) continue;
            if (!ReferenceEquals(n, start) && HasMultipleInputs(n)) continue;
            state.Visited.Add(n);
            AttachCompileContext(n, -1, -1, 0);
            foreach (var t in AllTargets(n))
                if (t is not null) queue.Enqueue(t);
        }
    }

    /// <summary>节点的全部下游目标（输出 slot 的 Targets，去重）。</summary>
    private static IEnumerable<IWorkflowNodeViewModel> AllTargets(IWorkflowNodeViewModel node)
        => node.Slots.Where(s => s is not null)
            .SelectMany(s => s!.Targets ?? [])
            .Select(t => t.Parent)
            .OfType<IWorkflowNodeViewModel>()
            .Distinct();

    private static IWorkflowNodeViewModel? LastNode(CompiledGraph? graph)
    {
        if (graph is null) return null;
        for (int i = graph.Entries.Count - 1; i >= 0; i--)
        {
            var e = LastNode(graph.Entries[i]);
            if (e is not null) return e;
        }
        return null;
    }

    private static IWorkflowNodeViewModel? LastNode(ActionEntry entry) => entry switch
    {
        ExecuteEntry exec when exec.Nodes.Count > 0 => exec.Nodes[exec.Nodes.Count - 1],
        BranchEntry branch when branch.Options.Count > 0 => LastNode(branch.Options[branch.Options.Count - 1].Graph),
        ParallelEntry par when par.Branches.Count > 0 => LastNode(par.Branches[par.Branches.Count - 1]),
        _ => null,
    };

    /// <summary>所有分支出口的共同下游（汇合点）；无共同下游返回 null（分支各自结束）。</summary>
    private static IWorkflowNodeViewModel? CommonNext(List<IWorkflowNodeViewModel?> exits)
    {
        IWorkflowNodeViewModel? common = null;
        var first = true;
        foreach (var exit in exits)
        {
            if (exit is null) continue;
            var next = SingleTarget(exit);
            if (first) { common = next; first = false; }
            else if (!ReferenceEquals(next, common)) { common = null; break; }
        }
        return common;
    }

    private static bool HasMultipleInputs(IWorkflowNodeViewModel node)
        => node.Slots.Where(s => s is not null)
            .SelectMany(s => s!.Sources ?? [])
            .Select(s => s.Parent)
            .OfType<IWorkflowNodeViewModel>()
            .Distinct()
            .Count() >= 2;

    private static IWorkflowNodeViewModel? SingleTarget(IWorkflowNodeViewModel node)
    {
        var targets = node.Slots.Where(s => s is not null)
            .SelectMany(s => s!.Targets ?? [])
            .Select(s => s.Parent)
            .OfType<IWorkflowNodeViewModel>()
            .Distinct()
            .ToList();
        return targets.Count == 1 ? targets[0] : null;
    }

    /// <summary>
    /// 链续接的「合法性」版 <see cref="SingleTarget"/>：返回经由合法输出边唯一可达的下游节点
    /// （去重后恰一个）；否则 null。每条输出边在编译期都以 <see cref="ICompileContext"/> 走一遍
    /// <see cref="IWorkflowNodeViewModelHelper.AccessAsync"/> 静态检测——非法边按运行期
    /// 广播语义跳过（视为未连接），不进入编译图。
    /// </summary>
    private async Task<IWorkflowNodeViewModel?> SingleTargetValidAsync(
        IWorkflowNodeViewModel node, CompileState state, CancellationToken ct)
    {
        var validTargets = new List<IWorkflowNodeViewModel>();
        foreach (var sender in node.Slots.Where(s => s is not null))
        {
            foreach (var receiver in sender!.Targets ?? [])
            {
                ct.ThrowIfCancellationRequested();
                var target = receiver.Parent as IWorkflowNodeViewModel;
                if (target is null) continue;

                var helper = node.GetHelper();
                if (helper is null) continue;

                // 编译期占位身份：Order 取当前游标（发送节点尚未编号），Sender/Receiver 填待校验的边。
                var compileCtx = new CompileContext
                {
                    Order = state.Counter,
                    ChainIndex = -1,
                    Offset = 0,
                    Sender = sender,
                    Receiver = receiver,
                };
                if (!await helper.AccessAsync(compileCtx, ct).ConfigureAwait(false))
                    continue;
                validTargets.Add(target);
            }
        }
        var distinct = validTargets.Distinct().ToList();
        return distinct.Count == 1 ? distinct[0] : null;
    }

    /// <summary>编译游标：全局序号计数器 + 已访问集合（避免 ref 参数，供异步递归共享）。</summary>
    private sealed class CompileState
    {
        public int Counter;
        public readonly HashSet<IWorkflowNodeViewModel> Visited = [];
    }
}
