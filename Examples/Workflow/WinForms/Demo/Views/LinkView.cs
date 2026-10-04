// VeloxDev customization: Customize line geometry, color, and thickness here.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using VeloxDev.WorkflowSystem;
// `Size` collides between System.Drawing and VeloxDev.WorkflowSystem; a drawing
// alias keeps `new Size(...)` and `RectangleF(...)` usage unambiguous.
using Size = System.Drawing.Size;

namespace Demo.Views;

/// <summary>
/// One connection between two ports, drawn as a single cubic curve that leaves each end horizontally, carrying
/// a travelling comet so the direction of data flow is readable at a glance.
/// <para>
/// The comet — a bright head, a tail that fades behind it, and a halo that follows the head — is cut out of the
/// curve <b>by arc length</b> rather than by a gradient brush. GDI+ made the difference plain: a
/// <see cref="LinearGradientBrush"/>'s axis is the straight line between the two ends, so on a curve it lights the
/// string rather than the rope; its stops can only be given at construction and are refused unless they span the
/// whole axis; and a pen keeps the stops of the brush it was built from, so the band had to be re-plumbed every
/// frame. Cutting the tail out of the geometry by arc length needs none of that — each slice is given its own
/// colour and width. The flattened curve, its arc-length table and the hit test all come from Core's
/// <see cref="LinkCurve"/> now; this view only draws it and submits it through <see cref="LinkHitTestEx.PublishCurve"/>.
/// </para>
/// <para>
/// The host canvas draws this link (<see cref="Render"/>) rather than showing it as a child window, so it never
/// participates in WinForms' fragile transparent compositing. Having no window, it can be neither hovered nor
/// focused: both live in the host. The canvas subscribes to the Core hub's
/// <see cref="LinkInteraction.HoverChanged"/> and writes <see cref="IsHighlighted"/> back onto the renderer that
/// drew the hovered link; the <c>Delete</c> key is the hub's own.
/// </para>
/// </summary>
public sealed class LinkView : Control
{
    // 曲线的最小水平拉出量（画布单位）：两个端口靠得很近时，0.5·dx 会让曲线退化成一条直线段。
    private const double PullMinimum = 40d;

    // 线宽；选中时加 1.5，与其它六家的连线一致
    private const float LineThickness = 2f;
    private const float HighlightThickness = 3.5f;

    // 选中色：柔和的淡青高光（与适配器 hub 的默认同色）。橙红太刺眼，用户明确否掉了。
    private static readonly Color HighlightColor = Color.FromArgb(255, 0xFF, 0xFF, 0xFF);

    // 拖尾占全长的比例。这是彗星唯一的观感旋钮：调大＝更长的尾、更像流光；调小＝更像一个亮点在跑。
    private const double TailFraction = 0.30;

    // 拖尾分几段画。每段一个透明度，衰减因此是连续的而不需要渐变刷。
    private const int TailSegments = 16;

    private IWorkflowLinkViewModel? _link;
    private INotifyPropertyChanged? _notifier;
    private INotifyPropertyChanged? _senderNotifier;
    private INotifyPropertyChanged? _receiverNotifier;

    private float _startLeft;
    private float _startTop;
    private float _endLeft;
    private float _endTop;
    private bool _canRender = true;
    private bool _isVirtual;
    private bool _isHighlighted;
    private Color _lineColor = CardTheme.FromHex("#CC38BDF8");

    // 本帧彗星的头部走到全长的几成、以及它有多亮：每次 Render 前由画布从它的时钟推入（SetFlow）——
    // 时钟归画布，渲染器不在控件树里、没有窗口应答它的 Invalidate
    private double _bandHead;
    private double _bandIntensity;

    // 本帧这条线的扁平化几何（画布/世界坐标，与指针同一系）。弧长、包围盒与命中判定都归它 ——
    // 本视图不再自存采样表。端点变化时重建一次。
    private LinkCurve? _curve;

    public LinkView()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.UserPaint,
            true);
        // No BackColor assignment: the link is rendered by the host canvas (Render)
        // and never shown as a child window, so it keeps the default opaque
        // background (a translucent BackColor would throw in .NET 10).
        TabStop = false;
        Enabled = false;
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public float StartLeft { get => _startLeft; set { _startLeft = value; RequestPaint(); } }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public float StartTop { get => _startTop; set { _startTop = value; RequestPaint(); } }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public float EndLeft { get => _endLeft; set { _endLeft = value; RequestPaint(); } }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public float EndTop { get => _endTop; set { _endTop = value; RequestPaint(); } }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool CanRender { get => _canRender; set { _canRender = value; RequestPaint(); } }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool IsVirtual { get => _isVirtual; set { _isVirtual = value; RequestPaint(); } }

    /// <summary>
    /// Whether the host considers this link highlighted. The canvas sets it from the hub's
    /// <see cref="LinkInteraction.HoverChanged"/>; nothing sets it by default.
    /// </summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool IsHighlighted
    {
        get => _isHighlighted;
        set
        {
            if (_isHighlighted == value) return;
            _isHighlighted = value;
            RequestPaint();
        }
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color LineColor
    {
        get => _lineColor;
        set
        {
            _lineColor = value;
            RequestPaint();
        }
    }

    /// <summary>
    /// Optional callback invoked instead of <see cref="Control.Invalidate"/> when link
    /// geometry or visibility changes. A surface that renders this link inside its own
    /// <c>OnPaint</c> (rather than as a child control) sets this to invalidate the host
    /// canvas, keeping the connection live while dragging.
    /// </summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Action? ExternalInvalidate { get; set; }

    private void RequestPaint()
    {
        RefreshGeometry();
        if (ExternalInvalidate is { } external) external();
        else Invalidate();
    }

    /// <summary>Wires a link model so anchor/visibility changes repaint this view.</summary>
    public void Bind(IWorkflowLinkViewModel? link)
    {
        if (ReferenceEquals(_link, link))
        {
            Sync(link);
            return;
        }

        UnsubscribeEndpoints();

        if (_notifier is not null)
        {
            _notifier.PropertyChanged -= OnLinkChanged;
            _notifier = null;
        }

        // 换绑另一条线：旧连线不能再指着这个渲染器，否则它会把一条已经不由这里画的线报成可命中。
        _link?.PublishCurve(null);

        _link = link;
        Tag = link;

        if (link is INotifyPropertyChanged n)
        {
            _notifier = n;
            n.PropertyChanged += OnLinkChanged;
        }

        SubscribeEndpoints();
        Sync(link);
    }

    private void SubscribeEndpoints()
    {
        if (_link?.Sender is INotifyPropertyChanged s)
        {
            _senderNotifier = s;
            s.PropertyChanged += OnEndpointChanged;
        }

        if (_link?.Receiver is INotifyPropertyChanged r)
        {
            _receiverNotifier = r;
            r.PropertyChanged += OnEndpointChanged;
        }
    }

    private void UnsubscribeEndpoints()
    {
        if (_senderNotifier is not null)
        {
            _senderNotifier.PropertyChanged -= OnEndpointChanged;
            _senderNotifier = null;
        }

        if (_receiverNotifier is not null)
        {
            _receiverNotifier.PropertyChanged -= OnEndpointChanged;
            _receiverNotifier = null;
        }
    }

    private void OnEndpointChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new PropertyChangedEventHandler(OnEndpointChanged), sender, e);
            return;
        }

        SyncEndpoints();
    }

    /// <summary>
    /// View-model accessor honored by <see cref="ViewManager"/> when a pooled
    /// view is recycled. Setting it re-binds this view to the new link.
    /// </summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public IWorkflowLinkViewModel? ViewModel
    {
        get => _link;
        set => Bind(value);
    }

    private void OnLinkChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new PropertyChangedEventHandler(OnLinkChanged), sender, e);
            return;
        }

        if (e.PropertyName is nameof(IWorkflowLinkViewModel.IsVisible) or null or "")
        {
            CanRender = _link?.IsVisible == true;
        }

        if (e.PropertyName is nameof(IWorkflowLinkViewModel.Sender)
            or nameof(IWorkflowLinkViewModel.Receiver)
            or null or "")
        {
            SyncEndpoints();
        }
    }

    private void SyncEndpoints()
    {
        if (_link is null) return;

        var sender = _link.Sender;
        var receiver = _link.Receiver;
        if (sender is not null)
        {
            StartLeft = (float)sender.Anchor.Horizontal;
            StartTop = (float)sender.Anchor.Vertical;
        }

        if (receiver is not null)
        {
            EndLeft = (float)receiver.Anchor.Horizontal;
            EndTop = (float)receiver.Anchor.Vertical;
        }

        IsVirtual = IsVirtualLink(_link);

        // 唯一重建弧长表之处：画布写回的锚点都经此（四个锚点 setter 只在这里与 Bind 里被写）
        RefreshGeometry();
        RequestPaint();
    }

    public void Sync(IWorkflowLinkViewModel? link)
    {
        if (link is null) return;

        CanRender = link.IsVisible;
        SyncEndpoints();
    }

    private bool IsVirtualLink(IWorkflowLinkViewModel link)
        => link.Sender?.Parent is null && link.Receiver?.Parent is null;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // 渲染器要走了：撤掉它发布的曲线，免得一条已经不画的线继续可命中。
            _link?.PublishCurve(null);

            UnsubscribeEndpoints();
            if (_notifier is not null)
            {
                _notifier.PropertyChanged -= OnLinkChanged;
                _notifier = null;
            }
        }

        base.Dispose(disposing);
    }

    // ── Flow effect ──────────────────────────────────────────────────────────────

    /// <summary>
    /// The comet's place and brightness for the frame that is about to be drawn: how far along the link its head
    /// has travelled (0 at the sender's anchor, 1 at the receiver's) and how lit it is. Pushed in by
    /// <c>WorkflowCanvas</c> before each <see cref="Render"/>, from the two numbers its clock writes.
    /// </summary>
    /// <remarks>
    /// Two numbers, and the same pair for every link on the surface: the tail is a fraction of each link's own
    /// arc length and both of its colours are mixed from that link's own <see cref="LineColor"/>, so nothing per
    /// link has to cross this boundary. That is what lets one clock light every link in its own hue. The write is
    /// also the frame — a renderer with no window cannot ask for a repaint, so the canvas does it when it writes.
    /// </remarks>
    public void SetFlow(double head, double intensity)
    {
        _bandHead = head;
        _bandIntensity = intensity;
    }

    /// <summary>
    /// Draws the link onto an arbitrary <see cref="Graphics"/> surface. In OnPaint the canvas first writes back
    /// all slot anchors, then calls this method uniformly in world coordinates after
    /// <c>TranslateTransform(origin)</c> — links are not separate transparent overlay child controls, which
    /// avoids the WinForms issue where overlapping full-size sibling windows are clipped by WS_CLIPSIBLINGS
    /// (only the topmost is drawn) and links become invisible. When used as a standalone control, OnPaint takes
    /// the same path.
    /// </summary>
    /// <param name="g">The surface to draw on, already translated to world coordinates.</param>
    public void Render(Graphics g)
    {
        if (!_canRender) return;

        // NaN gate (mirrors WorkflowLinkRenderEx.IsRenderReady in the XAML adapters): slot
        // anchors default to NaN until the canvas measures them, so a real link must not paint
        // a stale frame at NaN/origin before measurement lands. Placeholder endpoints (Parent
        // is null, e.g. the VirtualLink gesture) are exempt and render immediately.
        if (_link is not null && !WorkflowSlotUpdateGate.IsLinkRenderReady(_link)) return;
        var curve = _curve;
        if (curve is null || curve.Length <= 0) return;
        var length = curve.Length;

        g.SmoothingMode = SmoothingMode.AntiAlias;

        var color = _isHighlighted ? HighlightColor : _lineColor;
        var thickness = _isHighlighted ? HighlightThickness : LineThickness;

        // 管壁：两层更宽的同色低透明描边垫在下面，整条线因此像在发光而不是贴在背景上。圆头圆角，
        // 两端才不像被截断的横截面
        DrawSegment(g, 0, length, Fade(color, 0.10), thickness + 9);
        DrawSegment(g, 0, length, Fade(color, 0.16), thickness + 4);

        // 虚拟连线是指针下的橡皮筋：虚线、不流动
        if (_isVirtual)
        {
            using var pen = new Pen(Fade(color, 0.75), thickness) { DashStyle = DashStyle.Custom, DashPattern = [4f, 2f] };
            using var path = BuildPath(0, length);
            g.DrawPath(pen, path);
            return;
        }

        // 线体本身是静息的：光不在时它只是一根暗线，有了对比彗星才亮得出来；选中时抬透明度，线更实
        DrawSegment(g, 0, length, Fade(color, _isHighlighted ? 0.85 : 0.55), thickness);

        if (_bandIntensity > 0.001)
        {
            DrawComet(g, color, thickness);
        }
    }

    // 彗星：沿弧长切出 [头-尾, 头] 这一段，分若干小段画，每段给一个递减的透明度与变化的颜色。
    // 不用渐变刷是因为它的轴是两端之间的直线，在曲线上会把光打偏（见类注释）。
    private void DrawComet(Graphics g, Color color, float thickness)
    {
        var curve = _curve!;
        double head = Math.Clamp(_bandHead, 0, 1) * curve.Length;
        double tail = TailFraction * curve.Length;

        // 两遍：先光晕（更宽更淡）再本体，两遍都跟着头走，所以动感在光晕上也读得出来
        for (int pass = 0; pass < 2; pass++)
        {
            bool bloom = pass == 0;

            for (int k = 0; k < TailSegments; k++)
            {
                double f0 = k / (double)TailSegments;      // 0 = 尾梢，1 = 头
                double f1 = (k + 1) / (double)TailSegments;

                double l0 = head - (tail * (1 - f0));
                double l1 = head - (tail * (1 - f1));
                if (l1 <= 0 || l0 >= curve.Length) continue;

                // 平方衰减：让透明集中在尾段，读起来才像拖尾而不是一条均匀的带
                double a = _bandIntensity * f0 * f0;
                if (a <= 0.004) continue;

                // 尾梢是本体的颜色，越靠近头越白 —— 白热只发生在头部
                var c = Mix(color, Color.White, f0);

                DrawSegment(
                    g, l0, l1,
                    Fade(c, bloom ? a * 0.22 : a),
                    bloom ? thickness + 9 : (float)(thickness * (0.45 + (0.95 * f0))),
                    roundCap: true);
            }
        }
    }

    // 取 [from, to] 这一段弧长上的几何并描边。两端各自插值到精确位置，中间用现成采样点
    private void DrawSegment(Graphics g, double from, double to, Color color, float thickness, bool roundCap = false)
    {
        using var path = BuildPath(from, to);
        using var pen = new Pen(color, thickness);
        if (roundCap) pen.StartCap = pen.EndCap = LineCap.Round;
        g.DrawPath(pen, path);
    }

    private GraphicsPath BuildPath(double from, double to)
    {
        var path = new GraphicsPath();
        var curve = _curve;
        if (curve is null || curve.Length <= 0) return path;

        to = Math.Min(to, curve.Length);
        from = Math.Clamp(from, 0, curve.Length);

        var points = new List<PointF>(curve.Count + 2) { PointAtLength(from) };
        for (int i = 0; i < curve.Count; i++)
        {
            double l = curve.LengthAt(i);
            if (l <= from || l >= to) continue;
            points.Add(new PointF((float)curve.XAt(i), (float)curve.YAt(i)));
        }

        points.Add(PointAtLength(to));
        path.AddLines(points.ToArray());
        return path;
    }

    // ── Hit testing ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Whether <paramref name="worldPoint"/> lies within <paramref name="radius"/> of the curve, measured against
    /// the very geometry <see cref="Render"/> strokes — so the reachable strip bends with the curve instead of
    /// following the straight line between the two ends.
    /// </summary>
    /// <remarks>
    /// The point is in the world coordinates <see cref="Render"/> is given: a host that draws this view inside its
    /// own <c>OnPaint</c> produces one by subtracting its own pan and scroll from a mouse position.
    /// </remarks>
    public bool HitTest(PointF worldPoint, float radius)
    {
        // 虚拟连线是指针下的橡皮筋，永远贴在指针上；量不到端点的线没有几何可命中
        if (!_canRender || _isVirtual || _curve is not { Length: > 0 }) return false;
        return _curve.Contains(worldPoint.X, worldPoint.Y, radius);
    }

    // ── Geometry ─────────────────────────────────────────────────────────────────

    // 端点变化时重建这条线的扁平化几何，并提交给 Core（LinkHelper 持有它做命中）。
    // 画与命中共用同一条曲线 —— 本视图不再各自推一遍采样点与弧长。
    private void RefreshGeometry()
    {
        if (!float.IsFinite(_startLeft) || !float.IsFinite(_startTop)
            || !float.IsFinite(_endLeft) || !float.IsFinite(_endTop))
        {
            _curve = null;
            _link?.PublishCurve(null);
            return;
        }

        _curve = LinkCurve.BuildLinkCubic(_link, _startLeft, _startTop, _endLeft, _endTop, PullMinimum);
        _link?.PublishCurve(_curve, this);
    }

    // 弧长 → 点：取点归曲线（内部二分 + 段内插值，精确到亚像素），这里只转换坐标类型。
    private PointF PointAtLength(double len)
    {
        var curve = _curve;
        if (curve is null || curve.Length <= 0) return new PointF(_startLeft, _startTop);

        var (x, y) = curve.PointAtLength(len);
        return new PointF((float)x, (float)y);
    }

    // 两色之间线性混合（含 alpha），用于尾梢到头部的那一段
    private static Color Mix(Color from, Color to, double t)
    {
        byte L(byte a, byte b) => (byte)Math.Round(a + ((b - a) * t));

        return Color.FromArgb(L(from.A, to.A), L(from.R, to.R), L(from.G, to.G), L(from.B, to.B));
    }

    /// <summary>Same hue, given opacity. GDI+ colours carry alpha as a byte, the design's values are 0..1.</summary>
    private static Color Fade(Color color, double opacity)
        => Color.FromArgb((int)Math.Round(Math.Clamp(opacity, 0, 1) * 255), color.R, color.G, color.B);

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Render(e.Graphics);
    }
}
