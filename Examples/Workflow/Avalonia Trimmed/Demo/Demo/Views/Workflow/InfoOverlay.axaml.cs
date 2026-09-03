using System;
using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using VeloxDev.WorkflowSystem;

namespace Demo;

/// <summary>
/// Realtime floating-text info layer for the node-editor surface: canvas actual size, the visible
/// viewport (canvas + world), zoom/origin and the visible node/link elements materialized by the Core
/// virtualization. Model-driven: it reads <see cref="IWorkflowTreeViewModelHelper.Viewport"/> (kept
/// current by the surface behavior) plus <see cref="CanvasLayout"/> and the helper VisibleItems, and
/// refreshes from the model events + the TreeView host's scroll changes. The 复制 button copies the
/// current multi-line info to the clipboard.
/// </summary>
public partial class InfoOverlay : UserControl
{
    private IWorkflowTreeViewModel? _tree;
    private string _copyText = "";

    public InfoOverlay()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            UnsubscribeTree();
            _tree = DataContext as IWorkflowTreeViewModel;
            SubscribeTree();
            Refresh();
        };
    }

    /// <summary>Recomputes display lines + copy text from the current model/scroll state.</summary>
    public void Refresh()
    {
        string[] lines = BuildLines();
        InfoText.Text = string.Join("\n", lines);
        _copyText = string.Join(Environment.NewLine, lines);
        CopyButton.Content = "复制";
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

    private string[] BuildLines()
    {
        if (_tree is null)
        {
            return new[] { "VeloxDev Workflow — 未绑定画布" };
        }

        var layout = _tree.Layout;
        var actual = layout.ActualSize;
        var vp = _tree.GetHelper().Viewport;
        double ox = layout.ActualOffset.Horizontal, oy = layout.ActualOffset.Vertical;
        double sx = vp.Horizontal + ox, sy = vp.Vertical + oy;
        double vw = vp.Width, vh = vp.Height;

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
            "视口(世界) " + Fmt(vp.Horizontal) + ", " + Fmt(vp.Vertical) + "  " + Fmt(vw) + "×" + Fmt(vh),
            "缩放 " + Math.Round(zoomPercent).ToString() + "%  ·  Scale " + scale.ToString("0.00"),
            "原点 " + Fmt(ox) + ", " + Fmt(oy),
            "元素 节点 " + visibleNodes + "/" + totalNodes + " · 连线 " + visibleLinks + "/" + totalLinks,
        };
    }

    private async void OnCopyClicked(object? sender, RoutedEventArgs e)
    {
        try
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard is not null)
            {
                await clipboard.SetTextAsync(_copyText);
                CopyButton.Content = "已复制";
            }
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
