using System.Collections.ObjectModel;
using VeloxDev.MVVM;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.WorkflowSystem.CompilerEx;

/// <summary>
/// 编译入口：把一个起点（Controller）可达的子图分解成若干编译图（多图语义）。
/// v1 分解算法：
///  - 线性段（单入单出）→ ExecuteEntry；
///  - 实现 <see cref="ICompileTimeRouter"/> 的节点 → BranchEntry（静态分支按当前 key 剪枝，动态分支全保留）；
///  - 分支后所有出口共同指向的节点 → 汇合点，作为父图下一段链的起点（序号带偏移，不归零）；
///  - 编译完给每个实现 <see cref="ICompileTimeAware"/> 的节点注入 <see cref="CompileContext"/>。
/// 环路→RetryEntry 的生成留待后续（v1 以 visited 护栏防止死循环）。
/// </summary>
public sealed partial class CompilerViewModel
{
    [VeloxProperty] private ObservableCollection<CompiledGraph> _graphs = [];

    public async Task<IReadOnlyList<CompiledGraph>> CompileAsync<T>(T component)
        where T : IWorkflowViewModel
    {
        if (component is not IWorkflowNodeViewModel start)
            throw new ArgumentException(
                $"CompileAsync 需要 IWorkflowNodeViewModel 作为起点，收到 {component?.GetType().Name}。");

        var state = new CompileState();
        var graphs = new List<CompiledGraph> { await CompileGraphAsync(start, state) };

        _graphs.Clear();
        foreach (var g in graphs) _graphs.Add(g);
        return graphs;
    }

    private async Task<CompiledGraph> CompileGraphAsync(
        IWorkflowNodeViewModel start, CompileState state)
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

            // 环路护栏：已编译节点不再处理，避免死循环/重复编译。
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

                // 路由表里的分支 = 活跃分支 → 编译成 BranchOption（分配 live orders）
                foreach (var kv in routeTable)
                {
                    if (kv.Value is null) continue;
                    foreach (var target in kv.Value)
                    {
                        if (target is null) continue;
                        var sub = await CompileGraphAsync(target, state);
                        options.Add(new BranchOption
                        {
                            Key = kv.Key,
                            Label = kv.Key?.ToString() ?? "?",
                            Graph = sub,
                        });
                        exits.Add(LastNode(sub));
                    }
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

                entries.Add(new BranchEntry { Router = node, Options = options, IsDynamic = isDynamic });

                // 分支后的汇合点：所有活跃分支出口共同指向的下一个节点。
                node = CommonNext(exits);
                resumedAfterBranch = node is not null;
                continue;
            }

            // 线性节点
            chain.Add(node);
            var next = SingleTarget(node);
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
        RetryEntry retry => LastNode(retry.Body),
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

    /// <summary>编译游标：全局序号计数器 + 已访问集合（避免 ref 参数，供异步递归共享）。</summary>
    private sealed class CompileState
    {
        public int Counter;
        public readonly HashSet<IWorkflowNodeViewModel> Visited = [];
    }
}
