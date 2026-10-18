using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.AI.Workflow;

// 每棵树一份的节点选中集，以及它的动态提示词渲染。选中集是调用时取的身份快照
// （RuntimeId + 类型名），渲染不读活模型；revision 由 WorkflowAgentScope.ContextKey 折进缓存键。
internal static class WorkflowAgentSelection
{
    // 一次提示词最多列出的节点数；计数仍是全量。避免 select-all 把每轮提示词撑大。
    internal const int MaxRenderedNodes = 64;

    // 一个被选中的节点，预先摊平，渲染时不碰模型。
    internal readonly struct Entry
    {
        internal Entry(string id, string typeName)
        {
            Id = id;
            TypeName = typeName;
        }

        internal string Id { get; }

        internal string TypeName { get; }
    }

    // 不可变快照；Add/Clear 整体替换，不改旧实例。
    private sealed class Snapshot
    {
        internal static readonly Snapshot Empty =
            new(Array.Empty<IWorkflowNodeViewModel>(), Array.Empty<Entry>(), 0L);

        internal Snapshot(IWorkflowNodeViewModel[] nodes, Entry[] entries, long revision)
        {
            Nodes = nodes;
            Entries = entries;
            Revision = revision;
        }

        internal IWorkflowNodeViewModel[] Nodes { get; }

        internal Entry[] Entries { get; }

        internal long Revision { get; }
    }

    // ConditionalWeakTable 的 value；Gate 只保护快照替换。
    private sealed class State
    {
        internal readonly object Gate = new();

        internal Snapshot Snapshot = Snapshot.Empty;

        // 非空选中集存在时挂着的 tree.Nodes.CollectionChanged 观察者；清空时摘掉。
        internal TreeObserver? Observer;
    }

    // 每棵树一份；树被回收时条目一起消失。
    private static readonly ConditionalWeakTable<IWorkflowTreeViewModel, State> States = new();

    // 节点 → 它当前所在选中集的那棵树。ConditionalWeakTable 在 netstandard2.0 上不可枚举，
    // 而节点删除后 Parent 已经是 null —— 这张反向表让「节点自己释放自己」仍能找到那棵树。
    // 值里放弱引用，避免节点活得比树久时把树拽住。
    private sealed class NodeOwner
    {
        internal WeakReference<IWorkflowTreeViewModel>? Tree;
    }

    private static readonly ConditionalWeakTable<IWorkflowNodeViewModel, NodeOwner> Owners = new();
    private static readonly object OwnersGate = new();

    // 从未选过时为 0。
    internal static long RevisionFor(IWorkflowTreeViewModel tree)
        => States.TryGetValue(tree, out var state) ? Volatile.Read(ref state.Snapshot).Revision : 0L;

    // 返回副本；调用方拿不到内部数组。
    internal static IReadOnlyList<IWorkflowNodeViewModel> Get(IWorkflowTreeViewModel tree)
    {
        if (!States.TryGetValue(tree, out var state))
            return Array.Empty<IWorkflowNodeViewModel>();

        var snapshot = Volatile.Read(ref state.Snapshot);
        if (snapshot.Nodes.Length == 0)
            return Array.Empty<IWorkflowNodeViewModel>();

        var copy = new IWorkflowNodeViewModel[snapshot.Nodes.Length];
        Array.Copy(snapshot.Nodes, copy, copy.Length);
        return copy;
    }

    // 并集语义：已在选中集里的节点不重复、不计数；返回本次新增的节点数。
    internal static int Add(IWorkflowTreeViewModel tree, IReadOnlyList<IWorkflowNodeViewModel> nodes)
    {
        if (nodes.Count == 0) return 0;

        var state = States.GetValue(tree, static _ => new State());

        lock (state.Gate)
        {
            var current = state.Snapshot;

            var combined = new List<IWorkflowNodeViewModel>(current.Nodes.Length + nodes.Count);
            var entries = new List<Entry>(current.Nodes.Length + nodes.Count);
            combined.AddRange(current.Nodes);
            entries.AddRange(current.Entries);

            var added = 0;
            for (var i = 0; i < nodes.Count; i++)
            {
                var node = nodes[i];
                if (node is null) continue;
                if (ContainsReference(combined, node)) continue;
                if (node is not IWorkflowIdentifiable) continue;

                combined.Add(node);
                entries.Add(EntryOf(node));
                added++;
            }

            if (added == 0) return 0;

            var next = combined.ToArray();
            Volatile.Write(ref state.Snapshot, new Snapshot(next, entries.ToArray(), current.Revision + 1));
            UpdateOwners(tree, current.Nodes, next);
            SyncSubscriptions(tree, state, current.Nodes, next);
            return added;
        }
    }

    // 清空该树的选中集；返回释放的节点数。没有选中集时返回 0，不推进 revision。
    internal static int Clear(IWorkflowTreeViewModel tree)
    {
        if (!States.TryGetValue(tree, out var state)) return 0;

        lock (state.Gate)
        {
            var current = state.Snapshot;
            if (current.Nodes.Length == 0) return 0;

            var count = current.Nodes.Length;
            var next = Array.Empty<IWorkflowNodeViewModel>();

            Volatile.Write(ref state.Snapshot,
                new Snapshot(next, Array.Empty<Entry>(), current.Revision + 1));
            UpdateOwners(tree, current.Nodes, next);
            SyncSubscriptions(tree, state, current.Nodes, next);
            return count;
        }
    }

    // 接在快照替换之后；锁顺序是 state → OwnersGate，Remove 不会同时持有两者。
    private static void UpdateOwners(
        IWorkflowTreeViewModel tree,
        IWorkflowNodeViewModel[] previous,
        IWorkflowNodeViewModel[] next)
    {
        lock (OwnersGate)
        {
            for (var i = 0; i < previous.Length; i++)
                if (!ContainsReference(next, previous[i])) Owners.Remove(previous[i]);

            for (var i = 0; i < next.Length; i++)
            {
                var owner = Owners.GetValue(next[i], static _ => new NodeOwner());
                owner.Tree = new WeakReference<IWorkflowTreeViewModel>(tree);
            }
        }
    }

    // 选中集里的节点订阅两条变更：Parent 属性改变（节点自己）与从 tree.Nodes 移除（父树）。
    // Deleted 是第三条语义事件；标准删除路径三者都会经过，Remove 幂等。
    // 这些订阅只观察工作流，不修改它。
    private static void SyncSubscriptions(
        IWorkflowTreeViewModel tree,
        State state,
        IWorkflowNodeViewModel[] previous,
        IWorkflowNodeViewModel[] next)
    {
        for (var i = 0; i < previous.Length; i++)
            if (!ContainsReference(next, previous[i])) Unobserve(previous[i]);

        for (var i = 0; i < next.Length; i++)
            if (!ContainsReference(previous, next[i])) Observe(next[i]);

        if (next.Length == 0)
        {
            if (state.Observer is { } observer)
            {
                observer.Detach(tree);
                state.Observer = null;
            }
        }
        else if (state.Observer is null)
        {
            state.Observer = new TreeObserver(tree);
            state.Observer.Attach(tree);
        }
    }

    private static void Observe(IWorkflowNodeViewModel node)
    {
        node.PropertyChanged += OnNodePropertyChanged;
        if (node.GetHelper() is IWorkflowNodeEvents events) events.Deleted += OnNodeDeleted;
    }

    private static void Unobserve(IWorkflowNodeViewModel node)
    {
        node.PropertyChanged -= OnNodePropertyChanged;
        if (node.GetHelper() is IWorkflowNodeEvents events) events.Deleted -= OnNodeDeleted;
    }

    private static void OnNodePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IWorkflowNodeViewModel.Parent)
            && sender is IWorkflowNodeViewModel node)
        {
            Remove(new[] { node });
        }
    }

    private static void OnNodeDeleted(object? sender, NodeEventArgs e) => Remove(new[] { e.Node });

    // tree.Nodes 的 Remove/Replace 带旧项；Reset（Clear）不带，直接清空该树的选中集。
    // 另外盯着 Nodes 属性本身：整条集合被换掉时旧身份不再有意义，也清空。
    // 只持弱引用，避免「树 → 集合 → 观察者 → 树」把树拽住。
    private sealed class TreeObserver
    {
        private readonly WeakReference<IWorkflowTreeViewModel> _tree;
        private ObservableCollection<IWorkflowNodeViewModel>? _nodes;

        internal TreeObserver(IWorkflowTreeViewModel tree) => _tree = new WeakReference<IWorkflowTreeViewModel>(tree);

        internal void Attach(IWorkflowTreeViewModel tree)
        {
            _nodes = tree.Nodes;
            _nodes.CollectionChanged += OnNodesChanged;
            tree.PropertyChanged += OnTreePropertyChanged;
        }

        internal void Detach(IWorkflowTreeViewModel tree)
        {
            if (_nodes is not null)
            {
                _nodes.CollectionChanged -= OnNodesChanged;
                _nodes = null;
            }

            tree.PropertyChanged -= OnTreePropertyChanged;
        }

        private void OnTreePropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(IWorkflowTreeViewModel.Nodes)) return;
            if (!_tree.TryGetTarget(out var tree)) return;

            if (_nodes is not null) _nodes.CollectionChanged -= OnNodesChanged;
            _nodes = tree.Nodes;
            _nodes.CollectionChanged += OnNodesChanged;

            Clear(tree);
        }

        internal void OnNodesChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (!_tree.TryGetTarget(out var tree)) return;

            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Remove:
                case NotifyCollectionChangedAction.Replace:
                    if (e.OldItems is null) return;

                    var removed = new List<IWorkflowNodeViewModel>(e.OldItems.Count);
                    foreach (var item in e.OldItems)
                        if (item is IWorkflowNodeViewModel node) removed.Add(node);

                    if (removed.Count > 0) Remove(removed);
                    break;

                case NotifyCollectionChangedAction.Reset:
                    Clear(tree);
                    break;
            }
        }
    }

    // 从反向表找到每棵树，再按引用从该树的快照里滤掉这些节点。节点已经删除（Parent 为 null）
    // 也能释放；返回实际移除的节点数，没有任何快照命中时不推进 revision。
    internal static int Remove(IReadOnlyCollection<IWorkflowNodeViewModel> nodes)
    {
        if (nodes.Count == 0) return 0;

        var groups = new List<(IWorkflowTreeViewModel Tree, List<IWorkflowNodeViewModel> Nodes)>();
        var touched = new List<(IWorkflowNodeViewModel Node, IWorkflowTreeViewModel Tree)>();

        // 只在这里持 OwnersGate 做分组，随后立刻释放；state 锁在下一段里单独拿。
        lock (OwnersGate)
        {
            foreach (var node in nodes)
            {
                if (node is null) continue;
                if (!Owners.TryGetValue(node, out var owner)) continue;
                if (owner.Tree is not { } weak || !weak.TryGetTarget(out var tree)) continue;

                var group = FindGroup(groups, tree);
                if (group is null)
                {
                    group = new List<IWorkflowNodeViewModel>();
                    groups.Add((tree, group));
                }

                group.Add(node);
                touched.Add((node, tree));
            }
        }

        var removed = 0;
        foreach (var group in groups)
            removed += RemoveFromTree(group.Tree, group.Nodes);

        if (touched.Count == 0) return removed;

        lock (OwnersGate)
        {
            foreach (var (node, tree) in touched)
            {
                // 并发的 Select 可能已经把节点换到别的树；只在归属没变时才忘掉它。
                if (Owners.TryGetValue(node, out var owner)
                    && owner.Tree is { } weak
                    && weak.TryGetTarget(out var current)
                    && ReferenceEquals(current, tree))
                {
                    Owners.Remove(node);
                }
            }
        }

        return removed;
    }

    private static int RemoveFromTree(
        IWorkflowTreeViewModel tree,
        IReadOnlyCollection<IWorkflowNodeViewModel> nodes)
    {
        if (!States.TryGetValue(tree, out var state)) return 0;

        lock (state.Gate)
        {
            var current = state.Snapshot;
            if (current.Nodes.Length == 0) return 0;

            var keptNodes = new List<IWorkflowNodeViewModel>(current.Nodes.Length);
            var keptEntries = new List<Entry>(current.Nodes.Length);

            for (var i = 0; i < current.Nodes.Length; i++)
            {
                if (ContainsReference(nodes, current.Nodes[i])) continue;

                keptNodes.Add(current.Nodes[i]);
                keptEntries.Add(current.Entries[i]);
            }

            var removed = current.Nodes.Length - keptNodes.Count;
            if (removed == 0) return 0;

            var next = keptNodes.ToArray();
            Volatile.Write(ref state.Snapshot,
                new Snapshot(next, keptEntries.ToArray(), current.Revision + 1));
            SyncSubscriptions(tree, state, current.Nodes, next);
            return removed;
        }
    }

    private static List<IWorkflowNodeViewModel>? FindGroup(
        List<(IWorkflowTreeViewModel Tree, List<IWorkflowNodeViewModel> Nodes)> groups,
        IWorkflowTreeViewModel tree)
    {
        foreach (var group in groups)
            if (ReferenceEquals(group.Tree, tree)) return group.Nodes;

        return null;
    }

    // 返回 Markdown 段；没有选中集时返回空串。标题与指令随提示词语言切换。
    internal static string Render(IWorkflowTreeViewModel tree, AgentLanguages language)
    {
        if (!States.TryGetValue(tree, out var state))
            return string.Empty;

        var snapshot = Volatile.Read(ref state.Snapshot);
        if (snapshot.Entries.Length == 0)
            return string.Empty;

        var chinese = language == AgentLanguages.Chinese;
        var total = snapshot.Entries.Length;
        var shown = Math.Min(total, MaxRenderedNodes);
        var sb = new StringBuilder(96 + shown * 48);

        sb.AppendLine(chinese ? "## 当前选中的节点" : "## Current Node Selection");
        sb.AppendLine();
        sb.AppendLine(chinese
            ? $"用户已在画布上选中 **{total}** 个节点。把这次选择当作本轮请求的工作集：优先操作这些节点；除非用户明确要求，不要改动其它节点。下面的清单只带身份 —— 当前下标、几何、槽位与属性请用 `GetNodeDetailById` / `ListNodes` 查询。"
            : $"The user has selected **{total}** {(total == 1 ? "node" : "nodes")} on the canvas. Treat this selection as the working set for this turn: prefer operations on these nodes, and do not modify other nodes unless the user explicitly asks. The list below carries identity only — use `GetNodeDetailById` / `ListNodes` for the current index, geometry, slots and properties.");
        sb.AppendLine();

        for (var i = 0; i < shown; i++)
        {
            var entry = snapshot.Entries[i];
            sb.Append("- `").Append(entry.Id).Append("` · ").AppendLine(entry.TypeName);
        }

        if (total > shown)
        {
            sb.AppendLine();
            sb.AppendLine(chinese
                ? $"> 只列出了前 {MaxRenderedNodes} 个；另有 {total - shown} 个节点也在选中集中。"
                : $"> Only the first {MaxRenderedNodes} selected nodes are listed; {total - shown} more are in the selection.");
        }

        sb.AppendLine();
        return sb.ToString();
    }

    // 工具按 RuntimeId 定位组件；没有稳定 ID 的节点只会生成模型无法执行的行，所以这里直接拒绝。
    private static Entry EntryOf(IWorkflowNodeViewModel node)
    {
        if (node is not IWorkflowIdentifiable identifiable)
        {
            throw new InvalidOperationException(
                $"'{node.GetType().Name}' does not implement IWorkflowIdentifiable — a stable RuntimeId "
                + "(provided by the component Helper) is required for the Agent selection.");
        }

        var typeName = AgentTypeNames.SimpleOf(node);
        if (typeName.Length == 0) typeName = node.GetType().Name;
        return new Entry(identifiable.RuntimeId, typeName);
    }

    private static bool ContainsReference(IReadOnlyCollection<IWorkflowNodeViewModel> nodes, IWorkflowNodeViewModel node)
    {
        foreach (var candidate in nodes)
            if (ReferenceEquals(candidate, node)) return true;

        return false;
    }
}
