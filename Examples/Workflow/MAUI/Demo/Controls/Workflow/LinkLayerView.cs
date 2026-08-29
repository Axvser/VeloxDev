using System.Collections.Specialized;
using System.ComponentModel;
using Microsoft.Maui.Graphics;
using VeloxDev.WorkflowSystem;

namespace Demo.Controls;

/// <summary>
/// Single-GraphicsView link layer for the workflow canvas. Renders every link —
/// the real ones from <see cref="IWorkflowTreeViewModel.Links"/> plus the in-progress
/// virtual connection — in ONE Win2D draw pass instead of one GraphicsView per link,
/// so a canvas pan or node drag redraws a single surface rather than N.
/// It lives inside the world canvas (PART_Canvas), fills it, and draws in world
/// coordinates, so it translates with the canvas automatically (no per-link
/// ContentOffset bookkeeping needed).
/// </summary>
public sealed class LinkLayerView : GraphicsView
{
    private const float StrokeWidth = 4f;

    private static readonly Color LinkColor = Color.FromArgb("#22D3EE");
    private static readonly Color VirtualColor = Color.FromArgb("#E2E8F0");

    private IWorkflowTreeViewModel? _tree;
    private IWorkflowLinkViewModel? _virtualLink;
    private readonly HashSet<IWorkflowNodeViewModel> _subscribedNodes = [];
    private readonly HashSet<IWorkflowLinkViewModel> _subscribedLinks = [];

    public LinkLayerView()
    {
        InputTransparent = true;
        ZIndex = -1;
        Drawable = new LinkLayerDrawable(this);
        BindingContextChanged += OnBindingContextChanged;
    }

    private void OnBindingContextChanged(object? sender, EventArgs e)
    {
        var tree = BindingContext as IWorkflowTreeViewModel;
        if (ReferenceEquals(_tree, tree))
        {
            return;
        }

        Unsubscribe();
        _tree = tree;
        if (tree is null)
        {
            MarkDirty();
            return;
        }

        Subscribe(tree);
        MarkDirty();
    }

    // ── Model subscriptions (mirrors WorkflowMinimapOverlay's pattern) ────────

    private void Subscribe(IWorkflowTreeViewModel tree)
    {
        if (tree is INotifyPropertyChanged npc)
        {
            npc.PropertyChanged += OnTreePropertyChanged;
        }

        if (tree.Nodes is INotifyCollectionChanged nc)
        {
            nc.CollectionChanged += OnNodesChanged;
            foreach (var n in tree.Nodes)
            {
                SubscribeNode(n);
            }
        }

        if (tree.Links is INotifyCollectionChanged lc)
        {
            lc.CollectionChanged += OnLinksChanged;
            foreach (var l in tree.Links)
            {
                SubscribeLink(l);
            }
        }

        SubscribeVirtualLink(tree.VirtualLink);
    }

    private void Unsubscribe()
    {
        if (_tree is null)
        {
            return;
        }

        if (_tree is INotifyPropertyChanged npc)
        {
            npc.PropertyChanged -= OnTreePropertyChanged;
        }

        if (_tree.Nodes is INotifyCollectionChanged nc)
        {
            nc.CollectionChanged -= OnNodesChanged;
        }

        if (_tree.Links is INotifyCollectionChanged lc)
        {
            lc.CollectionChanged -= OnLinksChanged;
        }

        foreach (var n in _subscribedNodes)
        {
            if (n is INotifyPropertyChanged np)
            {
                np.PropertyChanged -= OnNodePropertyChanged;
            }
        }

        _subscribedNodes.Clear();

        foreach (var l in _subscribedLinks)
        {
            UnsubscribeLink(l);
        }

        _subscribedLinks.Clear();
        SubscribeVirtualLink(null);
        _tree = null;
    }

    private void SubscribeNode(IWorkflowNodeViewModel node)
    {
        if (_subscribedNodes.Add(node) && node is INotifyPropertyChanged npc)
        {
            npc.PropertyChanged += OnNodePropertyChanged;
        }
    }

    private void SubscribeLink(IWorkflowLinkViewModel link)
    {
        if (!_subscribedLinks.Add(link))
        {
            return;
        }

        if (link is INotifyPropertyChanged npc)
        {
            npc.PropertyChanged += OnLinkPropertyChanged;
        }

        if (link.Sender is INotifyPropertyChanged sp)
        {
            sp.PropertyChanged += OnSlotPropertyChanged;
        }

        if (link.Receiver is INotifyPropertyChanged rp)
        {
            rp.PropertyChanged += OnSlotPropertyChanged;
        }
    }

    private void UnsubscribeLink(IWorkflowLinkViewModel link)
    {
        if (link is INotifyPropertyChanged npc)
        {
            npc.PropertyChanged -= OnLinkPropertyChanged;
        }

        if (link.Sender is INotifyPropertyChanged sp)
        {
            sp.PropertyChanged -= OnSlotPropertyChanged;
        }

        if (link.Receiver is INotifyPropertyChanged rp)
        {
            rp.PropertyChanged -= OnSlotPropertyChanged;
        }

        _subscribedLinks.Remove(link);
    }

    private void SubscribeVirtualLink(IWorkflowLinkViewModel? link)
    {
        if (ReferenceEquals(_virtualLink, link))
        {
            return;
        }

        if (_virtualLink is not null)
        {
            UnsubscribeLink(_virtualLink);
        }

        _virtualLink = link;
        if (link is not null)
        {
            SubscribeLink(link);
        }
    }

    private void OnTreePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IWorkflowTreeViewModel.VirtualLink) && _tree is not null)
        {
            SubscribeVirtualLink(_tree.VirtualLink);
            MarkDirty();
        }
    }

    private void OnNodesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
        {
            foreach (var i in e.NewItems)
            {
                if (i is IWorkflowNodeViewModel n)
                {
                    SubscribeNode(n);
                }
            }
        }

        if (e.OldItems is not null)
        {
            foreach (var i in e.OldItems)
            {
                if (i is IWorkflowNodeViewModel n && _subscribedNodes.Remove(n) && n is INotifyPropertyChanged npc)
                {
                    npc.PropertyChanged -= OnNodePropertyChanged;
                }
            }
        }

        MarkDirty();
    }

    private void OnLinksChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
        {
            foreach (var i in e.NewItems)
            {
                if (i is IWorkflowLinkViewModel l)
                {
                    SubscribeLink(l);
                }
            }
        }

        if (e.OldItems is not null)
        {
            foreach (var i in e.OldItems)
            {
                if (i is IWorkflowLinkViewModel l)
                {
                    UnsubscribeLink(l);
                }
            }
        }

        MarkDirty();
    }

    private void OnNodePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IWorkflowNodeViewModel.Anchor) or nameof(IWorkflowNodeViewModel.Size))
        {
            MarkDirty();
        }
    }

    private void OnLinkPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IWorkflowLinkViewModel.IsVisible))
        {
            MarkDirty();
            return;
        }

        if (e.PropertyName is nameof(IWorkflowLinkViewModel.Sender) or nameof(IWorkflowLinkViewModel.Receiver)
            && sender is IWorkflowLinkViewModel link && _subscribedLinks.Contains(link))
        {
            // Endpoints rewired — re-subscribe to the new slots' anchors.
            if (link.Sender is INotifyPropertyChanged sp)
            {
                sp.PropertyChanged -= OnSlotPropertyChanged;
                sp.PropertyChanged += OnSlotPropertyChanged;
            }

            if (link.Receiver is INotifyPropertyChanged rp)
            {
                rp.PropertyChanged -= OnSlotPropertyChanged;
                rp.PropertyChanged += OnSlotPropertyChanged;
            }

            MarkDirty();
        }
    }

    private void OnSlotPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IWorkflowSlotViewModel.Anchor))
        {
            MarkDirty();
        }
    }

    private void MarkDirty()
    {
        // Invalidate requires the main thread — dispatch if called from a background source.
        if (MainThread.IsMainThread)
        {
            Invalidate();
        }
        else
        {
            MainThread.BeginInvokeOnMainThread(Invalidate);
        }
    }

    // ── Drawing ──────────────────────────────────────────────────────────────

    private static IEnumerable<IWorkflowLinkViewModel> EnumerateVisibleLinks(IWorkflowTreeViewModel tree)
    {
        foreach (var link in tree.Links)
        {
            if (link.IsVisible)
            {
                yield return link;
            }
        }

        if (tree.VirtualLink is { IsVisible: true } virtualLink)
        {
            yield return virtualLink;
        }
    }

    private static bool TryGetEndpoints(IWorkflowLinkViewModel link, out float startX, out float startY, out float endX, out float endY)
    {
        startX = (float)link.Sender.Anchor.Horizontal;
        startY = (float)link.Sender.Anchor.Vertical;
        endX = (float)link.Receiver.Anchor.Horizontal;
        endY = (float)link.Receiver.Anchor.Vertical;
        // A slot that has not been laid out yet has a NaN anchor — skip it rather
        // than feed NaN through Win2D.
        return !float.IsNaN(startX) && !float.IsNaN(startY) && !float.IsNaN(endX) && !float.IsNaN(endY);
    }

    private static bool IsVirtualLink(IWorkflowLinkViewModel link)
        => link.Sender.Parent is null || link.Receiver.Parent is null;

    private sealed class LinkLayerDrawable(LinkLayerView owner) : IDrawable
    {
        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            var tree = owner._tree;
            if (tree is null)
            {
                return;
            }

            // First pass: world-space bounding min so curves in the negative region
            // (overscroll before the world origin) stay inside the GraphicsView clip.
            var first = true;
            var minX = 0f;
            var minY = 0f;
            foreach (var link in EnumerateVisibleLinks(tree))
            {
                if (!TryGetEndpoints(link, out var sx, out var sy, out var ex, out var ey))
                {
                    continue;
                }

                var mx = Math.Min(sx, ex);
                var my = Math.Min(sy, ey);
                if (first)
                {
                    minX = mx;
                    minY = my;
                    first = false;
                }
                else
                {
                    minX = Math.Min(minX, mx);
                    minY = Math.Min(minY, my);
                }
            }

            if (first)
            {
                return;
            }

            var shiftX = minX < 0f ? -minX : 0f;
            var shiftY = minY < 0f ? -minY : 0f;
            // Uniform counter-translate: drawing is shifted into the clip region and the
            // view shifts back, so screen positions are unchanged (mirrors the old
            // per-link views' negative-offset correction, applied to the whole layer).
            owner.TranslationX = -shiftX;
            owner.TranslationY = -shiftY;

            foreach (var link in EnumerateVisibleLinks(tree))
            {
                if (!TryGetEndpoints(link, out var sx, out var sy, out var ex, out var ey))
                {
                    continue;
                }

                var isVirtual = IsVirtualLink(link);
                DrawPolyline(canvas, sx + shiftX, sy + shiftY, ex + shiftX, ey + shiftY,
                    isVirtual, isVirtual ? VirtualColor : LinkColor);
            }
        }
    }

    private static void DrawPolyline(ICanvas canvas, float startX, float startY, float endX, float endY, bool isVirtual, Color color)
    {
        const float phi = 0.6180339887f;
        var stub = ((endX - startX) / 2f) * (1f - phi);
        var p1X = startX + stub;
        var p2X = endX - stub;

        canvas.StrokeColor = color;
        canvas.StrokeSize = StrokeWidth;
        canvas.StrokeDashPattern = isVirtual ? [4, 2] : null;
        canvas.DrawLine(startX, startY, p1X, startY);
        canvas.DrawLine(p1X, startY, p2X, endY);
        canvas.DrawLine(p2X, endY, endX, endY);

        if (!isVirtual)
        {
            DrawArrowhead(canvas, p2X, endY, endX, endY, color);
        }

        canvas.StrokeDashPattern = null;
    }

    private static void DrawArrowhead(ICanvas canvas, float fromX, float fromY, float tipX, float tipY, Color color)
    {
        if (canvas is null || color is null)
        {
            return;
        }

        var dx = tipX - fromX;
        var dy = tipY - fromY;
        var length = MathF.Sqrt((dx * dx) + (dy * dy));
        if (length <= float.Epsilon)
        {
            return;
        }

        dx /= length;
        dy /= length;
        const float arrowLength = 12f;
        const float arrowWidth = 8f;
        var normalX = -dy;
        var normalY = dx;
        var baseX = tipX - (dx * arrowLength);
        var baseY = tipY - (dy * arrowLength);
        var leftX = baseX + (normalX * (arrowWidth / 2f));
        var leftY = baseY + (normalY * (arrowWidth / 2f));
        var rightX = baseX - (normalX * (arrowWidth / 2f));
        var rightY = baseY - (normalY * (arrowWidth / 2f));

        var arrow = new PathF();
        arrow.MoveTo(tipX, tipY);
        arrow.LineTo(leftX, leftY);
        arrow.LineTo(rightX, rightY);
        arrow.Close();

        canvas.FillColor = color;
        canvas.FillPath(arrow);
    }
}
