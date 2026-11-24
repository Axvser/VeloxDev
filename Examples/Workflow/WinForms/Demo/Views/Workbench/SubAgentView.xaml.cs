using System.ComponentModel;
using System.Drawing;
using Easel.Visuals;
using Easel.Visuals.Controls;
using VeloxDev.AI.SubAgents;
using TreeView = Easel.Visuals.Controls.TreeView;

namespace Demo.Views.Workbench;

/// <summary>
/// The AI inspector's 子代理 card: the Agent's dispatched children at every depth, with their clocks and tokens.
/// The tree view model owns the roster and the reconcile; this view only renders it and supplies the one-second
/// host clock the library deliberately does not own.
/// </summary>
public sealed partial class SubAgentView : WorkbenchViewBase
{
    private readonly System.Windows.Forms.Timer _tick;
    private SubAgentTreeViewModel? _tree;
    private bool _refreshQueued;

    /// <summary>Creates the card from XAML.</summary>
    public SubAgentView()
    {
        InitializeComponent();

        _tick = new System.Windows.Forms.Timer { Interval = 1000 };
        _tick.Tick += (_, _) => OnTick();
        _tick.Start();

        RefreshCounts();
        RefreshTree();
    }

    /// <summary>Binds the card to a scope, or unbinds it when the session has no sub-agent support.</summary>
    public void SetScope(SubAgentScope? scope)
    {
        if (_tree is not null)
        {
            _tree.PropertyChanged -= OnTreePropertyChanged;
            _tree.Dispose();
            _tree = null;
        }

        if (scope is not null)
        {
            _tree = new SubAgentTreeViewModel(scope);
            _tree.PropertyChanged += OnTreePropertyChanged;
        }

        Visibility = _tree is not null ? Easel.Controls.Visibility.Visible : Easel.Controls.Visibility.Collapsed;
        RefreshCounts();
        RefreshTree();
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _tick.Stop();
            _tick.Dispose();
            if (_tree is not null)
            {
                _tree.PropertyChanged -= OnTreePropertyChanged;
                _tree.Dispose();
                _tree = null;
            }
        }

        base.Dispose(disposing);
    }

    private void OnTreePropertyChanged(object? sender, PropertyChangedEventArgs e) => QueueRefresh();

    private void OnTick()
    {
        if (_tree is null)
        {
            return;
        }

        _tree.TickElapsed();
        QueueRefresh();
    }

    private void QueueRefresh()
    {
        if (_refreshQueued)
        {
            return;
        }

        _refreshQueued = true;
        WorkbenchDispatcher.Post(() =>
        {
            _refreshQueued = false;
            RefreshCounts();
            RefreshTree();
        });
    }

    private void RefreshCounts()
    {
        var tree = _tree;
        RunningCount.Visibility = tree is { IsIdle: false } ? Easel.Controls.Visibility.Visible : Easel.Controls.Visibility.Collapsed;
        RunningCount.Text = $"运行中 {tree?.RunningCount ?? 0}";
        TotalCount.Text = $"{tree?.TotalCount ?? 0} 个";
        TokensCount.Visibility = tree?.ScopeRoot.HasTokens == true ? Easel.Controls.Visibility.Visible : Easel.Controls.Visibility.Collapsed;
        TokensCount.Text = $"· {tree?.SubtreeTokensText ?? string.Empty} tokens";
        FailedCount.Visibility = tree?.HasFailed == true ? Easel.Controls.Visibility.Visible : Easel.Controls.Visibility.Collapsed;
        FailedCount.Text = $"· 失败 {tree?.FailedCount ?? 0}";
        EmptyHint.Visibility = tree is null || tree.IsEmpty ? Easel.Controls.Visibility.Visible : Easel.Controls.Visibility.Collapsed;
        AgentTree.Visibility = tree is not null && !tree.IsEmpty ? Easel.Controls.Visibility.Visible : Easel.Controls.Visibility.Collapsed;
    }

    private void RefreshTree()
    {
        AgentTree.Items.Clear();
        if (_tree is not null && !_tree.IsEmpty)
        {
            foreach (var child in _tree.ScopeRoot.Children)
            {
                AgentTree.Items.Add(BuildNode(child));
            }
        }
    }

    private static TreeViewItem BuildNode(SubAgentTreeNodeViewModel node)
    {
        var text = node.Title;
        if (!node.IsScopeRoot)
        {
            var details = new List<string>();
            if (node.HasDuration) details.Add(node.DurationText);
            if (node.HasTokens) details.Add(node.TokensText);
            if (node.ShowStateText) details.Add(node.StateText);
            if (node.HasDroppedRequests) details.Add($"有 {node.DroppedRequestCount} 项请求被拒");
            if (details.Count > 0) text = $"{node.Title}   {string.Join(" · ", details)}";
        }

        var item = new TreeViewItem
        {
            Header = text,
            Foreground = NodeColor(node),
            FontWeight = node.IsScopeRoot ? FontWeight.Bold : FontWeight.Normal,
            IsExpanded = true,
        };

        foreach (var child in node.Children)
        {
            item.Items.Add(BuildNode(child));
        }

        return item;
    }

    private static Color NodeColor(SubAgentTreeNodeViewModel node) => node switch
    {
        { IsScopeRoot: true } => WorkbenchPalette.AccentText,
        { IsRunning: true } => WorkbenchPalette.Success,
        _ when node.Row?.State == SubAgentState.Failed => WorkbenchPalette.Danger,
        _ when node.Row?.State == SubAgentState.Cancelled => WorkbenchPalette.TextTertiary,
        _ => WorkbenchPalette.TextSecondary,
    };
}
