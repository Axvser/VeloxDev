// VeloxDev customization: Customize line geometry, color, and thickness here.
using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;
using VeloxDev.WorkflowSystem;
// `Size` collides between System.Drawing and VeloxDev.WorkflowSystem; a drawing
// alias keeps `new Size(...)` and `RectangleF(...)` usage unambiguous.
using Size = System.Drawing.Size;

namespace Demo.Views;

/// <summary>
/// Orthogonal (polyline) connection with golden-ratio stubs, carrying a travelling highlight so the
/// direction of data flow is readable at a glance.
/// Passive visual only — no hover, highlight, or keyboard interaction.
/// </summary>
public sealed class LinkView : Control
{
    private const double Phi = 0.6180339887;

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
    private Color _lineColor = ParseColor("#DDFFFFFF");

    // 本帧光带的位置与亮度混合：每次 Render 前由 surface 的时钟推入（SetFlow）——时钟归表面，渲染器没有窗口
    private double _bandCentre;
    private double _bandMix;

    // 该链接自己的渐变：轴是它的两端点、停靠点是光带
    // GDI+ 的渐变端点只能在构造时给、之后改不了——链接移动才重建刷子，光带移动不重建（MoveBand）
    private LinearGradientBrush? _flowBrush;

    // 那个刷子的混合存在这里而不是从它取：InterpolationColors 的 getter 返回副本，写进副本等于没写（实测）
    // 五档而非光带的三档，因为 GDI+ 只认铺满整条轴的混合，多出的两档两端再放一遍静息色
    private readonly ColorBlend _band = new(5);

    // 光带混合用的两个颜色，取自本链接自己的颜色——按链接而非按表面，两条链接未必同色
    private Color _lit;
    private Color _dim;

    public LinkView()
    {
        // Passive overlay: no hit-testing, sits behind the nodes. The link is
        // rendered by the host canvas (Render) rather than as a child window, and
        // uses an opaque background so it never participates in WinForms' fragile
        // transparent compositing.
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

        // 先种下光带混合的两个颜色：渐变要等链接有轴才建，箭头却第一帧就用亮色；手工 new 的视图不会被推帧数
        UpdateFlowBrush();
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

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color LineColor
    {
        get => _lineColor;
        set
        {
            _lineColor = value;
            // 光带由这个颜色混出，故刷子随之重建：轴与两个颜色都由刷子持有，GDI+ 造好后都改不了
            UpdateFlowBrush();
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

        // 唯一重新指向渐变之处：画布写回的锚点都经此（四个锚点 setter 只在这里与 Bind 里被写）
        UpdateFlowBrush();
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
            UnsubscribeEndpoints();
            if (_notifier is not null)
            {
                _notifier.PropertyChanged -= OnLinkChanged;
                _notifier = null;
            }

            // GDI+ 画刷不会被自行回收，而拖节点时每写一次锚点就重建一个
            _flowBrush?.Dispose();
            _flowBrush = null;
        }

        base.Dispose(disposing);
    }

    // ── Flow effect ──────────────────────────────────────────────────────────────

    // 光带半宽（渐变偏移单位）
    private const double HalfWidth = 0.04;

    /// <summary>
    /// The band's place for the frame that is about to be drawn: where its middle stop sits along the link,
    /// and how much of its colour is the lit one. Pushed in by <c>WorkflowCanvas</c> before each
    /// <see cref="Render"/>, from the two numbers its clock writes.
    /// </summary>
    /// <remarks>
    /// Two numbers rather than the four the reference animates (its band's three stop offsets and its
    /// colour), because these are the two the other two are derived from and the same pair serves every
    /// renderer on the surface. The band's shoulders are <see cref="HalfWidth"/> either side of the centre
    /// and its colour is the mix of this link's own pair (see <see cref="MoveBand"/>), so nothing per link
    /// has to cross this boundary — which is what lets one pair of values be handed to every link, including
    /// links of different colours.
    /// </remarks>
    public void SetFlow(double centre, double mix)
    {
        _bandCentre = centre;
        _bandMix = mix;
    }

    // 沿链接指向渐变、给光带配色——按链接而非按帧的那部分；这里不写光带位置，它归时钟，由 SetFlow 每帧推入
    // 刷子重建而非重指（GDI+ 端点只能在构造时给），跨帧保留的只有 ColorBlend，锚点写回都经 SyncEndpoints
    private void UpdateFlowBrush()
    {
        _lit = LitOf(_lineColor);
        _dim = DimOf(_lit);

        _flowBrush?.Dispose();
        _flowBrush = null;

        var from = new PointF(_startLeft, _startTop);
        var to = new PointF(_endLeft, _endTop);

        // 两个锚点都测出长度才画链接：之前是 NaN（GDI+ 直接拒绝而非不画），手工建的视图则是原点两次
        // 两点重合的渐变没有轴可让光带走，用平色笔最诚实——这两个检查是唯一会让链接没有刷子的情况
        if (!IsFinite(from) || !IsFinite(to)) return;
        if (Math.Abs(to.X - from.X) < 0.5f && Math.Abs(to.Y - from.Y) < 0.5f) return;

        // 环绕模式留给刷子默认的 Tile：GDI+ 给不了 Pad，最接近的 WrapMode.Clamp 会抛 ArgumentException（实测）
        // 也无妨：描边从不采样到轴的两端之外，且首末停靠点同为静息色（见 MoveBand），越界采样无从不同
        _flowBrush = new LinearGradientBrush(from, to, _dim, _dim);

        MoveBand();
    }

    // 把光带放在时钟上次放的位置（SetFlow 收到的两个数），并交回它被放进去的刷子——Render 要的两样东西
    // 五档而非三档：GDI+ 只认铺满整条轴的混合（0.25/0.50/0.75 被拒，实测），多出两档两端补静息色
    // 笔必须每帧重建，因为一支笔会留住造它时刷子的停靠点（否则光带被钉在上一帧位置）
    private LinearGradientBrush MoveBand()
    {
        var brush = _flowBrush!;

        // 两个肩不动画：时钟只写中间停靠点，肩在其两侧 HalfWidth 处，光带才不至于糊满整条链接
        var trailing = _bandCentre - HalfWidth;
        var leading = _bandCentre + HalfWidth;

        _band.Colors![0] = _dim;
        _band.Colors[1] = _dim;
        _band.Colors[2] = Blend(_dim, _lit, _bandMix);
        _band.Colors[3] = _dim;
        _band.Colors[4] = _dim;
        _band.Positions![0] = 0f;
        _band.Positions[1] = (float)trailing;
        _band.Positions[2] = (float)_bandCentre;
        _band.Positions[3] = (float)leading;
        _band.Positions[4] = 1f;

        brush.InterpolationColors = _band;
        return brush;
    }

    // 两色线性混合（含 alpha）：mix 为 0 停在链接本色，为 1 全亮
    private static Color Blend(Color from, Color to, double t) => Color.FromArgb(
        (byte)Math.Round(from.A + (to.A - from.A) * t),
        (byte)Math.Round(from.R + (to.R - from.R) * t),
        (byte)Math.Round(from.G + (to.G - from.G) * t),
        (byte)Math.Round(from.B + (to.B - from.B) * t));

    private static bool IsFinite(PointF p) => float.IsFinite(p.X) && float.IsFinite(p.Y);

    // 亮色：链接本色各通道向白抬 45% 并置全不透明（本 demo 的链接是 87% 的白，这一步同时去掉半透）
    private static Color LitOf(Color color)
    {
        const double lift = 0.45;

        byte Up(byte channel) => (byte)Math.Round(channel + (255 - channel) * lift);

        return Color.FromArgb(255, Up(color.R), Up(color.G), Up(color.B));
    }

    // 静息色：亮色按 alpha 变暗到约 62%，靠它取反差而色相不变
    // 往白里提不行——青线（Avalonia）与白线（本 demo）上都几乎看不出，实测过
    private static Color DimOf(Color color) => Color.FromArgb(
        (byte)Math.Round(color.A * 0.62), color.R, color.G, color.B);

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Render(e.Graphics);
    }

    /// <summary>
    /// Draws the link geometry onto an arbitrary <see cref="Graphics"/> surface. In OnPaint the
    /// canvas first writes back all slot anchors, then calls this method uniformly in world
    /// coordinates after TranslateTransform(origin) — links are no longer separate transparent
    /// overlay child controls, avoiding the WinForms issue where overlapping full-size sibling
    /// windows are clipped by WS_CLIPSIBLINGS (only the topmost is drawn) and links become
    /// invisible. When used as a standalone control, OnPaint takes the same path.
    /// </summary>
    public void Render(Graphics g)
    {
        if (!_canRender) return;

        // NaN gate (mirrors WorkflowLinkRenderEx.IsRenderReady in the XAML adapters): slot
        // anchors default to NaN until the canvas measures them, so a real link must not paint
        // a stale frame at NaN/origin before measurement lands. Placeholder endpoints (Parent
        // is null, e.g. the VirtualLink gesture) are exempt and render immediately.
        if (_link is not null && !WorkflowSlotUpdateGate.IsLinkRenderReady(_link)) return;

        g.SmoothingMode = SmoothingMode.AntiAlias;

        var points = BuildPoints();
        if (points.Length < 2) return;

        // 行进高亮只在成形的连线上有意义：虚拟链接是指针下的橡皮筋，无渐变的链接是轴还没到，两者都用平色笔
        // 后一根检查有实义——MoveBand 要往刷子里写；笔每帧自刚写完光带的刷子新建（见 MoveBand），且不拥有它
        using var pen = _isVirtual || _flowBrush is null
            ? new Pen(_lineColor, float.Parse("2", CultureInfo.InvariantCulture))
            : new Pen(MoveBand(), float.Parse("2", CultureInfo.InvariantCulture));
        if (_isVirtual)
        {
            pen.DashStyle = DashStyle.Dash;
            pen.DashPattern = [4f, 2f];
        }

        g.DrawLines(pen, points);

        if (!_isVirtual)
        {
            DrawArrowhead(g, points[^2], points[^1]);
        }
    }

    private PointF[] BuildPoints()
    {
        var s = new PointF(_startLeft, _startTop);
        var e = new PointF(_endLeft, _endTop);
        double dx = _endLeft - _startLeft;
        double stub = dx / 2.0 * (1.0 - Phi);
        var p1 = new PointF(s.X + (float)stub, s.Y);
        var p4 = new PointF(e.X - (float)stub, e.Y);
        return [s, p1, p4, e];
    }

    private void DrawArrowhead(Graphics g, PointF from, PointF tip)
    {
        float tx = tip.X - from.X;
        float ty = tip.Y - from.Y;
        float len = (float)Math.Sqrt(tx * tx + ty * ty);
        if (len < 0.03f) return;

        // Unit vector along the last segment, plus its perpendicular.
        float ux = tx / len;
        float uy = ty / len;
        const float al = 12f, aw = 8f;
        float bx = tip.X - ux * al;
        float by = tip.Y - uy * al;
        float px = -uy, py = ux;

        var pts = new[]
        {
            tip,
            new PointF(bx + px * (aw / 2f), by + py * (aw / 2f)),
            new PointF(bx - px * (aw / 2f), by - py * (aw / 2f)),
        };

        // 箭头是终点标记，用光带的颜色而不是渐变：线体静息为暗，箭头若一起暗就成了唯一不亮的部分
        using var brush = new SolidBrush(_lit);
        g.FillPolygon(brush, pts);
    }

    private static Color ParseColor(string hex)
    {
        var value = hex.Trim();
        if (value.StartsWith("#", StringComparison.Ordinal))
        {
            var digits = value.Substring(1);
            if (digits.Length == 8)
            {
                return Color.FromArgb(
                    byte.Parse(digits.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                    byte.Parse(digits.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                    byte.Parse(digits.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                    byte.Parse(digits.Substring(6, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
            }

            if (digits.Length == 6)
            {
                return Color.FromArgb(
                    byte.Parse(digits.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                    byte.Parse(digits.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                    byte.Parse(digits.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
            }
        }

        return Color.FromName(value);
    }
}
