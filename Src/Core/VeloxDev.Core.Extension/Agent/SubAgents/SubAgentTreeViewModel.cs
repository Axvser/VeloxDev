using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using VeloxDev.MVVM;

namespace VeloxDev.AI.SubAgents;

/// <summary>
/// One node of the sub-agent tree: a row, and the nodes of the sub-agents that row spawned.
/// <para>
/// The roster a scope keeps is flat and holds only its own direct children, which is what keeps one agent
/// from seeing another's. The tree is therefore a projection of several such rosters, hung off one another
/// by the child scope each row stands for — not a second source of truth, and never mutated here.
/// </para>
/// <para>
/// The top node stands for the scope itself and has no <see cref="Row"/> — see
/// <see cref="SubAgentTreeViewModel.ScopeRoot"/>. Everything a node reports about its own run forwards to its
/// row, so <see cref="Row"/> being <c>null</c> is the one case a consumer has to handle.
/// </para>
/// </summary>
public sealed partial class SubAgentTreeNodeViewModel
{
    [VeloxProperty] private bool isExpanded = true;
    [VeloxProperty] private string expandGlyph = ExpandedGlyph;

    // 顶节点给「作用域自身」显示的名字。宿主可设；对其余节点没有意义。
    [VeloxProperty] private string scopeTitle = DefaultScopeTitle;

    // 作用域自己那一份消耗，宿主知道就填。库测不出来 —— 作用域的 agent 是宿主的，只有宿主的对话产生它的用量。
    // 留 null 时顶节点退回去显示子树合计：那是诚实的数字，而不是对宿主用量的猜测。
    [VeloxProperty] private long? scopeTokens = null;

    // 自下而上算出的合计。字段用小写直读，是因为 RecomputeAggregates 里要连算几个节点，
    // 每读一次就发一次通知只会让面板白刷。
    [VeloxProperty] private long subtreeTokens = 0;
    [VeloxProperty] private int subtreeCallCount = 0;

    private const string ExpandedGlyph = "▾";
    private const string CollapsedGlyph = "▸";

    // The scope root's handle. A real child's id is a GUID, so this cannot collide — which matters because
    // the reconcile matches nodes by id.
    internal const string ScopeRootId = "__scope__";

    private const string DefaultScopeTitle = "本会话";

    /// <summary>Builds the node for one dispatched sub-agent.</summary>
    /// <param name="row">The roster row this node shows. Its own properties are the node's — nothing is copied.</param>
    /// <param name="parent">The node that spawned this one, or <c>null</c> at the top of the tree.</param>
    public SubAgentTreeNodeViewModel(SubAgentStatusViewModel row, SubAgentTreeNodeViewModel? parent)
    {
        Row = row ?? throw new ArgumentNullException(nameof(row));
        Parent = parent;
    }

    // The scope root: same node type, so the view has one template, but there is no row behind it. The id is
    // fixed rather than generated, because the node survives every rebuild and the reconcile keys on it.
    private SubAgentTreeNodeViewModel(string title)
    {
        scopeTitle = title;
        Parent = null;
    }

    internal static SubAgentTreeNodeViewModel CreateScopeRoot(string title) => new(title);

    /// <summary>
    /// The row this node shows, or <c>null</c> when the node is the scope itself.
    /// </summary>
    public SubAgentStatusViewModel? Row { get; }

    /// <summary>The node that spawned this one, or <c>null</c> at the top of the tree.</summary>
    public SubAgentTreeNodeViewModel? Parent { get; internal set; }

    /// <summary>The sub-agents this one dispatched, in spawn order.</summary>
    public ObservableCollection<SubAgentTreeNodeViewModel> Children { get; } = [];

    /// <summary>The handle, forwarded so a template can bind it without reaching through <see cref="Row"/>.</summary>
    public string Id => Row?.Id ?? ScopeRootId;

    /// <summary>How deep this node sits, 0 for the scope itself and 1 for a direct child of it.</summary>
    public int Depth => Row?.Depth ?? 0;

    /// <summary>Whether this node stands for the scope rather than for a dispatched sub-agent.</summary>
    public bool IsScopeRoot => Row is null;

    /// <summary>The title the row shows: what the spawn named the task, or the scope's own name at the top.</summary>
    public string Title => Row?.Name ?? ScopeTitle;

    /// <summary>Whether there is anything to expand into.</summary>
    public bool HasChildren => Children.Count > 0;

    /// <summary>
    /// The tokens this node's own run spent, or <c>null</c> when nothing measured it.
    /// </summary>
    /// <remarks>
    /// For a sub-agent this is what its own runs cost — not its subtree's. The two are deliberately separate,
    /// because a parent that reports only its own number hides the work beneath it and a parent that reports
    /// the sum makes the column impossible to total. See <see cref="SubtreeTokens"/> for the other half.
    /// </remarks>
    public long? TokensUsed => Row?.TokensUsed ?? ScopeTokens;

    /// <summary>The subtree total as it is shown: abbreviated, like <see cref="TokensText"/>.</summary>
    public string SubtreeTokensText => Abbreviate(subtreeTokens);

    /// <summary>Whether there is a token figure to show, own or inherited from the subtree.</summary>
    public bool HasTokens => TokensUsed is not null || subtreeTokens > 0;

    /// <summary>
    /// The token figure the row shows: this node's own spend, or the subtree total when nothing measured it.
    /// </summary>
    public string TokensText => Abbreviate(TokensUsed ?? subtreeTokens);

    /// <summary>
    /// Whether the subtree total is worth printing beside <see cref="TokensText"/> — true only for a node that
    /// has both a measured spend of its own and descendants that spent more.
    /// </summary>
    public bool ShowSubtreeTokens => TokensUsed is not null && subtreeTokens > TokensUsed;

    /// <summary>How long this node's own run took, or <c>null</c> before it started or for the scope.</summary>
    public TimeSpan? Duration => Row?.Duration;

    /// <summary>The elapsed time as text, empty when there is nothing to show.</summary>
    public string DurationText => Row?.DurationText ?? string.Empty;

    /// <summary>Whether there is an elapsed time to show.</summary>
    public bool HasDuration => Row?.Duration is not null;

    /// <summary>The row's state as text, empty for the scope — which has no run of its own to describe.</summary>
    public string StateText => Row?.StateText ?? string.Empty;

    /// <summary>
    /// Whether the state is worth printing beside the lamp.
    /// </summary>
    /// <remarks>
    /// The lamp already carries "finished" — it is the state most rows are in, and a grey lamp on every one of
    /// them says it once instead of once per row. What the lamp cannot distinguish is anything else: a running
    /// child breathes, but not whether it is queued or working; a stopped one is grey either way, whether it
    /// died or was called off. Those are the states that earn the words.
    /// </remarks>
    public bool ShowStateText => Row is { State: not SubAgentState.Completed };

    /// <summary>Whether this node's own run is still going. Always false for the scope.</summary>
    public bool IsRunning => Row?.IsRunning ?? false;

    /// <summary>Tool calls made by this node's own run, or the subtree's when it has none of its own.</summary>
    public int CallCount => Row?.CallCount ?? subtreeCallCount;

    /// <summary>Whether this node's own spawn asked for something it did not get.</summary>
    public bool HasDroppedRequests => Row?.HasDroppedRequests ?? false;

    /// <summary>How many requests were refused, when <see cref="HasDroppedRequests"/> is true.</summary>
    public int DroppedRequestCount => Row?.DroppedRequests.Count ?? 0;

    /// <summary>Announces that this node's row moved, for the members that forward to it.</summary>
    /// <remarks>
    /// <para>
    /// Called from the rebuild for every node it touches, because a node does not subscribe to its own row —
    /// the scope does, and the rebuild is what that subscription drives.
    /// </para>
    /// <para>
    /// A template should bind these members rather than reaching through <see cref="Row"/>: the scope root
    /// has no row, and every member here forwards to it, so a path that starts at the row would point at
    /// nothing for exactly the node the panel is built around.
    /// </para>
    /// </remarks>
    internal void NotifyRow()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(StateText));
        OnPropertyChanged(nameof(ShowStateText));
        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(CallCount));
        OnPropertyChanged(nameof(HasDroppedRequests));
        OnPropertyChanged(nameof(DroppedRequestCount));
        OnPropertyChanged(nameof(Duration));
        OnPropertyChanged(nameof(DurationText));
        OnPropertyChanged(nameof(HasDuration));
    }

    /// <summary>Shows or hides this node's children.</summary>
    public void ToggleExpand() => IsExpanded = !IsExpanded;

    partial void OnIsExpandedChanged(bool oldValue, bool newValue)
        => ExpandGlyph = newValue ? ExpandedGlyph : CollapsedGlyph;

    partial void OnScopeTitleChanged(string oldValue, string newValue) => OnPropertyChanged(nameof(Title));

    partial void OnScopeTokensChanged(long? oldValue, long? newValue)
    {
        // 重算而不只是发通知：宿主通常在树建好之后才填这个数，而合计是上一次 Rebuild 时算的。
        // RecomputeAggregates 会把它自己那几个派生属性一起广播，所以这里只补 TokensUsed。
        OnPropertyChanged(nameof(TokensUsed));
        RecomputeAggregates();
    }

    partial void OnSubtreeTokensChanged(long oldValue, long newValue)
    {
        OnPropertyChanged(nameof(SubtreeTokensText));
        OnPropertyChanged(nameof(TokensText));
        OnPropertyChanged(nameof(HasTokens));
        OnPropertyChanged(nameof(ShowSubtreeTokens));
    }

    internal void NotifyHasChildren() => OnPropertyChanged(nameof(HasChildren));

    /// <summary>
    /// Tells a bound panel that the clock moved. The row does the same for itself; this is for a template that
    /// binds the node rather than the row.
    /// </summary>
    internal void NotifyElapsed()
    {
        OnPropertyChanged(nameof(Duration));
        OnPropertyChanged(nameof(DurationText));
    }

    /// <summary>
    /// Recomputes the subtree totals from this node's own figures plus its children's.
    /// <para>
    /// Called after the children have been filled, so the recursion is bottom-up by construction: a child's
    /// total is final before its parent reads it.
    /// </para>
    /// </summary>
    internal void RecomputeAggregates()
    {
        long tokens = TokensUsed ?? 0;
        int calls = Row?.CallCount ?? 0;

        foreach (var child in Children)
        {
            tokens += child.subtreeTokens;
            calls += child.subtreeCallCount;
        }

        subtreeTokens = tokens;
        subtreeCallCount = calls;

        OnPropertyChanged(nameof(SubtreeTokens));
        OnPropertyChanged(nameof(SubtreeTokensText));
        OnPropertyChanged(nameof(SubtreeCallCount));
        OnPropertyChanged(nameof(TokensText));
        OnPropertyChanged(nameof(HasTokens));
        OnPropertyChanged(nameof(ShowSubtreeTokens));
    }

    // 与 SubAgentStatusViewModel.TokensText 同一套缩写。两处都留，是因为它们的输入不同：
    // 那边缩写自己的 TokensUsed，这边要缩写节点上的合计，且节点没有 Row 时也要能用。
    private static string Abbreviate(long value) => value switch
    {
        <= 0 => string.Empty,
        < 1000 => $"{value}",
        < 1_000_000 => $"{value / 1000d:0.#}k",
        _ => $"{value / 1_000_000d:0.##}M",
    };
}

/// <summary>
/// The sub-agent relationship graph, as a bindable tree.
/// <para>
/// Live rather than snapshotted: every scope in the tree raises <see cref="SubAgentScope.Changed"/> when its
/// roster or any of its rows moves, and a rebuild is queued on the thread this was created on. Rebuilds are
/// merged, so a child starting, a child finishing and a grandchild appearing in the same instant cost one
/// pass rather than three.
/// </para>
/// <para>
/// Node identity survives a rebuild: a node already showing a row is kept and re-parented rather than
/// replaced, so an expanded node stays expanded and a template's selection is not thrown away by an
/// unrelated child changing state. The reconcile also moves rather than rebuilds, so a change to one child
/// does not tear down the containers of its siblings.
/// </para>
/// <para>
/// <see cref="Dispose"/> detaches from the scopes and <b>does not cancel anything</b>. Closing a panel is
/// not a decision about the work the panel was showing; cancelling is
/// <see cref="SubAgentScope.Cancel"/>'s or the scope's own disposal's to make.
/// </para>
/// </summary>
public sealed partial class SubAgentTreeViewModel : IDisposable
{
    private readonly SubAgentScope _scope;
    private readonly SynchronizationContext? _ui = SynchronizationContext.Current;

    // Every scope visited by the last rebuild, so a change anywhere in the tree queues one. Reconciled
    // during the walk rather than subscribed up front: children that do not exist yet cannot be named.
    private readonly Dictionary<SubAgentScope, EventHandler> _watched = [];

    /// <summary>
    /// Serializes rebuilds. The class-wide assumption is that they happen on one thread — the one this was
    /// created on — and the queue below only *delivers* on that thread. With no context to post to (a panel
    /// built off the UI thread, which is every test and any headless host) the rebuild runs inline on
    /// whichever scope raised the change, and two children finishing in the same instant are two threads in
    /// <see cref="Fill"/>. That is not a rare interleaving: it is what a fan-out does by construction, and
    /// the corruption it causes lands as an exception inside a child's own run rather than here.
    /// </summary>
    private readonly object _rebuildGate = new();

    private List<SubAgentStatusViewModel> _flat = [];
    private bool _rebuildQueued;
    private bool _disposed;

    // 树顶：恰好一个节点（作用域自身），供只渲染第一层的层级控件使用；更深层挂在各节点的 Children 上。
    // 它必须是 [VeloxProperty]：本类的 INotifyPropertyChanged 基建由生成器按「有没有 VeloxProperty 成员」
    // 决定，去掉它就是去掉 OnPropertyChanged —— 而下面的计数全是派生属性，没有它就一个都发不出去。
    [VeloxProperty] private ObservableCollection<SubAgentTreeNodeViewModel> tree = [];

    /// <summary>Builds a tree over <paramref name="scope"/> and renders it once.</summary>
    public SubAgentTreeViewModel(SubAgentScope scope)
    {
        _scope = scope ?? throw new ArgumentNullException(nameof(scope));
        ScopeRoot = SubAgentTreeNodeViewModel.CreateScopeRoot(DefaultScopeTitle);
        Tree.Add(ScopeRoot);
        Rebuild();
    }

    private const string DefaultScopeTitle = "本会话";

    /// <summary>
    /// The node standing for the scope itself: the single top of <see cref="Tree"/>, and the parent of every
    /// node in <see cref="Roots"/>.
    /// </summary>
    public SubAgentTreeNodeViewModel ScopeRoot { get; }

    /// <summary>
    /// The sub-agents the scope dispatched, one node each — the same nodes as
    /// <see cref="SubAgentTreeNodeViewModel.Children"/> of <see cref="ScopeRoot"/>.
    /// </summary>
    public ObservableCollection<SubAgentTreeNodeViewModel> Roots => ScopeRoot.Children;

    /// <summary>Every sub-agent in the tree, at every depth. The scope itself is not counted.</summary>
    public int TotalCount => _flat.Count;

    /// <summary>How many are still working.</summary>
    public int RunningCount => _flat.Count(r => r.IsRunning);

    /// <summary>How many finished with a result.</summary>
    public int CompletedCount => _flat.Count(r => r.State == SubAgentState.Completed);

    /// <summary>How many failed.</summary>
    public int FailedCount => _flat.Count(r => r.State == SubAgentState.Failed);

    /// <summary>How many were cancelled.</summary>
    public int CancelledCount => _flat.Count(r => r.State == SubAgentState.Cancelled);

    /// <summary>Nothing has been dispatched yet.</summary>
    public bool IsEmpty => _flat.Count == 0;

    /// <summary>Nothing is still running.</summary>
    public bool IsIdle => RunningCount == 0;

    /// <summary>At least one sub-agent failed.</summary>
    public bool HasFailed => FailedCount > 0;

    /// <summary>Tokens spent by every sub-agent in the tree, at every depth.</summary>
    public long SubtreeTokens => ScopeRoot.SubtreeTokens;

    /// <summary>The same total as it is shown, for a header that prints it inline.</summary>
    public string SubtreeTokensText => ScopeRoot.SubtreeTokensText;

    /// <summary>
    /// Refreshes the elapsed time on every running node, for a panel that shows a clock.
    /// <para>
    /// The library owns no timer: a panel that ticks and a process that hosts one have different lifetimes,
    /// and a timer started here would belong to neither. The host drives this from whatever it already has —
    /// a dispatcher timer, a frame pacer — at whatever rate it finds readable.
    /// </para>
    /// </summary>
    public void TickElapsed()
    {
        foreach (var node in Walk())
        {
            if (node.Row is { IsRunning: true } row) row.NotifyElapsed();
            node.NotifyElapsed();
        }
    }

    /// <summary>
    /// Rebuilds the tree from the scopes as they stand, on the calling thread. Serialized against every
    /// other rebuild, so a caller does not have to be the thread the tree was created on to be safe.
    /// </summary>
    public void Rebuild()
    {
        lock (_rebuildGate)
        {
            if (_disposed) return;

            var flat = new List<SubAgentStatusViewModel>();
            Watch(_scope);
            Fill(ScopeRoot.Children, _scope, ScopeRoot, flat);
            ScopeRoot.RecomputeAggregates();
            _flat = flat;

            NotifyCounts();
        }
    }

    /// <summary>
    /// Projection of one scope's roster onto one level of the tree, merging into the nodes already there.
    /// <para>
    /// Merging, not replacing: a level is reconciled in place so that only the rows that actually appeared or
    /// vanished reach the view as an add or a remove. Clearing the collection instead would publish a reset
    /// on every rebuild, and the roster republishes on every property write of every row — so a reset would
    /// throw away and rebuild every container in the panel several times per child.
    /// </para>
    /// </summary>
    private void Fill(
        ObservableCollection<SubAgentTreeNodeViewModel> level,
        SubAgentScope scope,
        SubAgentTreeNodeViewModel? parent,
        List<SubAgentStatusViewModel> flat)
    {
        var rows = scope.Children;
        var existing = new Dictionary<string, SubAgentTreeNodeViewModel>(StringComparer.Ordinal);
        foreach (var node in level) existing[node.Id] = node;

        // Departures first, walking backwards so a removal cannot skip the node after it. Looked up by id
        // rather than by reference, because a row that a spawn replaced keeps its id but not its instance.
        var alive = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in rows) alive.Add(row.Id);
        for (int i = level.Count - 1; i >= 0; i--)
            if (!alive.Contains(level[i].Id)) level.RemoveAt(i);

        // Then arrivals and reordering. IndexOf is linear and this is quadratic in the width of one level;
        // that is the right trade at the widths a fan-out reaches, and the alternative — an index maintained
        // across the mutations below — is the kind of bookkeeping that is wrong exactly when it matters.
        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];

            if (!existing.TryGetValue(row.Id, out var node))
            {
                node = new SubAgentTreeNodeViewModel(row, parent);
                level.Insert(i, node);
            }
            else
            {
                var at = IndexOf(level, row.Id);
                if (at != i) level.Move(at, i);
            }

            node.Parent = parent;
            flat.Add(row);

            if (scope.SubAgentsOf(row.Id) is { } childScope)
            {
                Watch(childScope);
                Fill(node.Children, childScope, node, flat);
            }
            else if (node.Children.Count > 0)
            {
                // A child that had grandchildren and no longer does — its scope is gone, so the nodes under
                // it describe nothing. Cleared rather than left, or the subtree totals would keep counting
                // agents that are no longer part of the tree.
                node.Children.Clear();
            }

            // After the children, so the recursion is bottom-up.
            node.RecomputeAggregates();
            node.NotifyHasChildren();
            node.NotifyRow();
        }
    }

    private static int IndexOf(ObservableCollection<SubAgentTreeNodeViewModel> level, string id)
    {
        for (int i = 0; i < level.Count; i++)
            if (string.Equals(level[i].Id, id, StringComparison.Ordinal)) return i;
        return -1;
    }

    /// <summary>Every node in the tree, the scope root first.</summary>
    private IEnumerable<SubAgentTreeNodeViewModel> Walk()
    {
        var stack = new Stack<SubAgentTreeNodeViewModel>();
        stack.Push(ScopeRoot);

        while (stack.Count > 0)
        {
            var node = stack.Pop();
            yield return node;

            foreach (var child in node.Children) stack.Push(child);
        }
    }

    private void Watch(SubAgentScope scope)
    {
        if (_watched.ContainsKey(scope)) return;
        EventHandler handler = (_, _) => QueueRebuild();
        _watched[scope] = handler;
        scope.Changed += handler;
    }

    private void QueueRebuild()
    {
        // Checked and set under the same gate the rebuild takes, or two threads raising at once would both
        // see an unqueued flag, both queue, and the coalescing would guarantee nothing.
        lock (_rebuildGate)
        {
            if (_disposed || _rebuildQueued) return;
            _rebuildQueued = true;

            if (_ui is null || ReferenceEquals(_ui, SynchronizationContext.Current))
            {
                Drain();
                return;
            }
        }

        _ui.Post(_ => Drain(), null);
    }

    private void Drain()
    {
        lock (_rebuildGate)
        {
            _rebuildQueued = false;
            Rebuild();
        }
    }

    /// <summary>
    /// Detaches from every scope it was watching. The sub-agents keep running — see the type's remarks.
    /// </summary>
    public void Dispose()
    {
        lock (_rebuildGate)
        {
            if (_disposed) return;
            _disposed = true;

            foreach (var pair in _watched) pair.Key.Changed -= pair.Value;
            _watched.Clear();

            // The counts go with the nodes. They are derived from the last rebuild, and a disposed panel can
            // never be refreshed into agreement — Rebuild returns early once disposed — so a total left standing
            // would describe children this panel no longer holds, for good.
            //
            // The scope root stays in Tree: it is this panel's own node, not the roster's, and a second
            // Rebuild is impossible, so nothing can repopulate it with children the panel does not hold.
            Roots.Clear();
            _flat = [];
            ScopeRoot.RecomputeAggregates();
            NotifyCounts();
        }
    }

    private void NotifyCounts()
    {
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(RunningCount));
        OnPropertyChanged(nameof(CompletedCount));
        OnPropertyChanged(nameof(FailedCount));
        OnPropertyChanged(nameof(CancelledCount));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(IsIdle));
        OnPropertyChanged(nameof(HasFailed));
        OnPropertyChanged(nameof(SubtreeTokens));
        OnPropertyChanged(nameof(SubtreeTokensText));
    }
}
