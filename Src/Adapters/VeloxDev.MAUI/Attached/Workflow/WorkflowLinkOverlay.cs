using System.Collections.Specialized;
using System.ComponentModel;
using Microsoft.Maui.Graphics;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

/// <summary>
/// Viewport-sized single-draw link overlay for a workflow surface. Renders every real link
/// (plus the in-progress virtual connection) in ONE <see cref="GraphicsView"/> draw pass that
/// lives in the decorator's coordinate space — the same frame the grid and ruler use.
///
/// It deliberately does NOT live inside the scrolling world canvas (which grows by 1/Scale on
/// zoom-in): a canvas-sized GraphicsView exceeds the Win2D ~16k device-pixel texture cap at
/// deep zoom and the whole layer silently disappears. Sized to the viewport instead, the overlay
/// transforms each endpoint's collapsed canvas-local anchor <c>c</c> (= slot/node <see cref="Anchor"/>
/// value) to viewport pixels via the identity shared with the grid drawable:
///   px = RulerThickness + c + ContentOffset − ScrollOffset
/// where ContentOffset == Layout.ActualOffset (the world origin) and ScrollOffset == the
/// ScrollViewer offset. No negative-coordinate shift / TranslationX counter-compensation is
/// needed: geometry the canvas itself would clip simply never enters the viewport, and links
/// that fall outside it are culled by bounding box before drawing.
/// </summary>
public sealed class WorkflowLinkOverlay : GraphicsView
{
    private const float ArrowHeadLength = 12f;
    private const float ArrowHeadWidth = 8f;
    private const double CullMargin = 24d;

    private static readonly Color DefaultWhite = Color.FromArgb("#DDFFFFFF");

    public static readonly BindableProperty WorkflowTreeProperty = BindableProperty.Create(
        nameof(WorkflowTree), typeof(IWorkflowTreeViewModel), typeof(WorkflowLinkOverlay), null,
        propertyChanged: OnWorkflowTreeChanged);

    public static readonly BindableProperty ScrollOffsetXProperty = BindableProperty.Create(
        nameof(ScrollOffsetX), typeof(double), typeof(WorkflowLinkOverlay), 0d, propertyChanged: OnVisualPropertyChanged);

    public static readonly BindableProperty ScrollOffsetYProperty = BindableProperty.Create(
        nameof(ScrollOffsetY), typeof(double), typeof(WorkflowLinkOverlay), 0d, propertyChanged: OnVisualPropertyChanged);

    public static readonly BindableProperty ContentOffsetXProperty = BindableProperty.Create(
        nameof(ContentOffsetX), typeof(double), typeof(WorkflowLinkOverlay), 0d, propertyChanged: OnVisualPropertyChanged);

    public static readonly BindableProperty ContentOffsetYProperty = BindableProperty.Create(
        nameof(ContentOffsetY), typeof(double), typeof(WorkflowLinkOverlay), 0d, propertyChanged: OnVisualPropertyChanged);

    public static readonly BindableProperty RulerThicknessProperty = BindableProperty.Create(
        nameof(RulerThickness), typeof(double), typeof(WorkflowLinkOverlay), 0d, propertyChanged: OnVisualPropertyChanged);

    public static readonly BindableProperty LinkLineColorProperty = BindableProperty.Create(
        nameof(LinkLineColor), typeof(Color), typeof(WorkflowLinkOverlay), DefaultWhite, propertyChanged: OnVisualPropertyChanged);

    public static readonly BindableProperty VirtualLineColorProperty = BindableProperty.Create(
        nameof(VirtualLineColor), typeof(Color), typeof(WorkflowLinkOverlay), DefaultWhite, propertyChanged: OnVisualPropertyChanged);

    public static readonly BindableProperty StrokeWidthProperty = BindableProperty.Create(
        nameof(StrokeWidth), typeof(double), typeof(WorkflowLinkOverlay), 4d, propertyChanged: OnVisualPropertyChanged);

    private IWorkflowTreeViewModel? _tree;
    private IWorkflowLinkViewModel? _virtualLink;
    private readonly HashSet<IWorkflowNodeViewModel> _subscribedNodes = [];
    private readonly HashSet<IWorkflowLinkViewModel> _subscribedLinks = [];
    private bool _invalidatePending;

    public WorkflowLinkOverlay()
    {
        InputTransparent = true;
        Drawable = new LinkOverlayDrawable(this);
    }

    public IWorkflowTreeViewModel? WorkflowTree { get => (IWorkflowTreeViewModel?)GetValue(WorkflowTreeProperty); set => SetValue(WorkflowTreeProperty, value); }
    public double ScrollOffsetX { get => (double)GetValue(ScrollOffsetXProperty); set => SetValue(ScrollOffsetXProperty, value); }
    public double ScrollOffsetY { get => (double)GetValue(ScrollOffsetYProperty); set => SetValue(ScrollOffsetYProperty, value); }
    public double ContentOffsetX { get => (double)GetValue(ContentOffsetXProperty); set => SetValue(ContentOffsetXProperty, value); }
    public double ContentOffsetY { get => (double)GetValue(ContentOffsetYProperty); set => SetValue(ContentOffsetYProperty, value); }
    public double RulerThickness { get => (double)GetValue(RulerThicknessProperty); set => SetValue(RulerThicknessProperty, value); }
    public Color? LinkLineColor { get => (Color?)GetValue(LinkLineColorProperty); set => SetValue(LinkLineColorProperty, value); }
    public Color? VirtualLineColor { get => (Color?)GetValue(VirtualLineColorProperty); set => SetValue(VirtualLineColorProperty, value); }
    public double StrokeWidth { get => (double)GetValue(StrokeWidthProperty); set => SetValue(StrokeWidthProperty, value); }

    private static void OnWorkflowTreeChanged(BindableObject bindable, object? oldValue, object? newValue)
    {
        if (bindable is WorkflowLinkOverlay overlay)
        {
            overlay.AttachTree(newValue as IWorkflowTreeViewModel);
            overlay.ScheduleInvalidate();
        }
    }

    private static void OnVisualPropertyChanged(BindableObject bindable, object? oldValue, object? newValue)
    {
        if (bindable is WorkflowLinkOverlay overlay)
        {
            overlay.ScheduleInvalidate();
        }
    }

    // ── Model subscriptions (mirrors the superseded LinkLayerView pattern) ─────

    private void AttachTree(IWorkflowTreeViewModel? tree)
    {
        if (ReferenceEquals(_tree, tree))
        {
            return;
        }

        Unsubscribe();
        _tree = tree;
        if (tree is null)
        {
            return;
        }

        Subscribe(tree);
    }

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
            ScheduleInvalidate();
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

        ScheduleInvalidate();
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

        ScheduleInvalidate();
    }

    private void OnNodePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IWorkflowNodeViewModel.Anchor) or nameof(IWorkflowNodeViewModel.Size))
        {
            ScheduleInvalidate();
        }
    }

    private void OnLinkPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IWorkflowLinkViewModel.IsVisible))
        {
            ScheduleInvalidate();
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

            ScheduleInvalidate();
        }
    }

    private void OnSlotPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IWorkflowSlotViewModel.Anchor))
        {
            ScheduleInvalidate();
        }
    }

    /// <summary>
    /// A scroll/pan/zoom frame writes several offset DPs back-to-back, and every model
    /// event above wants a redraw too. Coalesce them into ONE Win2D Invalidate per frame
    /// by flushing at the next main-thread dispatch instead of invalidating inline.
    /// </summary>
    private void ScheduleInvalidate()
    {
        if (_invalidatePending)
        {
            return;
        }

        _invalidatePending = true;
        MainThread.BeginInvokeOnMainThread(FlushInvalidate);
    }

    private void FlushInvalidate()
    {
        _invalidatePending = false;
        Invalidate();
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

    private sealed class LinkOverlayDrawable(WorkflowLinkOverlay owner) : IDrawable
    {
        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            var tree = owner._tree;
            if (tree is null || dirtyRect.Width <= 0 || dirtyRect.Height <= 0)
            {
                return;
            }

            var ruler = Math.Max(0d, owner.RulerThickness);
            var ox = owner.ContentOffsetX;
            var oy = owner.ContentOffsetY;
            var scrollX = owner.ScrollOffsetX;
            var scrollY = owner.ScrollOffsetY;
            var linkColor = owner.LinkLineColor;
            var virtualColor = owner.VirtualLineColor;
            var strokeWidth = (float)Math.Max(0.5d, owner.StrokeWidth);

            // Viewport bounds (expanded a little so a link edge never pops at the frame boundary).
            var left = dirtyRect.Left - (float)CullMargin;
            var right = dirtyRect.Right + (float)CullMargin;
            var top = dirtyRect.Top - (float)CullMargin;
            var bottom = dirtyRect.Bottom + (float)CullMargin;

            // Transform each collapsed canvas-local anchor c to viewport pixels:
            // px = Ruler + c + ContentOffset − ScrollOffset (shared with the grid drawable).
            float ToX(float c) => (float)(ruler + c + ox - scrollX);
            float ToY(float c) => (float)(ruler + c + oy - scrollY);

            foreach (var link in EnumerateVisibleLinks(tree))
            {
                if (!TryGetEndpoints(link, out var csx, out var csy, out var cex, out var cey))
                {
                    continue;
                }

                var isVirtual = IsVirtualLink(link);
                var color = isVirtual ? virtualColor : linkColor;
                if (color is null)
                {
                    continue;
                }

                const float phi = 0.6180339887f;
                var stub = ((cex - csx) / 2f) * (1f - phi);

                var startX = ToX(csx);
                var startY = ToY(csy);
                var turn1X = ToX(csx + stub);
                var turn2X = ToX(cex - stub);
                var endX = ToX(cex);
                var endY = ToY(cey);

                // Whole-link bounding-box cull: segments outside the viewport are clipped by the
                // canvas anyway, so drawing them is pure waste (and deep zoom could send huge
                // coordinates through Win2D otherwise).
                var minX = Math.Min(startX, Math.Min(turn1X, Math.Min(turn2X, endX)));
                var maxX = Math.Max(startX, Math.Max(turn1X, Math.Max(turn2X, endX)));
                var minY = Math.Min(startY, endY);
                var maxY = Math.Max(startY, endY);
                if (maxX < left || minX > right || maxY < top || minY > bottom)
                {
                    continue;
                }

                canvas.StrokeColor = color;
                canvas.StrokeSize = strokeWidth;
                canvas.StrokeDashPattern = isVirtual ? [4, 2] : null;
                canvas.DrawLine(startX, startY, turn1X, startY);
                canvas.DrawLine(turn1X, startY, turn2X, endY);
                canvas.DrawLine(turn2X, endY, endX, endY);

                if (!isVirtual)
                {
                    DrawArrowhead(canvas, turn2X, endY, endX, endY, color);
                }

                canvas.StrokeDashPattern = null;
            }
        }

        private static void DrawArrowhead(ICanvas canvas, float fromX, float fromY, float tipX, float tipY, Color color)
        {
            var dx = tipX - fromX;
            var dy = tipY - fromY;
            var length = MathF.Sqrt((dx * dx) + (dy * dy));
            if (length <= float.Epsilon)
            {
                return;
            }

            dx /= length;
            dy /= length;
            var normalX = -dy;
            var normalY = dx;
            var baseX = tipX - (dx * ArrowHeadLength);
            var baseY = tipY - (dy * ArrowHeadLength);
            var leftX = baseX + (normalX * (ArrowHeadWidth / 2f));
            var leftY = baseY + (normalY * (ArrowHeadWidth / 2f));
            var rightX = baseX - (normalX * (ArrowHeadWidth / 2f));
            var rightY = baseY - (normalY * (ArrowHeadWidth / 2f));

            var arrow = new PathF();
            arrow.MoveTo(tipX, tipY);
            arrow.LineTo(leftX, leftY);
            arrow.LineTo(rightX, rightY);
            arrow.Close();

            canvas.FillColor = color;
            canvas.FillPath(arrow);
        }
    }
}
