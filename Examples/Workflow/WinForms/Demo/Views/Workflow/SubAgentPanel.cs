using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using Demo.Views.Workbench;
using VeloxDev.AI.SubAgents;

namespace Demo.Views.Workflow;

/// <summary>
/// The AI inspector's 子代理 card: the Agent's dispatched children at every depth, with their clocks and tokens.
///
/// The tree view model owns the roster and the reconcile; this panel only renders it. It is rebuilt on the
/// model's change notification and on a one-second host clock — the library deliberately owns no timer, so the
/// demo supplies the one it already has, the same arrangement the Avalonia panel uses.
/// </summary>
internal sealed class SubAgentPanel : FramePanel
{
    private readonly Label _running;
    private readonly Label _total;
    private readonly Label _tokens;
    private readonly Label _failed;
    private readonly Label _empty;
    private readonly TreeView _treeView;
    private readonly System.Windows.Forms.Timer _tick;

    private SubAgentTreeViewModel? _tree;
    private bool _refreshQueued;

    public SubAgentPanel()
    {
        Fill = WorkbenchPalette.Surface;
        Frame = WorkbenchPalette.Border;
        Ground = WorkbenchPalette.Panel;
        Radius = 6;
        FrameWidth = 1;
        Padding = new Padding(10);
        Height = 220;
        Visible = false;

        var title = new Label
        {
            AutoSize = true,
            Text = "子代理",
            ForeColor = WorkbenchPalette.AccentText,
            BackColor = Color.Transparent,
            Font = CardTheme.Font(9f, FontStyle.Bold),
            Margin = new Padding(0, 0, 8, 0),
        };

        _running = CreateCount("运行中 0", WorkbenchPalette.Success);
        _total = CreateCount("0 个", WorkbenchPalette.TextTertiary);
        _tokens = CreateCount("· 0 tokens", CardTheme.FromHex("#C99BFF"));
        _failed = CreateCount("· 失败 0", WorkbenchPalette.Danger);

        var counts = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        counts.Controls.Add(_running);
        counts.Controls.Add(_total);
        counts.Controls.Add(_tokens);
        counts.Controls.Add(_failed);

        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            RowCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 0, 0, 6),
            Padding = Padding.Empty,
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        header.Controls.Add(title, 0, 0);
        header.Controls.Add(counts, 1, 0);

        _empty = new Label
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            MaximumSize = new Size(286, 0),
            Text = "尚无子代理。让 Agent「派一个子代理去数一数节点」试试。",
            ForeColor = WorkbenchPalette.TextTertiary,
            BackColor = Color.Transparent,
            Font = CardTheme.Font(8f),
            Margin = new Padding(0, 0, 0, 4),
        };

        _treeView = new TreeView
        {
            Dock = DockStyle.Fill,
            BackColor = WorkbenchPalette.Surface,
            ForeColor = WorkbenchPalette.TextPrimary,
            BorderStyle = BorderStyle.None,
            ShowLines = false,
            ShowPlusMinus = true,
            ShowRootLines = true,
            HideSelection = true,
            FullRowSelect = true,
            ItemHeight = 20,
            Font = CardTheme.Font(8.5f),
            Margin = Padding.Empty,
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        layout.Controls.Add(header, 0, 0);
        layout.Controls.Add(_empty, 0, 1);
        layout.Controls.Add(_treeView, 0, 2);

        Controls.Add(layout);

        _tick = new System.Windows.Forms.Timer { Interval = 1000 };
        _tick.Tick += (_, _) => OnTick();
        _tick.Start();

        RefreshCounts();
        RefreshTree();
    }

    /// <summary>Binds the panel to a scope, or unbinds it when the session has no sub-agent support.</summary>
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

        Visible = _tree is not null;
        RefreshCounts();
        RefreshTree();
    }

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
        if (_tree is null) return;
        _tree.TickElapsed();
        QueueRefresh();
    }

    private void QueueRefresh()
    {
        if (_refreshQueued || IsDisposed || !IsHandleCreated) return;
        _refreshQueued = true;

        BeginInvoke(RefreshFromTree);
    }

    private void RefreshFromTree()
    {
        _refreshQueued = false;
        RefreshCounts();
        RefreshTree();
    }

    private void RefreshCounts()
    {
        var tree = _tree;
        _running.Visible = tree is { IsIdle: false };
        _running.Text = $"运行中 {tree?.RunningCount ?? 0}";
        _total.Text = $"{tree?.TotalCount ?? 0} 个";
        _tokens.Visible = tree?.ScopeRoot.HasTokens == true;
        _tokens.Text = $"· {tree?.SubtreeTokensText ?? string.Empty} tokens";
        _failed.Visible = tree?.HasFailed == true;
        _failed.Text = $"· 失败 {tree?.FailedCount ?? 0}";
        _empty.Visible = tree is null || tree.IsEmpty;
        _treeView.Visible = tree is not null && !tree.IsEmpty;
    }

    private void RefreshTree()
    {
        _treeView.BeginUpdate();
        _treeView.Nodes.Clear();

        if (_tree is not null && !_tree.IsEmpty)
        {
            foreach (var child in _tree.ScopeRoot.Children)
                _treeView.Nodes.Add(BuildNode(child));
        }

        _treeView.ExpandAll();
        _treeView.EndUpdate();
    }

    private static TreeNode BuildNode(SubAgentTreeNodeViewModel node)
    {
        var treeNode = new TreeNode(node.Title)
        {
            ForeColor = NodeColor(node),
            NodeFont = CardTheme.Font(8.5f, node.IsScopeRoot ? FontStyle.Bold : FontStyle.Regular),
            ToolTipText = node.TokensText,
        };

        if (!node.IsScopeRoot)
        {
            var details = new System.Collections.Generic.List<string>();
            if (node.HasDuration) details.Add(node.DurationText);
            if (node.HasTokens) details.Add(node.TokensText);
            if (node.ShowStateText) details.Add(node.StateText);
            if (node.HasDroppedRequests) details.Add($"有 {node.DroppedRequestCount} 项请求被拒");
            if (details.Count > 0) treeNode.Text = $"{node.Title}   {string.Join(" · ", details)}";
        }

        foreach (var child in node.Children)
            treeNode.Nodes.Add(BuildNode(child));

        return treeNode;
    }

    private static Color NodeColor(SubAgentTreeNodeViewModel node) => node switch
    {
        { IsScopeRoot: true } => WorkbenchPalette.AccentText,
        { IsRunning: true } => WorkbenchPalette.Success,
        _ when node.Row?.State == SubAgentState.Failed => WorkbenchPalette.Danger,
        _ when node.Row?.State == SubAgentState.Cancelled => WorkbenchPalette.TextTertiary,
        _ => WorkbenchPalette.TextSecondary,
    };

    private static Label CreateCount(string text, Color color) => new()
    {
        AutoSize = true,
        Text = text,
        ForeColor = color,
        BackColor = Color.Transparent,
        Font = CardTheme.Font(8f),
        Margin = new Padding(6, 0, 0, 0),
    };
}
