using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

/// <summary>
/// A cubic Bézier connection that leaves each port horizontally.
/// </summary>
/// <remarks>
/// <para>
/// Materialized and recycled by <see cref="ViewPool"/> — one view per visible link, the drag preview included —
/// and paints itself. It is a passive visual: it takes no pointer or keyboard input of its own. The interaction
/// itself is driven by <see cref="WorkflowTreeView"/>, which publishes a <see cref="LinkCurve"/> for every drawn
/// link (through <see cref="LinkHitTestEx.PublishCurve"/>, using this control as the curve's visual); the Core
/// interaction hub (<see cref="VeloxDev.WorkflowSystem.LinkInteraction"/>) turns <see cref="IsHighlighted"/> on
/// for the one the pointer is over, through <see cref="ILinkHighlight"/>.
/// </para>
/// <para>
/// A WinForms child window is opaque and cannot composite over its siblings, so the window region is carved to the
/// stroke band of the curve instead of a bounding box: the grid behind stays visible around the line, and only
/// the line's own area can ever cover the canvas. Keep <see cref="SurfaceBackground"/> equal to the surface's grid
/// background, or the carved band shows up as a seam.
/// </para>
/// <para>
/// The view belongs behind the node cards. Re-ordering pooled views is the surface's job — see
/// <see cref="WorkflowTreeView"/>'s link-layer arrangement.
/// </para>
/// </remarks>
public class WorkflowLinkView : Control, ILinkHighlight
{
    /// <summary>Extra width the region gets on each side of the stroke, so the antialiased edge is not clipped.</summary>
    private const float RegionPad = 1.5f;

    private IWorkflowLinkViewModel? _link;
    private readonly ModelChangeRelay _relay;

    // 选中色的缺省值：柔和的淡青高光（与其它家的 hub 默认同色），不是橙红 —— 悬停的反馈该是一层光，
    // 不是报警。保留可改写。
    private static readonly Color DefaultHighlightColor = Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF);

    private bool _canRender = true;
    private bool _isVirtual;
    private bool _isHighlighted;
    private Color _lineColor = Color.FromArgb(0xDD, 0xFF, 0xFF, 0xFF);
    private Color _highlightColor = DefaultHighlightColor;
    private float _thickness = 1.5f;
    private float _pullMinimum = 40f;
    private Color _surfaceBackground = Color.FromArgb(0x1E, 0x1E, 0x1E);

    // 当前帧：窗口局部坐标下的四个曲线控制点（起点、两个控制点、终点），以及窗口被它平移过的原点。
    // 没东西可画时为 null。
    private PointF[]? _windowCurve;
    private Region? _windowRegion;

    /// <summary>Creates the link view.</summary>
    public WorkflowLinkView()
    {
        _relay = new ModelChangeRelay(this, OnModelChanged);

        // 不透明填充，且与窗口被雕出来的那片网格同色：窗口正好盖住描边带，所以这个填充在画布上必须看不出来。
        BackColor = _surfaceBackground;
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.UserPaint,
            true);
        // 关掉：描边带绝不能吞掉本该发给下面画布（平移）或发给连线手势的鼠标输入。
        TabStop = false;
        Enabled = false;
        ApplyRegion(null);
    }

    /// <summary>The stroke colour.</summary>
    public Color LineColor
    {
        get => _lineColor;
        set { _lineColor = value; Invalidate(); }
    }

    /// <summary>The stroke width, in pixels.</summary>
    /// <remarks>Changing it re-carves the window region on the next geometry rebuild.</remarks>
    public float Thickness
    {
        get => _thickness;
        set
        {
            if (_thickness == value || value <= 0) return;
            _thickness = value;
            RebuildGeometry();
        }
    }

    /// <summary>
    /// The least horizontal distance, in pixels, each control point is pulled away from its own endpoint — what
    /// makes the curve leave both ports horizontally.
    /// </summary>
    /// <remarks>
    /// The pull actually used is the larger of this and half the horizontal gap between the endpoints, so two
    /// ports close together do not degenerate the curve into a straight segment. Changing this re-carves the
    /// window region on the next geometry rebuild.
    /// </remarks>
    public float PullMinimum
    {
        get => _pullMinimum;
        set
        {
            if (_pullMinimum == value || value < 0) return;
            _pullMinimum = value;
            RebuildGeometry();
        }
    }

    /// <summary>The colour behind the link — the surface's grid background.</summary>
    public Color SurfaceBackground
    {
        get => _surfaceBackground;
        set
        {
            if (_surfaceBackground == value) return;
            _surfaceBackground = value;
            BackColor = value;
            Invalidate();
        }
    }

    /// <inheritdoc />
    public bool IsHighlighted
    {
        get => _isHighlighted;
        set
        {
            if (_isHighlighted == value) return;
            _isHighlighted = value;
            Invalidate();
        }
    }

    /// <summary>Colour a highlighted link is drawn in.</summary>
    public Color HighlightColor
    {
        get => _highlightColor;
        set
        {
            if (_highlightColor == value) return;
            _highlightColor = value;
            Invalidate();
        }
    }

    /// <summary>
    /// View-model accessor honored by <see cref="ViewManager"/> when a pooled view is recycled. Setting it
    /// re-binds this view to the new link.
    /// </summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public IWorkflowLinkViewModel? ViewModel
    {
        get => _link;
        set => Bind(value);
    }

    /// <summary>Wires a link model so anchor and visibility changes repaint this view.</summary>
    /// <param name="link">The link, or <see langword="null"/> to detach.</param>
    public void Bind(IWorkflowLinkViewModel? link)
    {
        if (ReferenceEquals(_link, link))
        {
            Sync(link);
            return;
        }

        // 池把这个视图回收给另一条线：旧连线不能再指着这个控件，否则它会把一条已经不由这里画的线报成可命中
        RetractCurve();

        _link = link;
        Tag = link;

        // 连线自己，加上它两端 —— 端点的锚点变了，几何就得重算。
        _relay.Set(link as INotifyPropertyChanged);
        _relay.Add(link?.Sender as INotifyPropertyChanged);
        _relay.Add(link?.Receiver as INotifyPropertyChanged);

        Sync(link);
    }

    /// <summary>Re-reads this view's link and re-carves the window. Called on bind and on recycle.</summary>
    /// <param name="link">The link.</param>
    public void Sync(IWorkflowLinkViewModel? link)
    {
        if (link is null) return;

        _canRender = link.IsVisible;
        RebuildGeometry();
    }

    // 连线自己或它任一端动了：可见性可能变了，几何也要重算。重算前重读一次可见性 —— 端点的变更不影响它，
    // 但读一次是幂等的，比按属性名分叉更不容易漏。
    private void OnModelChanged(PropertyChangedEventArgs e)
    {
        _canRender = _link?.IsVisible == true;
        RebuildGeometry();
    }

    // 按当前端点重建窗口盒与区域。端点是画布局部坐标（插槽布局按每个插槽控件的屏幕位置写进去的），所以
    // 盒子直接用，不加节点视图自己加的那份平移。
    private void RebuildGeometry()
    {
        var link = _link;
        // NaN 门：插槽锚点在画布测量之前是 NaN，真实连线要等就绪；虚拟连线占位（Parent 为 null）豁免。
        if (link is null || !_canRender || !WorkflowSlotUpdateGate.IsLinkRenderReady(link))
        {
            _windowCurve = null;
            RetractCurve();
            ApplyRegion(null);
            return;
        }

        var sender = link.Sender;
        var receiver = link.Receiver;
        if (sender is null || receiver is null)
        {
            _windowCurve = null;
            RetractCurve();
            ApplyRegion(null);
            return;
        }

        // 每次都重算，绝不用缓存值：一个从虚拟（手势）连线回收来的池化视图，接着画真实连线时不能还画着虚线。
        _isVirtual = sender.Parent is null && receiver.Parent is null;

        var points = BuildCurve(sender.Anchor, receiver.Anchor);
        if (!IsDrawable(points))
        {
            _windowCurve = null;
            RetractCurve();
            ApplyRegion(null);
            return;
        }

        // 把这条线画出来的几何提交给 Core：命中判定与绘制从此共用同一条曲线，不再各推一遍。
        // 坐标是画布局部坐标（slot.Anchor 的空间），也是表面指针事件所在的空间 —— 两边必须同系。
        link.PublishCurve(
            LinkCurve.BuildCubic(
                points[0].X, points[0].Y, points[3].X, points[3].Y, _pullMinimum),
            this);

        using var strokePen = new Pen(Color.Black, _thickness + 2 * RegionPad) { LineJoin = LineJoin.Miter };
        using var strokePath = new GraphicsPath();
        strokePath.AddBezier(points[0], points[1], points[2], points[3]);
        strokePath.Widen(strokePen);

        var bounds = strokePath.GetBounds();
        var originX = (float)Math.Floor(bounds.Left);
        var originY = (float)Math.Floor(bounds.Top);
        using (var shift = new Matrix(1f, 0f, 0f, 1f, -originX, -originY))
        {
            strokePath.Transform(shift);
        }

        // 落在整像素上，好让画出来的线保持它原来在画布局部的那条路径；小数余量留在点的坐标里。
        Location = new Point((int)originX, (int)originY);
        Size = new System.Drawing.Size(
            Math.Max(1, (int)Math.Ceiling(bounds.Right) - (int)originX + 1),
            Math.Max(1, (int)Math.Ceiling(bounds.Bottom) - (int)originY + 1));

        var local = new PointF[points.Length];
        for (var i = 0; i < points.Length; i++)
        {
            local[i] = new PointF(points[i].X - originX, points[i].Y - originY);
        }

        _windowCurve = local;
        ApplyRegion(strokePath);
        Invalidate();
    }

    // 撤销这条线发布的曲线：画不出来时它不该再回答命中。曲线撤掉，LinkHelper 里那个 Visual 也跟着清。
    private void RetractCurve() => _link?.PublishCurve(null);

    // 把雕好的形状交给窗口。WinForms 会把区域拷贝进窗口，所以上一个托管 Region 归我们处置。
    private void ApplyRegion(GraphicsPath? strokePath)
    {
        var next = strokePath is null ? new Region() : new Region(strokePath);
        var previous = _windowRegion;
        _windowRegion = next;
        Region = next;
        previous?.Dispose();
    }

    // GDI+ 拒绝加宽一条它描不出来的路径：端点落在同一个像素上（连线手势的第一帧）、或者锚点还没测量（NaN）。
    // 不用 float.IsFinite / `^1` 索引 —— 本包要能编到 netframework4.6.1。
    private static bool IsDrawable(PointF[] points)
    {
        foreach (var point in points)
        {
            if (float.IsNaN(point.X) || float.IsNaN(point.Y)
                || float.IsInfinity(point.X) || float.IsInfinity(point.Y))
            {
                return false;
            }
        }

        var last = points[points.Length - 1];
        return Math.Abs(points[0].X - last.X) >= 0.5f || Math.Abs(points[0].Y - last.Y) >= 0.5f;
    }

    // 起点、两个控制点、终点 —— AddBezier 要的那四个点。
    // 两个控制点各自水平拉开：连线因此从两端水平出线、中间平滑过渡，没有折角。
    private PointF[] BuildCurve(Anchor sender, Anchor receiver)
    {
        var s = new PointF((float)sender.Horizontal, (float)sender.Vertical);
        var e = new PointF((float)receiver.Horizontal, (float)receiver.Vertical);

        // 最小拉出量：两个端口靠得很近时，0.5·dx 会让曲线退化成一条直线段，失去「从端口水平出来」的形状。
        var pull = Math.Max(_pullMinimum, Math.Abs(e.X - s.X) * 0.5f);
        return [s, new PointF(s.X + pull, s.Y), new PointF(e.X - pull, e.Y), e];
    }

    /// <inheritdoc />
    protected override void OnPaint(PaintEventArgs e)
    {
        if (e is null) throw new ArgumentNullException(nameof(e));

        base.OnPaint(e);

        var points = _windowCurve;
        if (points is null || points.Length < 4) return;

        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        // 选中（悬停命中）那条换成高亮色并加粗 1.5，与其它六家同一种读法
        var color = _isHighlighted ? _highlightColor : _lineColor;
        var thickness = _isHighlighted ? _thickness + 1.5f : _thickness;

        using var pen = new Pen(color, thickness);
        if (_isVirtual)
        {
            pen.DashStyle = DashStyle.Dash;
            pen.DashPattern = [4f, 2f];
        }

        using var curve = new GraphicsPath();
        curve.AddBezier(points[0], points[1], points[2], points[3]);
        g.DrawPath(pen, curve);
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            RetractCurve();
            _relay.Clear();
            _windowRegion?.Dispose();
            _windowRegion = null;
        }

        base.Dispose(disposing);
    }

    /// <summary>Parses a <c>#RRGGBB</c>, <c>#AARRGGBB</c> or named colour.</summary>
    /// <param name="hex">The colour text.</param>
    /// <returns>The colour.</returns>
    protected static Color ParseColor(string hex) => WorkflowSurfaceColors.Parse(hex);
}
