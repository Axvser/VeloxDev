using System;
using System.Collections.Specialized;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using VeloxDev.WorkflowSystem;

namespace Demo.Views.Workflow;

/// <summary>
/// Realtime floating-text info layer for the node-editor surface: canvas actual size, the visible
/// viewport (canvas + world), zoom/origin and the visible node/link elements materialized by the Core
/// virtualization. Repaints from the Core model (Layout / helper VisibleItems / Nodes / Links) plus the
/// ScrollOffset/ContentOffset/Viewport DPs that the host binds (same feed as the minimap). The 复制
/// button copies the current multi-line info to the clipboard.
/// </summary>
public sealed partial class InfoOverlay : UserControl
{
    public static readonly DependencyProperty WorkflowTreeProperty = DependencyProperty.Register(
        nameof(WorkflowTree), typeof(IWorkflowTreeViewModel), typeof(InfoOverlay), new PropertyMetadata(null, OnTreeChanged));

    public static readonly DependencyProperty ScrollOffsetXProperty = DependencyProperty.Register(
        nameof(ScrollOffsetX), typeof(double), typeof(InfoOverlay), new PropertyMetadata(0.0, OnVisualChanged));
    public static readonly DependencyProperty ScrollOffsetYProperty = DependencyProperty.Register(
        nameof(ScrollOffsetY), typeof(double), typeof(InfoOverlay), new PropertyMetadata(0.0, OnVisualChanged));
    public static readonly DependencyProperty ContentOffsetXProperty = DependencyProperty.Register(
        nameof(ContentOffsetX), typeof(double), typeof(InfoOverlay), new PropertyMetadata(0.0, OnVisualChanged));
    public static readonly DependencyProperty ContentOffsetYProperty = DependencyProperty.Register(
        nameof(ContentOffsetY), typeof(double), typeof(InfoOverlay), new PropertyMetadata(0.0, OnVisualChanged));
    public static readonly DependencyProperty ViewportWidthProperty = DependencyProperty.Register(
        nameof(ViewportWidth), typeof(double), typeof(InfoOverlay), new PropertyMetadata(0.0, OnVisualChanged));
    public static readonly DependencyProperty ViewportHeightProperty = DependencyProperty.Register(
        nameof(ViewportHeight), typeof(double), typeof(InfoOverlay), new PropertyMetadata(0.0, OnVisualChanged));

    public IWorkflowTreeViewModel? WorkflowTree { get => (IWorkflowTreeViewModel?)GetValue(WorkflowTreeProperty); set => SetValue(WorkflowTreeProperty, value); }

    public double ScrollOffsetX { get => (double)GetValue(ScrollOffsetXProperty); set => SetValue(ScrollOffsetXProperty, value); }
    public double ScrollOffsetY { get => (double)GetValue(ScrollOffsetYProperty); set => SetValue(ScrollOffsetYProperty, value); }
    public double ContentOffsetX { get => (double)GetValue(ContentOffsetXProperty); set => SetValue(ContentOffsetXProperty, value); }
    public double ContentOffsetY { get => (double)GetValue(ContentOffsetYProperty); set => SetValue(ContentOffsetYProperty, value); }
    public double ViewportWidth { get => (double)GetValue(ViewportWidthProperty); set => SetValue(ViewportWidthProperty, value); }
    public double ViewportHeight { get => (double)GetValue(ViewportHeightProperty); set => SetValue(ViewportHeightProperty, value); }

    private IWorkflowTreeViewModel? _tree;
    private string _copyText = "";

    public InfoOverlay()
    {
        InitializeComponent();
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

        _tree.Nodes.CollectionChanged += OnCollectionChanged;
        _tree.Links.CollectionChanged += OnCollectionChanged;
        _tree.GetHelper().VisibleItems.CollectionChanged += OnCollectionChanged;
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

        _tree.Nodes.CollectionChanged -= OnCollectionChanged;
        _tree.Links.CollectionChanged -= OnCollectionChanged;
        _tree.GetHelper().VisibleItems.CollectionChanged -= OnCollectionChanged;
        _tree = null;
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e) => Refresh();
    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => Refresh();

    /// <summary>Recomputes display lines + copy text from the current model/scroll state.</summary>
    private void Refresh()
    {
        string[] lines = BuildLines();
        InfoText.Text = string.Join("\n", lines);
        _copyText = string.Join(Environment.NewLine, lines);
        CopyButton.Content = "复制";
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
        double wx = sx - ox, wy = sy - oy;

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

    private void OnCopyClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            var package = new DataPackage();
            package.SetText(_copyText);
            Clipboard.SetContent(package);
            Clipboard.Flush();
            CopyButton.Content = "已复制";
        }
        catch
        {
            CopyButton.Content = "复制失败";
        }
    }

    private static string Fmt(double value)
    {
        double abs = Math.Abs(value);
        if (abs < 10000) return Math.Round(value).ToString();
        if (abs < 1000000) return Math.Round(value / 1000.0, 1).ToString() + "K";
        return Math.Round(value / 1000000.0, 1).ToString() + "M";
    }
}
