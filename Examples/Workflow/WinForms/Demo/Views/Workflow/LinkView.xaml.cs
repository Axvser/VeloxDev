// VeloxDev customization: customize the curve, colours, hit-test policy and the travelling light here.
using System.Drawing;
using System.Drawing.Drawing2D;
using VeloxDev.TransitionSystem;
using VeloxDev.WinForms.Xaml;
using VeloxDev.WorkflowSystem;
using Demo.Workflow;
using DrawingPen = System.Drawing.Pen;

namespace Demo.Views.Workflow;

/// <summary>
/// Cubic Bézier connection that publishes the curve Core hit-tests, paints it in world coordinates and runs the
/// travelling light along it by arc length.
/// </summary>
/// <remarks>
/// The light is a comet: a bright head, a tail that fades behind it and a halo around both. It is cut out of the
/// curve by arc length rather than by a gradient brush, because a gradient's axis is the straight line between the
/// ends and on a curve it lights the string instead of the rope.
/// </remarks>
public sealed partial class LinkView : VeloxDev.WinForms.Xaml.Visuals.Controls.UserControl
{
    private const double MinimumPull = 40d;

    // 拖尾占全长的比例，以及分几段画。拖尾越长越像流光，越短越像一个亮点在跑。
    private const double TailFraction = 0.30d;
    private const int TailSegments = 16;

    // 三段相位的时长：出发、行进、到达。
    private const float LineWidth = 2f;
    private const float LineWidthHighlighted = 3.5f;

    private static readonly TimeSpan EnterDuration = TimeSpan.FromMilliseconds(450);
    private static readonly TimeSpan TravelDuration = TimeSpan.FromMilliseconds(700);
    private static readonly TimeSpan ExitDuration = TimeSpan.FromMilliseconds(450);

    private ILinkComponent? _publishedLink;
    private LinkCurve? _curve;
    private long _paintCountAtTick;
    private bool _disposed;

    // 一棵树共用一个时钟；本视图只订它，并在 tick 里把两个标量抄下来。
    private WorkflowFlowClock? _clock;
    private readonly Action _clockTick;
    private double _bandHead;
    private double _bandIntensity;

    // 静息笔与彗星笔都按配色/线宽缓存；逐帧变的只有透明度，写在笔的颜色上。
    private readonly GraphicsPath _bodyPath = new();
    private readonly GraphicsPath[] _tailPaths = CreateTailPaths();
    private readonly DrawingPen[] _bloomPens = new DrawingPen[TailSegments];
    private readonly DrawingPen[] _cometPens = new DrawingPen[TailSegments];
    private DrawingPen? _glowOuterPen;
    private DrawingPen? _glowInnerPen;
    private DrawingPen? _bodyPen;
    private DrawingPen? _dashedPen;
    private Color _pensColor;
    private float _pensThickness;
    private bool _pensHighlighted;
    private bool _pensVirtual;
    private bool _pensReady;
    private Color _cometPensColor;
    private float _cometPensThickness;
    private bool _cometPensReady;

    static LinkView()
        => WorkflowFlowClock.RegisterDriver(static clock => new FlowDriver(clock));

    /// <summary>Creates the connection view; it does not take pointer hits, the surface resolves those.</summary>
    public LinkView()
    {
        _clockTick = OnClockTick;
        InitializeComponent();
        IsHitTestVisible = false;
    }

    /// <summary>The horizontal position of the sender end.</summary>
    public static readonly DependencyProperty StartLeftProperty = DependencyProperty.Register(
        nameof(StartLeft), typeof(double), typeof(LinkView),
        new PropertyMetadata(0d) { AffectsRender = true });

    /// <summary>The vertical position of the sender end.</summary>
    public static readonly DependencyProperty StartTopProperty = DependencyProperty.Register(
        nameof(StartTop), typeof(double), typeof(LinkView),
        new PropertyMetadata(0d) { AffectsRender = true });

    /// <summary>The horizontal position of the receiver end.</summary>
    public static readonly DependencyProperty EndLeftProperty = DependencyProperty.Register(
        nameof(EndLeft), typeof(double), typeof(LinkView),
        new PropertyMetadata(0d) { AffectsRender = true });

    /// <summary>The vertical position of the receiver end.</summary>
    public static readonly DependencyProperty EndTopProperty = DependencyProperty.Register(
        nameof(EndTop), typeof(double), typeof(LinkView),
        new PropertyMetadata(0d) { AffectsRender = true });

    /// <summary>Whether the link has both endpoints measured and may be drawn.</summary>
    public static readonly DependencyProperty CanRenderProperty = DependencyProperty.Register(
        nameof(CanRender), typeof(bool), typeof(LinkView),
        new PropertyMetadata(true) { AffectsRender = true });

    /// <summary>Whether this view is the pointer's drag preview rather than a real connection.</summary>
    public static readonly DependencyProperty IsVirtualProperty = DependencyProperty.Register(
        nameof(IsVirtual), typeof(bool), typeof(LinkView),
        new PropertyMetadata(false) { AffectsRender = true });

    /// <summary>The resting line colour.</summary>
    public static readonly DependencyProperty LineColorProperty = DependencyProperty.Register(
        nameof(LineColor), typeof(Color), typeof(LinkView),
        new PropertyMetadata(Color.FromArgb(0xDD, 0xFF, 0xFF, 0xFF)) { AffectsRender = true });

    /// <summary>The colour the link turns into while it is the hovered one.</summary>
    public static readonly DependencyProperty HighlightColorProperty = DependencyProperty.Register(
        nameof(HighlightColor), typeof(Color), typeof(LinkView),
        new PropertyMetadata(Color.White) { AffectsRender = true });

    /// <summary>Whether this link is the hovered one.</summary>
    public static readonly DependencyProperty IsHighlightedProperty = DependencyProperty.Register(
        nameof(IsHighlighted), typeof(bool), typeof(LinkView),
        new PropertyMetadata(false) { AffectsRender = true });

    /// <summary>The horizontal position of the sender end.</summary>
    public double StartLeft { get => (double)GetValue(StartLeftProperty)!; set => SetValue(StartLeftProperty, value); }

    /// <summary>The vertical position of the sender end.</summary>
    public double StartTop { get => (double)GetValue(StartTopProperty)!; set => SetValue(StartTopProperty, value); }

    /// <summary>The horizontal position of the receiver end.</summary>
    public double EndLeft { get => (double)GetValue(EndLeftProperty)!; set => SetValue(EndLeftProperty, value); }

    /// <summary>The vertical position of the receiver end.</summary>
    public double EndTop { get => (double)GetValue(EndTopProperty)!; set => SetValue(EndTopProperty, value); }

    /// <summary>Whether the link has both endpoints measured and may be drawn.</summary>
    public bool CanRender { get => (bool)GetValue(CanRenderProperty)!; set => SetValue(CanRenderProperty, value); }

    /// <summary>Whether this view is the pointer's drag preview rather than a real connection.</summary>
    public bool IsVirtual { get => (bool)GetValue(IsVirtualProperty)!; set => SetValue(IsVirtualProperty, value); }

    /// <summary>The resting line colour.</summary>
    public Color LineColor { get => (Color)GetValue(LineColorProperty)!; set => SetValue(LineColorProperty, value); }

    /// <summary>The colour the link turns into while it is the hovered one.</summary>
    public Color HighlightColor { get => (Color)GetValue(HighlightColorProperty)!; set => SetValue(HighlightColorProperty, value); }

    /// <summary>Whether this link is the hovered one.</summary>
    public bool IsHighlighted { get => (bool)GetValue(IsHighlightedProperty)!; set => SetValue(IsHighlightedProperty, value); }

    /// <inheritdoc />
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        // 池子换项时旧视图被释放，但它的绑定仍挂在模型上；释放后迟到的一次锚点更新不该再碰 GDI+。
        if (_disposed)
        {
            return;
        }

        base.OnPropertyChanged(e);
        if (ReferenceEquals(e.Property, DataContextProperty))
        {
            _publishedLink?.PublishCurve(null);
            _publishedLink = null;
            _curve = null;
            ResetFlow();
            InvalidateVisual();
        }
        else if (ReferenceEquals(e.Property, StartLeftProperty)
                 || ReferenceEquals(e.Property, StartTopProperty)
                 || ReferenceEquals(e.Property, EndLeftProperty)
                 || ReferenceEquals(e.Property, EndTopProperty))
        {
            RefreshGeometry();
        }
        else if (ReferenceEquals(e.Property, CanRenderProperty) || ReferenceEquals(e.Property, IsVirtualProperty))
        {
            RefreshFlowSubscription();
        }
    }

    /// <inheritdoc />
    protected override RectangleF GetInvalidationBounds()
    {
        // 视图铺满整个画布，但每帧动的只有这条曲线；按路径包围盒重绘，30fps 的流光才不会每帧刷整面。
        if (_bodyPath.PointCount == 0)
        {
            return base.GetInvalidationBounds();
        }

        var bounds = _bodyPath.GetBounds();
        bounds.Inflate(16f, 16f);
        return bounds;
    }

    /// <inheritdoc />
    protected override void OnRender(Graphics graphics)
    {
        if (_disposed || !CanRender || DataContext is not ILinkComponent link || !link.IsRenderReady())
        {
            return;
        }

        // 视图在池里被改绑/复活后，订阅只在真正要画时才建立 —— 视觉树没有 Loaded 事件。
        RefreshFlowSubscription();

        // 任何一次绘制都按真实时间推进流光：拖动/平移期间定时器不发帧，这些重绘仍让彗星在走。
        if (_clock is { } flowClock)
        {
            flowClock.Sample();
            _bandHead = flowClock.BandHead;
            _bandIntensity = flowClock.BandIntensity;
        }

        if (_curve is null)
        {
            RefreshGeometry();
        }

        var curve = _curve;
        if (curve is null || curve.Length <= 0d)
        {
            return;
        }

        var color = IsHighlighted ? HighlightColor : LineColor;
        var isVirtual = IsVirtual || (link.Sender?.Parent is null && link.Receiver?.Parent is null);
        var thickness = IsHighlighted ? LineWidthHighlighted : LineWidth;

        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        EnsurePens(color, thickness, isVirtual);

        // 两层更宽的同色低透明描边垫在下面，整条线因此像在发光而不是贴在背景上。
        graphics.DrawPath(_glowOuterPen!, _bodyPath);
        graphics.DrawPath(_glowInnerPen!, _bodyPath);

        if (isVirtual)
        {
            graphics.DrawPath(_dashedPen!, _bodyPath);
            return;
        }

        graphics.DrawPath(_bodyPen!, _bodyPath);

        if (_bandIntensity > 0.001d)
        {
            DrawComet(graphics, curve, color, thickness);
        }
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _disposed = true;
            UnsubscribeFlow();
            _publishedLink?.PublishCurve(null);
            _publishedLink = null;

            _bodyPath.Dispose();
            foreach (var path in _tailPaths)
            {
                path.Dispose();
            }

            foreach (var pen in _bloomPens)
            {
                pen?.Dispose();
            }

            foreach (var pen in _cometPens)
            {
                pen?.Dispose();
            }

            _glowOuterPen?.Dispose();
            _glowInnerPen?.Dispose();
            _bodyPen?.Dispose();
            _dashedPen?.Dispose();
        }

        base.Dispose(disposing);
    }

    // ── flow ────────────────────────────────────────────────────────────────

    // 端点或数据上下文变了才重建：绘制、取点与命中共用这一份弧长表，帧回调不碰它。
    private void RefreshGeometry()
    {
        var link = DataContext as ILinkComponent;

        // 端口尚未测量时锚点是 NaN：把它交给 GDI+ 会让路径进入无效状态，之后连 Reset 都会抛。
        if (link is null || !link.IsRenderReady()
            || !double.IsFinite(StartLeft) || !double.IsFinite(StartTop)
            || !double.IsFinite(EndLeft) || !double.IsFinite(EndTop))
        {
            _curve = null;
            _bodyPath.Reset();
            if (_publishedLink is not null)
            {
                _publishedLink.PublishCurve(null);
                _publishedLink = null;
            }

            return;
        }

        var points = LinkCurve.LinkCurvePoints(link, StartLeft, StartTop, EndLeft, EndTop, MinimumPull);
        _curve = LinkCurve.FromCubic(
            points[0].X, points[0].Y, points[1].X, points[1].Y,
            points[2].X, points[2].Y, points[3].X, points[3].Y);

        _bodyPath.Reset();
        _bodyPath.AddBezier(
            (float)points[0].X, (float)points[0].Y,
            (float)points[1].X, (float)points[1].Y,
            (float)points[2].X, (float)points[2].Y,
            (float)points[3].X, (float)points[3].Y);

        if (!ReferenceEquals(_publishedLink, link))
        {
            _publishedLink?.PublishCurve(null);
            _publishedLink = link;
        }

        link?.PublishCurve(_curve, this);
    }

    private void RefreshFlowSubscription()
    {
        var clock = CanRender && !IsVirtual && Surface is not null
            ? WorkflowFlowClock.ForLink(DataContext as ILinkComponent)
            : null;

        if (ReferenceEquals(clock, _clock))
        {
            return;
        }

        UnsubscribeFlow();
        _clock = clock;
        _clock?.Subscribe(_clockTick);
    }

    private void UnsubscribeFlow()
    {
        if (_clock is null)
        {
            return;
        }

        _clock.Unsubscribe(_clockTick);
        _clock = null;
        _bandHead = 0d;
        _bandIntensity = 0d;
    }

    private void ResetFlow()
    {
        _bandHead = 0d;
        _bandIntensity = 0d;
        RefreshFlowSubscription();
    }

    private void OnClockTick()
    {
        var clock = _clock;
        if (clock is null)
        {
            return;
        }

        // 拖动/平移的重绘会推进彗星；这类重绘之后的定时帧让位，停止移动后下一拍恢复，按住不动仍有帧。
        if (Surface is { } surface)
        {
            var paintCount = surface.PaintCount;
            if (paintCount != _paintCountAtTick)
            {
                _paintCountAtTick = paintCount;
                if (!surface.LastPaintWasClockDriven)
                {
                    return;
                }
            }

            surface.MarkNextPaintAsClockDriven();
        }

        var previousHead = _bandHead;
        var previousIntensity = _bandIntensity;
        _bandHead = clock.BandHead;
        _bandIntensity = clock.BandIntensity;

        // 只有彗星尾段在动：重画旧尾段与新尾段的并集，静态线身和别的连线不参与。
        if (_curve is { Length: > 0d } curve)
        {
            var previous = CometBounds(curve, previousHead, previousIntensity);
            var next = CometBounds(curve, _bandHead, _bandIntensity);
            var damage = previous.Width > 0f
                ? (next.Width > 0f ? RectangleF.Union(previous, next) : previous)
                : next;
            if (damage.Width > 0f && damage.Height > 0f)
            {
                InvalidateVisual(damage);
                return;
            }

            if (previous.Width <= 0f && next.Width <= 0f)
            {
                return;
            }
        }

        InvalidateVisual();
    }

    // 尾段沿弧长 [头 - 30% 全长, 头]；取采样点的包围盒，再给 9px 光晕留外扩。
    private static RectangleF CometBounds(LinkCurve curve, double head, double intensity)
    {
        if (intensity <= 0.004d || curve.Length <= 0d)
        {
            return RectangleF.Empty;
        }

        var end = Math.Clamp(head, 0d, 1d) * curve.Length;
        var start = end - (TailFraction * curve.Length);
        float minX = float.MaxValue;
        float minY = float.MaxValue;
        float maxX = float.MinValue;
        float maxY = float.MinValue;
        var found = false;
        for (var index = 0; index <= 8; index++)
        {
            var length = start + ((end - start) * index / 8d);
            if (length < 0d || length > curve.Length)
            {
                continue;
            }

            var point = curve.PointAtLength(length);
            minX = Math.Min(minX, (float)point.X);
            minY = Math.Min(minY, (float)point.Y);
            maxX = Math.Max(maxX, (float)point.X);
            maxY = Math.Max(maxY, (float)point.Y);
            found = true;
        }

        if (!found)
        {
            return RectangleF.Empty;
        }

        return RectangleF.FromLTRB(minX - 20f, minY - 20f, maxX + 20f, maxY + 20f);
    }

    // 每棵树只建一次，30fps 共享一条周期；各视图不再各起一条计时器。
    // WinForms 这条用线程池 Timer + UI 投递，而不是 Transition 或 WinForms Timer：
    // - Transition 的 await 续体会把帧投回 WindowsFormsSynchronizationContext，句柄状态异常时会在线程池上崩；
    // - WinForms Timer 走 WM_TIMER，连续输入时会被饿死，流光看起来冻住。
    // 投递失败（关闭中/句柄已销毁）直接丢弃这一帧，不让异常逃到线程池。
    private sealed class FlowDriver : IWorkflowFlowDriver
    {
        private const int FrameIntervalMs = 33;

        private readonly WorkflowFlowClock _clock;
        private readonly SynchronizationContext? _ui;
        private readonly System.Threading.Timer _timer;
        private readonly TransitionEventArgs _args = new();
        private int _pending;

        public FlowDriver(WorkflowFlowClock clock)
        {
            _clock = clock;
            _ui = SynchronizationContext.Current;
            _timer = new System.Threading.Timer(OnTimer, null, Timeout.Infinite, Timeout.Infinite);
        }

        public void Start()
        {
            _clock.BeginSampling(EnterDuration, TravelDuration, ExitDuration);
            _timer.Change(0, FrameIntervalMs);
        }

        public void Stop()
        {
            _timer.Change(Timeout.Infinite, Timeout.Infinite);
            _clock.EndSampling();
        }

        private void OnTimer(object? state)
        {
            if (Interlocked.CompareExchange(ref _pending, 1, 0) != 0)
            {
                return;
            }

            try
            {
                if (_ui is not null)
                {
                    _ui.Post(static driver => ((FlowDriver)driver!).Tick(), this);
                    return;
                }
            }
            catch (Exception exception) when (exception is InvalidOperationException or ObjectDisposedException)
            {
                // The dispatcher lost its handle during shutdown; fall through and drop the frame.
            }

            Volatile.Write(ref _pending, 0);
        }

        private void Tick()
        {
            Volatile.Write(ref _pending, 0);
            _clock.Sample();
            _clock.LateUpdateHandler(_clock, _args);
        }
    }

    // ── render helpers ──────────────────────────────────────────────────────

    private void EnsurePens(Color color, float thickness, bool isVirtual)
    {
        if (_pensReady
            && color == _pensColor
            && Math.Abs(thickness - _pensThickness) < 0.01f
            && isVirtual == _pensVirtual
            && IsHighlighted == _pensHighlighted)
        {
            return;
        }

        _glowOuterPen?.Dispose();
        _glowInnerPen?.Dispose();
        _bodyPen?.Dispose();
        _dashedPen?.Dispose();

        _glowOuterPen = GlowPen(color, 0.10d, thickness + 9f);
        _glowInnerPen = GlowPen(color, 0.16d, thickness + 4f);

        _dashedPen = new DrawingPen(AtAlpha(color, 0.75d), thickness)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            DashPattern = [4f, 2f],
        };

        _bodyPen = new DrawingPen(AtAlpha(color, IsHighlighted ? 0.85d : 0.55d), thickness)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round,
        };

        _pensColor = color;
        _pensThickness = thickness;
        _pensVirtual = isVirtual;
        _pensHighlighted = IsHighlighted;
        _pensReady = true;
    }

    private static DrawingPen GlowPen(Color color, double alpha, float thickness)
        => new(AtAlpha(color, alpha), thickness)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round,
        };

    // 彗星的每段有一个固定混合色与线宽；逐帧变的只有画这一笔时的透明度。
    private void EnsureCometPens(Color color, float thickness)
    {
        if (_cometPensReady
            && color == _cometPensColor
            && Math.Abs(thickness - _cometPensThickness) < 0.01f)
        {
            return;
        }

        for (var k = 0; k < TailSegments; k++)
        {
            var f = k / (double)TailSegments;
            var mixed = Mix(color, Color.White, f);
            _bloomPens[k]?.Dispose();
            _cometPens[k]?.Dispose();
            _bloomPens[k] = new DrawingPen(mixed, thickness + 9f);
            _cometPens[k] = new DrawingPen(mixed, (float)(thickness * (0.45d + (0.95d * f))))
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round,
            };
        }

        _cometPensColor = color;
        _cometPensThickness = thickness;
        _cometPensReady = true;
    }

    // 沿弧长切出 [头-尾, 头]，分若干小段，每段给递减的透明度与向白色过渡的颜色。
    private void DrawComet(Graphics graphics, LinkCurve curve, Color color, float thickness)
    {
        if (curve.Length <= 0d)
        {
            return;
        }

        var head = Math.Clamp(_bandHead, 0d, 1d) * curve.Length;
        var tail = TailFraction * curve.Length;
        EnsureCometPens(color, thickness);

        for (var k = 0; k < TailSegments; k++)
        {
            var f0 = k / (double)TailSegments;
            var f1 = (k + 1) / (double)TailSegments;
            var l0 = head - (tail * (1d - f0));
            var l1 = head - (tail * (1d - f1));
            if (l1 <= 0d || l0 >= curve.Length)
            {
                continue;
            }

            // 平方衰减：透明度集中在尾段，读起来才像拖尾而不是一条均匀的带。
            var alpha = _bandIntensity * f0 * f0;
            if (alpha <= 0.004d)
            {
                continue;
            }

            BuildSegmentPath(_tailPaths[k], curve, l0, l1);

            // 先光晕后本体，两遍都跟着头走，动感在光晕上也读得出来。
            _bloomPens[k]!.Color = AtAlpha(_bloomPens[k]!.Color, alpha * 0.22d);
            graphics.DrawPath(_bloomPens[k]!, _tailPaths[k]);

            _cometPens[k]!.Color = AtAlpha(_cometPens[k]!.Color, alpha);
            graphics.DrawPath(_cometPens[k]!, _tailPaths[k]);
        }
    }

    private static void BuildSegmentPath(GraphicsPath path, LinkCurve curve, double from, double to)
    {
        to = Math.Min(to, curve.Length);
        from = Math.Clamp(from, 0d, curve.Length);

        path.Reset();
        var start = curve.PointAtLength(from);
        path.AddLine((float)start.X, (float)start.Y, (float)start.X, (float)start.Y);

        for (var i = 0; i < curve.Count; i++)
        {
            var length = curve.LengthAt(i);
            if (length <= from || length >= to)
            {
                continue;
            }

            path.AddLine((float)curve.XAt(i), (float)curve.YAt(i), (float)curve.XAt(i), (float)curve.YAt(i));
        }

        var end = curve.PointAtLength(to);
        path.AddLine((float)end.X, (float)end.Y, (float)end.X, (float)end.Y);
    }

    private static GraphicsPath[] CreateTailPaths()
    {
        var paths = new GraphicsPath[TailSegments];
        for (var index = 0; index < paths.Length; index++)
        {
            paths[index] = new GraphicsPath();
        }

        return paths;
    }

    private static Color Mix(Color from, Color to, double t)
    {
        byte Lerp(byte a, byte b) => (byte)Math.Round(a + ((b - a) * t));
        return Color.FromArgb(Lerp(from.A, to.A), Lerp(from.R, to.R), Lerp(from.G, to.G), Lerp(from.B, to.B));
    }

    private static Color AtAlpha(Color color, double alpha)
        => Color.FromArgb((int)Math.Round(Math.Clamp(alpha, 0d, 1d) * 255d), color.R, color.G, color.B);
}
