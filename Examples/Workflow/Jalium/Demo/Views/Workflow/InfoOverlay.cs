using System.ComponentModel;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Media;
using VeloxDev.WorkflowSystem;

namespace Demo.Views.Workflow;

/// <summary>
/// Realtime floating-text info layer for the node-editor surface (a "decorator layer" like the
/// minimap overlay, but a passive HUD): a translucent rounded panel, anchored bottom-left by the
/// composing window, showing canvas actual size, the current visible viewport (canvas + world),
/// zoom/origin and the visible node/link elements materialized by the Core virtualization
/// (<see cref="IWorkflowTreeViewModelHelper.VisibleItems"/>). It repaints from the Core model
/// (Layout / helper VisibleItems / Nodes / Links) plus the scroll/viewport numbers the window pushes
/// on every scroll or surface change (same feed as the minimap). The small 复制 button in the
/// bottom-right corner copies the current multi-line info to the clipboard for debugging.
/// </summary>
public sealed class InfoOverlay : Border
{
    private static readonly SolidColorBrush s_bg = new(Color.FromArgb(0xE6, 0x12, 0x15, 0x1B));
    private static readonly SolidColorBrush s_border = new(Color.FromArgb(0xCC, 0x8E, 0xA3, 0xB8));
    private static readonly SolidColorBrush s_text = new(Color.FromRgb(0xDD, 0xE4, 0xEA));
    private static readonly SolidColorBrush s_btnBg = new(Color.FromRgb(0x1E, 0x3A, 0x5F));
    private static readonly SolidColorBrush s_btnFg = new(Color.FromRgb(0x7E, 0xC8, 0xFF));

    public static readonly DependencyProperty WorkflowTreeProperty = DependencyProperty.Register(
        "WorkflowTree", typeof(IWorkflowTreeViewModel), typeof(InfoOverlay), new PropertyMetadata(null, OnTreeChanged));

    // The same scroll / content-offset / viewport feeds the minimap overlay consumes; the window pushes
    // them on every scroll or surface change so the numbers never go stale while panning or zooming.
    public static readonly DependencyProperty ScrollOffsetXProperty = DependencyProperty.Register(
        "ScrollOffsetX", typeof(double), typeof(InfoOverlay), new PropertyMetadata(0.0, OnVisualChanged));
    public static readonly DependencyProperty ScrollOffsetYProperty = DependencyProperty.Register(
        "ScrollOffsetY", typeof(double), typeof(InfoOverlay), new PropertyMetadata(0.0, OnVisualChanged));
    public static readonly DependencyProperty ContentOffsetXProperty = DependencyProperty.Register(
        "ContentOffsetX", typeof(double), typeof(InfoOverlay), new PropertyMetadata(0.0, OnVisualChanged));
    public static readonly DependencyProperty ContentOffsetYProperty = DependencyProperty.Register(
        "ContentOffsetY", typeof(double), typeof(InfoOverlay), new PropertyMetadata(0.0, OnVisualChanged));
    public static readonly DependencyProperty ViewportWidthProperty = DependencyProperty.Register(
        "ViewportWidth", typeof(double), typeof(InfoOverlay), new PropertyMetadata(0.0, OnVisualChanged));
    public static readonly DependencyProperty ViewportHeightProperty = DependencyProperty.Register(
        "ViewportHeight", typeof(double), typeof(InfoOverlay), new PropertyMetadata(0.0, OnVisualChanged));

    public IWorkflowTreeViewModel? WorkflowTree { get => (IWorkflowTreeViewModel?)GetValue(WorkflowTreeProperty); set => SetValue(WorkflowTreeProperty, value); }

    public double ScrollOffsetX { get => (double)(GetValue(ScrollOffsetXProperty) ?? 0.0); set => SetValue(ScrollOffsetXProperty, value); }
    public double ScrollOffsetY { get => (double)(GetValue(ScrollOffsetYProperty) ?? 0.0); set => SetValue(ScrollOffsetYProperty, value); }
    public double ContentOffsetX { get => (double)(GetValue(ContentOffsetXProperty) ?? 0.0); set => SetValue(ContentOffsetXProperty, value); }
    public double ContentOffsetY { get => (double)(GetValue(ContentOffsetYProperty) ?? 0.0); set => SetValue(ContentOffsetYProperty, value); }
    public double ViewportWidth { get => (double)(GetValue(ViewportWidthProperty) ?? 0.0); set => SetValue(ViewportWidthProperty, value); }
    public double ViewportHeight { get => (double)(GetValue(ViewportHeightProperty) ?? 0.0); set => SetValue(ViewportHeightProperty, value); }

    private IWorkflowTreeViewModel? _tree;
    private readonly TextBlock[] _lineText = new TextBlock[LineCount];
    private readonly Button _copyButton;
    private string _copyText = "";

    private const int LineCount = 6;

    public InfoOverlay()
    {
        CornerRadius = new CornerRadius(6);
        BorderBrush = s_border;
        BorderThickness = new Thickness(1);
        Background = s_bg;

        var grid = new Grid();
        var lines = new StackPanel { Spacing = 2, Margin = new Thickness(12, 10, 56, 10) };
        for (int i = 0; i < LineCount; i++)
        {
            _lineText[i] = new TextBlock
            {
                Foreground = s_text,
                FontSize = 12,
                IsHitTestVisible = false, // text area stays click-through; only the copy button is interactive
            };
            lines.Children.Add(_lineText[i]);
        }

        _copyButton = new Button
        {
            Content = "复制",
            FontSize = 11,
            Background = s_btnBg,
            Foreground = s_btnFg,
            BorderBrush = s_btnFg,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 2, 8, 2),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 8, 8),
        };
        _copyButton.Click += (_, _) =>
        {
            Clipboard.SetText(_copyText);
            _copyButton.Content = "已复制";
        };

        grid.Children.Add(lines);
        grid.Children.Add(_copyButton);
        Child = grid;
    }

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

    // ── Model subscriptions ──────────────────────────────────────────────────

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

    // ── Content ──────────────────────────────────────────────────────────────

    /// <summary>Recomputes the display lines + copy text from the current model/scroll state.</summary>
    public void Refresh()
    {
        string[] lines = BuildLines();
        for (int i = 0; i < lines.Length && i < _lineText.Length; i++)
        {
            _lineText[i].Text = lines[i];
        }

        for (int i = lines.Length; i < _lineText.Length; i++)
        {
            _lineText[i].Text = string.Empty;
        }

        _copyText = string.Join(Environment.NewLine, lines);
        _copyButton.Content = "复制";
    }

    private string[] BuildLines()
    {
        if (_tree is null)
        {
            return new[] { "VeloxDev Workflow — 未绑定画布" };
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

        return new[]
        {
            "画布 " + Fmt(actual.Width) + " × " + Fmt(actual.Height),
            "视口(画布) " + Fmt(sx) + ", " + Fmt(sy) + "  " + Fmt(vw) + "×" + Fmt(vh),
            "视口(世界) " + Fmt(wx) + ", " + Fmt(wy) + "  " + Fmt(vw) + "×" + Fmt(vh),
            "缩放 " + Math.Round(zoomPercent).ToString() + "%  ·  Scale " + scale.ToString("0.00"),
            "原点 " + Fmt(ox) + ", " + Fmt(oy),
            "元素 节点 " + visibleNodes + "/" + totalNodes + " · 连线 " + visibleLinks + "/" + totalLinks,
        };
    }

    private static string Fmt(double value)
    {
        double abs = Math.Abs(value);
        if (abs < 10000) return Math.Round(value).ToString();
        if (abs < 1000000) return Math.Round(value / 1000.0, 1).ToString() + "K";
        return Math.Round(value / 1000000.0, 1).ToString() + "M";
    }
}
