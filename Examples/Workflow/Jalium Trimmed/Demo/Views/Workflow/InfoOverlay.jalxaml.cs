using System;
using System.ComponentModel;
using Jalium.UI;
using Jalium.UI.Controls;
using VeloxDev.WorkflowSystem;

namespace Demo.Views.Workflow;

/// <summary>
/// Realtime floating-text info layer for the node-editor surface (a "decorator layer" like the minimap
/// overlay, but a passive HUD): a translucent rounded panel showing canvas actual size, the current visible
/// viewport (canvas + world), zoom/origin and the visible node/link elements materialized by the Core
/// virtualization (<see cref="IWorkflowTreeViewModelHelper.VisibleItems"/>).
/// </summary>
/// <remarks>
/// <para>
/// The panel itself is markup; this file is the data side only. It repaints from the Core model
/// (Layout / helper VisibleItems / Nodes / Links) plus the scroll/viewport numbers fed in as
/// <see cref="ScrollOffsetXProperty"/> and friends — the same feed the minimap overlay consumes.
/// </para>
/// <para>
/// The small 复制 button copies the current multi-line info to the clipboard for debugging.
/// </para>
/// </remarks>
public sealed partial class InfoOverlay : Border
{
    /// <summary>The tree this HUD reports on.</summary>
    public static readonly DependencyProperty WorkflowTreeProperty = DependencyProperty.Register(
        "WorkflowTree", typeof(IWorkflowTreeViewModel), typeof(InfoOverlay), new PropertyMetadata(null, OnTreeChanged));

    /// <summary>The surface scroll viewer's horizontal offset.</summary>
    public static readonly DependencyProperty ScrollOffsetXProperty = DependencyProperty.Register(
        "ScrollOffsetX", typeof(double), typeof(InfoOverlay), new PropertyMetadata(0.0, OnVisualChanged));

    /// <summary>The surface scroll viewer's vertical offset.</summary>
    public static readonly DependencyProperty ScrollOffsetYProperty = DependencyProperty.Register(
        "ScrollOffsetY", typeof(double), typeof(InfoOverlay), new PropertyMetadata(0.0, OnVisualChanged));

    /// <summary>The world origin's horizontal position, excluding any ruler reserve.</summary>
    public static readonly DependencyProperty ContentOffsetXProperty = DependencyProperty.Register(
        "ContentOffsetX", typeof(double), typeof(InfoOverlay), new PropertyMetadata(0.0, OnVisualChanged));

    /// <summary>The world origin's vertical position, excluding any ruler reserve.</summary>
    public static readonly DependencyProperty ContentOffsetYProperty = DependencyProperty.Register(
        "ContentOffsetY", typeof(double), typeof(InfoOverlay), new PropertyMetadata(0.0, OnVisualChanged));

    /// <summary>The measured viewport width.</summary>
    public static readonly DependencyProperty ViewportWidthProperty = DependencyProperty.Register(
        "ViewportWidth", typeof(double), typeof(InfoOverlay), new PropertyMetadata(0.0, OnVisualChanged));

    /// <summary>The measured viewport height.</summary>
    public static readonly DependencyProperty ViewportHeightProperty = DependencyProperty.Register(
        "ViewportHeight", typeof(double), typeof(InfoOverlay), new PropertyMetadata(0.0, OnVisualChanged));

    /// <summary>The tree this HUD reports on.</summary>
    public IWorkflowTreeViewModel? WorkflowTree { get => (IWorkflowTreeViewModel?)GetValue(WorkflowTreeProperty); set => SetValue(WorkflowTreeProperty, value); }

    /// <summary>The surface scroll viewer's horizontal offset.</summary>
    public double ScrollOffsetX { get => (double)(GetValue(ScrollOffsetXProperty) ?? 0.0); set => SetValue(ScrollOffsetXProperty, value); }

    /// <summary>The surface scroll viewer's vertical offset.</summary>
    public double ScrollOffsetY { get => (double)(GetValue(ScrollOffsetYProperty) ?? 0.0); set => SetValue(ScrollOffsetYProperty, value); }

    /// <summary>The world origin's horizontal position, excluding any ruler reserve.</summary>
    public double ContentOffsetX { get => (double)(GetValue(ContentOffsetXProperty) ?? 0.0); set => SetValue(ContentOffsetXProperty, value); }

    /// <summary>The world origin's vertical position, excluding any ruler reserve.</summary>
    public double ContentOffsetY { get => (double)(GetValue(ContentOffsetYProperty) ?? 0.0); set => SetValue(ContentOffsetYProperty, value); }

    /// <summary>The measured viewport width.</summary>
    public double ViewportWidth { get => (double)(GetValue(ViewportWidthProperty) ?? 0.0); set => SetValue(ViewportWidthProperty, value); }

    /// <summary>The measured viewport height.</summary>
    public double ViewportHeight { get => (double)(GetValue(ViewportHeightProperty) ?? 0.0); set => SetValue(ViewportHeightProperty, value); }

    private IWorkflowTreeViewModel? _tree;
    private string _copyText = "";

    public InfoOverlay()
    {
        InitializeComponent();

        // 唯一的接线：按钮是互动的，其余都是数据。放在构造里，与「代码后置只剩初始构造」不冲突。
        PART_Copy.Click += (_, _) =>
        {
            Clipboard.SetText(_copyText);
            PART_Copy.Content = "已复制";
        };
    }

    private TextBlock[] Lines => [PART_Line1, PART_Line2, PART_Line3, PART_Line4, PART_Line5, PART_Line6];

    private static void OnVisualChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is InfoOverlay overlay)
        {
            overlay.Refresh();
        }
    }

    private static void OnTreeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not InfoOverlay overlay)
        {
            return;
        }

        overlay.UnsubscribeTree();
        overlay._tree = (IWorkflowTreeViewModel?)e.NewValue;
        overlay.SubscribeTree();
        overlay.Refresh();
    }

    private void SubscribeTree()
    {
        if (_tree is null)
        {
            return;
        }

        if (_tree.Layout is INotifyPropertyChanged layout)
        {
            layout.PropertyChanged += OnModelChanged;
        }

        _tree.Nodes.CollectionChanged += OnModelChanged;
        _tree.Links.CollectionChanged += OnModelChanged;
        _tree.GetHelper().VisibleItems.CollectionChanged += OnModelChanged;
    }

    private void UnsubscribeTree()
    {
        if (_tree is null)
        {
            return;
        }

        if (_tree.Layout is INotifyPropertyChanged layout)
        {
            layout.PropertyChanged -= OnModelChanged;
        }

        _tree.Nodes.CollectionChanged -= OnModelChanged;
        _tree.Links.CollectionChanged -= OnModelChanged;
        _tree.GetHelper().VisibleItems.CollectionChanged -= OnModelChanged;
    }

    private void OnModelChanged(object? sender, EventArgs e) => Refresh();

    /// <summary>Recomputes the display lines + copy text from the current model/scroll state.</summary>
    public void Refresh()
    {
        string[] lines = BuildLines();
        var targets = Lines;

        for (int i = 0; i < targets.Length; i++)
        {
            targets[i].Text = i < lines.Length ? lines[i] : string.Empty;
        }

        _copyText = string.Join(Environment.NewLine, lines);
        PART_Copy.Content = "复制";
    }

    private string[] BuildLines()
    {
        if (_tree is null)
        {
            return ["VeloxDev Workflow — 未绑定画布"];
        }

        var layout = _tree.Layout;
        var actual = layout.ActualSize;
        double ox = ContentOffsetX, oy = ContentOffsetY;
        double sx = ScrollOffsetX, sy = ScrollOffsetY;
        double vw = ViewportWidth, vh = ViewportHeight;
        double wx = sx - ox, wy = sy - oy; // world = canvas − origin

        double scale = layout.Scale.Horizontal;
        double zoomPercent = scale > 0 ? 100.0 / scale : 100.0;

        int totalNodes = _tree.Nodes.Count;
        int totalLinks = _tree.Links.Count;
        int visibleNodes = 0;
        int visibleLinks = 0;
        var virtualLink = _tree.VirtualLink;
        foreach (var item in _tree.GetHelper().VisibleItems)
        {
            if (item is IWorkflowNodeViewModel)
            {
                visibleNodes++;
            }
            else if (item is IWorkflowLinkViewModel link && !ReferenceEquals(link, virtualLink))
            {
                visibleLinks++;
            }
        }

        return
        [
            "画布 " + Fmt(actual.Width) + " × " + Fmt(actual.Height),
            "视口(画布) " + Fmt(sx) + ", " + Fmt(sy) + "  " + Fmt(vw) + "×" + Fmt(vh),
            "视口(世界) " + Fmt(wx) + ", " + Fmt(wy) + "  " + Fmt(vw) + "×" + Fmt(vh),
            "缩放 " + Math.Round(zoomPercent).ToString() + "%  ·  Scale " + scale.ToString("0.00"),
            "原点 " + Fmt(ox) + ", " + Fmt(oy),
            "元素 节点 " + visibleNodes + "/" + totalNodes + " · 连线 " + visibleLinks + "/" + totalLinks,
        ];
    }

    private static string Fmt(double value)
    {
        double abs = Math.Abs(value);
        if (abs < 10000) return Math.Round(value).ToString();
        if (abs < 1000000) return Math.Round(value / 1000.0, 1).ToString() + "K";
        return Math.Round(value / 1000000.0, 1).ToString() + "M";
    }
}
