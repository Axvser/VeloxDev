using System.Collections.Specialized;
using System.ComponentModel;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using VeloxDev.WorkflowSystem;

namespace Demo.Components.Workflow;

/// <summary>
/// Realtime floating-text info layer for the node-editor surface: canvas actual size, the visible
/// viewport (canvas + world), zoom/origin and the visible node/link elements materialized by the Core
/// virtualization. Reads the <see cref="SurfaceViewport"/> context pushed by the surface behavior plus
/// the Core model; subscribes to the model so scale / visible counts refresh without a new viewport
/// packet. The copy button writes the current multi-line info to the clipboard (JS helper).
/// </summary>
public partial class InfoOverlay : ComponentBase, IDisposable
{
    /// <summary>Gets or sets the workflow tree rendered by the surface.</summary>
    [Parameter]
    public IWorkflowTreeViewModel? Tree { get; set; }

    /// <summary>Gets or sets the surface viewport context pushed by <see cref="WorkflowSurfaceBehavior"/>.</summary>
    [Parameter]
    public SurfaceViewport? Viewport { get; set; }

    [Inject]
    public IJSRuntime JS { get; set; } = default!;

    private string[] _lines = [];
    private string _copyText = "";
    private string _copyLabel = "复制";

    private INotifyPropertyChanged? _layoutNotify;
    private IWorkflowTreeViewModel? _subscribedTree;

    private string CopyLabel => _copyLabel;
    private string[] Lines
    {
        get
        {
            Recompute();
            return _lines;
        }
    }

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        base.OnParametersSet();
        if (!ReferenceEquals(_subscribedTree, Tree))
        {
            UnsubscribeTree();
            _subscribedTree = Tree;
            SubscribeTree();
        }
    }

    private void Recompute()
    {
        if (Tree is null)
        {
            _lines = ["VeloxDev Workflow — 未绑定画布"];
            _copyText = string.Join(Environment.NewLine, _lines);
            return;
        }

        var layout = Tree.Layout;
        var actual = layout.ActualSize;
        double ox, oy, sx, sy, vw, vh;
        if (Viewport is { } vp)
        {
            ox = vp.ContentOffsetX;
            oy = vp.ContentOffsetY;
            sx = vp.ScrollLeft;
            sy = vp.ScrollTop;
            vw = vp.ViewportWidth;
            vh = vp.ViewportHeight;
        }
        else
        {
            var v = Tree.GetHelper().Viewport;
            ox = layout.ActualOffset.Horizontal;
            oy = layout.ActualOffset.Vertical;
            vw = v.Width;
            vh = v.Height;
            sx = v.Horizontal + ox;
            sy = v.Vertical + oy;
        }

        double wx = sx - ox, wy = sy - oy;
        double scale = layout.Scale.Horizontal;
        double zoomPercent = scale > 0 ? 100.0 / scale : 100.0;

        int totalNodes = Tree.Nodes.Count;
        int totalLinks = Tree.Links.Count;
        int visibleNodes = 0;
        int visibleLinks = 0;
        var virtualLink = Tree.VirtualLink;
        foreach (var item in Tree.GetHelper().VisibleItems)
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

        _lines =
        [
            "画布 " + Fmt(actual.Width) + " × " + Fmt(actual.Height),
            "视口(画布) " + Fmt(sx) + ", " + Fmt(sy) + "  " + Fmt(vw) + "×" + Fmt(vh),
            "视口(世界) " + Fmt(wx) + ", " + Fmt(wy) + "  " + Fmt(vw) + "×" + Fmt(vh),
            "缩放 " + Math.Round(zoomPercent).ToString() + "%  ·  Scale " + scale.ToString("0.00"),
            "原点 " + Fmt(ox) + ", " + Fmt(oy),
            "元素 节点 " + visibleNodes + "/" + totalNodes + " · 连线 " + visibleLinks + "/" + totalLinks,
        ];
        _copyText = string.Join(Environment.NewLine, _lines);
    }

    private void SubscribeTree()
    {
        if (Tree is null)
        {
            return;
        }

        if (Tree.Layout is INotifyPropertyChanged layout)
        {
            _layoutNotify = layout;
            layout.PropertyChanged += OnModelChanged;
        }

        Tree.Nodes.CollectionChanged += OnCollectionChanged;
        Tree.Links.CollectionChanged += OnCollectionChanged;
        Tree.GetHelper().VisibleItems.CollectionChanged += OnCollectionChanged;
    }

    private void UnsubscribeTree()
    {
        if (_layoutNotify is not null)
        {
            _layoutNotify.PropertyChanged -= OnModelChanged;
            _layoutNotify = null;
        }

        if (_subscribedTree is not null)
        {
            _subscribedTree.Nodes.CollectionChanged -= OnCollectionChanged;
            _subscribedTree.Links.CollectionChanged -= OnCollectionChanged;
            _subscribedTree.GetHelper().VisibleItems.CollectionChanged -= OnCollectionChanged;
        }

        _subscribedTree = null;
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e) => InvokeAsync(StateHasChanged);
    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => InvokeAsync(StateHasChanged);

    private async Task CopyInfo()
    {
        if (JS is null)
        {
            return;
        }

        try
        {
            var ok = await JS.InvokeAsync<bool>("copyWorkflowInfo", _copyText);
            _copyLabel = ok ? "已复制" : "复制失败";
        }
        catch
        {
            _copyLabel = "复制失败";
        }

        StateHasChanged();
        await Task.Delay(1200);
        _copyLabel = "复制";
        StateHasChanged();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        UnsubscribeTree();
    }

    private static string Fmt(double value)
    {
        double abs = Math.Abs(value);
        if (abs < 10000) return Math.Round(value).ToString();
        if (abs < 1000000) return Math.Round(value / 1000.0, 1).ToString() + "K";
        return Math.Round(value / 1000000.0, 1).ToString() + "M";
    }
}
