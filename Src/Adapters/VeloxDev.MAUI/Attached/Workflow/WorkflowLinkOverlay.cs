using System.Collections.Specialized;
using System.ComponentModel;
using Microsoft.Maui.Graphics;
using VeloxDev.TransitionSystem;
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

    public static readonly BindableProperty LinkFlowEnabledProperty = BindableProperty.Create(
        nameof(LinkFlowEnabled), typeof(bool), typeof(WorkflowLinkOverlay), false, propertyChanged: OnLinkFlowEnabledChanged);

    private IWorkflowTreeViewModel? _tree;
    private IWorkflowLinkViewModel? _virtualLink;
    private readonly HashSet<IWorkflowNodeViewModel> _subscribedNodes = [];
    private readonly HashSet<IWorkflowLinkViewModel> _subscribedLinks = [];
    private bool _invalidatePending;

    private double _bandCentre;
    private double _bandMix;

    public WorkflowLinkOverlay()
    {
        InputTransparent = true;
        Drawable = new LinkOverlayDrawable(this);

        // 逐帧动画要在 UI 线程上从 Loaded 起动、Unloaded 停止；移出树后不能还留着帧到达链接层
        Loaded += (_, _) => StartFlow();
        Unloaded += (_, _) => StopFlow();
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

    /// <summary>Whether a settled link carries the travelling highlight that shows which way its data
    /// flows. Off by default, because it is decoration: a minimal editor draws plain links.</summary>
    public bool LinkFlowEnabled { get => (bool)GetValue(LinkFlowEnabledProperty); set => SetValue(LinkFlowEnabledProperty, value); }

    /// <summary>Where the band is, as a fraction of a link's length from its sender's end. Written by the
    /// flow every frame; each link derives its geometry and colours from it while drawing.</summary>
    public double BandCentre
    {
        get => _bandCentre;
        set
        {
            _bandCentre = value;
            ScheduleInvalidate();
        }
    }

    /// <summary>How far the band has come up from the line's resting colour to the lit one: 0 is not there
    /// yet, 1 is fully lit. It climbs while the band enters and falls while it leaves.</summary>
    public double BandMix
    {
        get => _bandMix;
        set
        {
            _bandMix = value;
            ScheduleInvalidate();
        }
    }

    #region Flow effect

    // 光带半宽（沿链的渐变偏移单位）
    private const double FlowBandHalfWidth = 0.04;

    // 三段相位各自结束时光带中心的位置：成形、全亮行进、退去
    private const double FlowBandFrom = 0.06;
    private const double FlowBandFormed = 0.34;
    private const double FlowBandLeaving = 0.66;
    private const double FlowBandTo = 0.94;

    // 整层只跑一个动画：本层一趟画完所有链接，光带因此一起前进；只动两个数（位置、亮度），各链接自推几何与颜色
    // 相位成链：段的 LoopTime 只重复该段，Repeat 用首轮捕获的端点重跑整条链
    private static readonly Transition<WorkflowLinkOverlay> Flow =
        Transition<WorkflowLinkOverlay>.Create()
            // 相位一：一边成形一边进入（走三分之一路程，同时由静息色变亮）
            .Property(o => o.BandCentre, FlowBandFormed)
            .Property(o => o.BandMix, 1d)
            .Effect(new TransitionEffect()
            {
                Duration = TimeSpan.FromMilliseconds(550),
                Ease = Eases.Default,
            })
            .Then()
            // 相位二：保持全亮只移动——这一段读起来才是流动而非脉冲
            .Property(o => o.BandCentre, FlowBandLeaving)
            .Effect(new TransitionEffect()
            {
                Duration = TimeSpan.FromMilliseconds(650),
                Ease = Eases.Default,
            })
            .Then()
            // 相位三：一边退回静息色一边离开；周期两端都是均匀暗色，循环接缝才看不出来
            .Property(o => o.BandCentre, FlowBandTo)
            .Property(o => o.BandMix, 0d)
            .Effect(new TransitionEffect()
            {
                Duration = TimeSpan.FromMilliseconds(550),
                Ease = Eases.Default,
            })
            .Repeat(int.MaxValue);

    // 起动流动；Loaded 与开关打开时都调，光带因此总从发送端开始，而不是上次停在哪就从哪
    private void StartFlow()
    {
        if (!LinkFlowEnabled)
        {
            return;
        }

        // 链从目标读起始值，Execute 前先回到起点；循环在每个接缝重放这份捕获值
        _bandCentre = FlowBandFrom;
        _bandMix = 0d;
        Flow.Execute(this);
    }

    // 停流动，并让链释放它持有的资源
    private void StopFlow()
        => Transition.Exit(this, IncludeMutual: true, IncludeNoMutual: true);

    private static void OnLinkFlowEnabledChanged(BindableObject bindable, object? oldValue, object? newValue)
    {
        if (bindable is not WorkflowLinkOverlay overlay)
        {
            return;
        }

        if (newValue is true)
        {
            overlay.StartFlow();
        }
        else
        {
            overlay.StopFlow();
        }

        overlay.ScheduleInvalidate();
    }

    // 亮色：链接本色提到全不透明，各通道再向白抬一点，白链接也留出更亮处
    private static Color FlowLit(Color color)
    {
        const double lift = 0.45;

        float Up(float channel) => (float)Math.Round(channel + (1d - channel) * lift, 6);

        return new Color(Up(color.Red), Up(color.Green), Up(color.Blue), 1f);
    }

    // 静息色：亮色按 alpha 变暗到约 62%，色相不变；往白里提在青线上几乎看不出（实测过）
    private static Color FlowDim(Color color)
        => new(color.Red, color.Green, color.Blue, (float)(color.Alpha * 0.62));

    private static Color FlowBlend(Color from, Color to, double t) => new(
        (float)Math.Round(from.Red + (to.Red - from.Red) * t, 6),
        (float)Math.Round(from.Green + (to.Green - from.Green) * t, 6),
        (float)Math.Round(from.Blue + (to.Blue - from.Blue) * t, 6),
        (float)Math.Round(from.Alpha + (to.Alpha - from.Alpha) * t, 6));

    // 流动链接分成的份数：够细看不出台阶，也够少撑得住满屏链接的每帧开销
    private const int FlowSegments = 24;

    // 链上某点应得的颜色；u 是它在链上的位置
    private static Color BandColorAt(Color dim, Color lit, double u, double centre, double mix)
    {
        var reach = Math.Abs(u - centre) / FlowBandHalfWidth;
        return reach >= 1d ? dim : FlowBlend(dim, lit, mix * (1d - reach));
    }

    // 以流动画一条链接并返回箭头该用的颜色：链接画成一串短段，每段取映射在该处的颜色
    // 本版 ICanvas 只有 SetFillPaint、无描边等价物，渐变描边不可表达；沿链长度采样也让光带能跟着肘部拐角走
    private Color ApplyFlowStroke(
        ICanvas canvas,
        Color color,
        float startX,
        float startY,
        float turn1X,
        float turn2X,
        float endX,
        float endY)
    {
        var lit = FlowLit(color);
        var dim = FlowDim(lit);
        // 循环写的两个值；下面全是本链接对它们的读取
        var centre = _bandCentre;
        var mix = _bandMix;

        // 肘部是三段——横档、斜线、横档；按沿链的里程放段，而不是按它跨过包围盒的比例
        var stub = MathF.Abs(turn1X - startX);
        var diagonal = MathF.Sqrt(((turn2X - turn1X) * (turn2X - turn1X)) + ((endY - startY) * (endY - startY)));
        var tail = MathF.Abs(endX - turn2X);
        var total = stub + diagonal + tail;
        if (total <= float.Epsilon)
        {
            return lit;
        }

        var previousX = startX;
        var previousY = startY;
        for (var piece = 1; piece <= FlowSegments; piece++)
        {
            var distance = (piece / (float)FlowSegments) * total;
            float x;
            float y;
            if (distance <= stub)
            {
                var t = stub <= float.Epsilon ? 1f : distance / stub;
                x = startX + ((turn1X - startX) * t);
                y = startY;
            }
            else if (distance <= stub + diagonal)
            {
                var t = diagonal <= float.Epsilon ? 1f : (distance - stub) / diagonal;
                x = turn1X + ((turn2X - turn1X) * t);
                y = startY + ((endY - startY) * t);
            }
            else
            {
                var t = tail <= float.Epsilon ? 1f : (distance - stub - diagonal) / tail;
                x = turn2X + ((endX - turn2X) * t);
                y = endY;
            }

            // 取段中点的颜色：一段带的是它所覆盖长度上的颜色而非末端，亮带才不会落后半段
            canvas.StrokeColor = BandColorAt(dim, lit, (piece - 0.5f) / FlowSegments, centre, mix);
            canvas.DrawLine(previousX, previousY, x, y);

            previousX = x;
            previousY = y;
        }

        return lit;
    }

    #endregion

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

                canvas.StrokeSize = strokeWidth;
                canvas.StrokeDashPattern = isVirtual ? [4, 2] : null;

                // 流动链接按光带颜色分段画；箭头用亮色——线体静息是暗的，箭头跟着暗就永远不亮
                // 虚拟链接是指针下的橡皮筋，保持平色虚线笔
                var arrowColor = color;
                if (isVirtual || !owner.LinkFlowEnabled)
                {
                    canvas.StrokeColor = color;
                    canvas.DrawLine(startX, startY, turn1X, startY);
                    canvas.DrawLine(turn1X, startY, turn2X, endY);
                    canvas.DrawLine(turn2X, endY, endX, endY);
                }
                else
                {
                    arrowColor = owner.ApplyFlowStroke(
                        canvas, color, startX, startY, turn1X, turn2X, endX, endY);
                }

                if (!isVirtual)
                {
                    DrawArrowhead(canvas, turn2X, endY, endX, endY, arrowColor);
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
