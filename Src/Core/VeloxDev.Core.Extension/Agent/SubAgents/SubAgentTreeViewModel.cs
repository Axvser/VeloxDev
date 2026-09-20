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
/// </summary>
public sealed partial class SubAgentTreeNodeViewModel(SubAgentStatusViewModel row, SubAgentTreeNodeViewModel? parent)
{
    [VeloxProperty] private bool isExpanded = true;
    [VeloxProperty] private string expandGlyph = ExpandedGlyph;

    private const string ExpandedGlyph = "▾";
    private const string CollapsedGlyph = "▸";

    /// <summary>The row this node shows. Its own properties are the node's — nothing is copied.</summary>
    public SubAgentStatusViewModel Row { get; } = row;

    /// <summary>The node that spawned this one, or <c>null</c> at the top of the tree.</summary>
    public SubAgentTreeNodeViewModel? Parent { get; internal set; } = parent;

    /// <summary>The sub-agents this one dispatched, in spawn order.</summary>
    public ObservableCollection<SubAgentTreeNodeViewModel> Children { get; } = [];

    /// <summary>The handle, forwarded so a template can bind it without reaching through <see cref="Row"/>.</summary>
    public string Id => Row.Id;

    /// <summary>How deep this node sits, 1 for a direct child of the panel's scope.</summary>
    public int Depth => Row.Depth;

    /// <summary>Whether there is anything to expand into.</summary>
    public bool HasChildren => Children.Count > 0;

    partial void OnIsExpandedChanged(bool oldValue, bool newValue)
        => ExpandGlyph = newValue ? ExpandedGlyph : CollapsedGlyph;

    /// <summary>Shows or hides this node's children.</summary>
    public void ToggleExpand() => IsExpanded = !IsExpanded;

    internal void NotifyHasChildren() => OnPropertyChanged(nameof(HasChildren));
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
/// unrelated child changing state.
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

    private List<SubAgentStatusViewModel> _flat = [];
    private bool _rebuildQueued;
    private bool _disposed;

    [VeloxProperty] private ObservableCollection<SubAgentTreeNodeViewModel> roots = [];

    /// <summary>
    /// The tree's top level, one node per sub-agent dispatched by the scope this was built on. Deeper
    /// levels hang off each node's <see cref="SubAgentTreeNodeViewModel.Children"/>.
    /// </summary>
    // Roots is generated from the field above.

    /// <summary>Builds a tree over <paramref name="scope"/> and renders it once.</summary>
    public SubAgentTreeViewModel(SubAgentScope scope)
    {
        _scope = scope ?? throw new ArgumentNullException(nameof(scope));
        Rebuild();
    }

    /// <summary>Every sub-agent in the tree, at every depth.</summary>
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

    /// <summary>Rebuilds the tree from the scopes as they stand, on the calling thread.</summary>
    public void Rebuild()
    {
        if (_disposed) return;

        var flat = new List<SubAgentStatusViewModel>();
        Watch(_scope);
        Fill(Roots, _scope, null, flat);
        _flat = flat;

        NotifyCounts();
    }

    /// <summary>
    /// Projection of one scope's roster onto one level of the tree, merging into the nodes already there.
    /// </summary>
    private void Fill(
        ObservableCollection<SubAgentTreeNodeViewModel> level,
        SubAgentScope scope,
        SubAgentTreeNodeViewModel? parent,
        List<SubAgentStatusViewModel> flat)
    {
        var existing = level.ToDictionary(node => node.Id, StringComparer.Ordinal);
        level.Clear();

        foreach (var row in scope.Children)
        {
            var node = existing.TryGetValue(row.Id, out var kept)
                ? kept
                : new SubAgentTreeNodeViewModel(row, parent);

            node.Parent = parent;
            flat.Add(row);

            if (scope.SubAgentsOf(row.Id) is { } childScope)
            {
                Watch(childScope);
                Fill(node.Children, childScope, node, flat);
            }
            else
            {
                node.Children.Clear();
            }

            node.NotifyHasChildren();
            level.Add(node);
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
        if (_disposed || _rebuildQueued) return;
        _rebuildQueued = true;

        if (_ui is null || ReferenceEquals(_ui, SynchronizationContext.Current))
        {
            Drain();
            return;
        }

        _ui.Post(_ => Drain(), null);
    }

    private void Drain()
    {
        _rebuildQueued = false;
        Rebuild();
    }

    /// <summary>
    /// Detaches from every scope it was watching. The sub-agents keep running — see the type's remarks.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var pair in _watched) pair.Key.Changed -= pair.Value;
        _watched.Clear();

        // The counts go with the nodes. They are derived from the last rebuild, and a disposed panel can
        // never be refreshed into agreement — Rebuild returns early once disposed — so a total left standing
        // would describe children this panel no longer holds, for good.
        Roots.Clear();
        _flat = [];
        NotifyCounts();
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
    }
}
