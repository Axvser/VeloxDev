using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

/// <summary>
/// A thumbnail overview of a workflow surface, anchored to its host's top-right corner.
/// </summary>
/// <remarks>
/// <para>
/// Implements <see cref="IWorkflowMinimapOverlay"/> so <see cref="WorkflowSurfaceBehavior"/> and
/// <see cref="WorkflowTreeView"/> push the offsets it draws with. Dragging the viewport block requests a pan through
/// <see cref="IWorkflowMinimapScrollSource"/>, which the surface subscribes to.
/// </para>
/// <para>
/// Derive from it only to restyle it — the layout maths, the drag mapping and the anchoring are the same in every
/// host. Assign it to <see cref="WorkflowTreeView.MinimapOverlay"/> and the wiring is done.
/// </para>
/// </remarks>
public class WorkflowMinimapOverlay : Panel, IWorkflowMinimapOverlay, IWorkflowMinimapScrollSource
{
    /// <summary>Overlay margin from the surface's top-right corner, in pixels.</summary>
    public const int CornerMargin = 12;

    private Color _minimapBackground = Color.FromArgb(0xD2, 0x14, 0x19, 0x22);
    private Color _minimapBorderBrush = Color.FromArgb(0xDC, 0x94, 0xA3, 0xB8);
    private Color _nodeBrush = Color.FromArgb(0xDC, 0x38, 0xBD, 0xF8);
    private Color _viewportStroke = Color.FromArgb(0xF0, 0xFF, 0xFF, 0xFF);

    private bool _dragging;

    // 每趟绘制复用同一份缩略框，免掉一次列表分配（内容变了也无所谓 —— 下一趟绘制会重填）。
    private readonly List<(double X, double Y, double W, double H)> _nodeRects = [];

    /// <summary>Creates the overlay at its default size, anchored to the host's top-right corner.</summary>
    public WorkflowMinimapOverlay()
    {
        DoubleBuffered = true;
        Width = 200;
        Height = 140;
        Anchor = AnchorStyles.Top | AnchorStyles.Right;
        SetStyle(ControlStyles.ResizeRedraw, true);
        // 背景不透明：WinForms 没有可靠的透明合成，所以浮层擦成自己的底色，而不是让表面透过一个透明面板显出来。
        // alpha 强制 255 —— .NET 10 上给 BackColor 一个半透明值会抛（没有 SupportsTransparentBackColor 时
        // Control.set_BackColor 要求 A == 0xFF）。
        BackColor = Color.FromArgb(255, _minimapBackground);
    }

    /// <inheritdoc />
    public event Action<double, double>? ViewportScrollRequested;

    /// <summary>Panel background.</summary>
    public Color MinimapBackground
    {
        get => _minimapBackground;
        set
        {
            if (_minimapBackground == value) return;
            _minimapBackground = value;
            BackColor = Color.FromArgb(255, value);
            Invalidate();
        }
    }

    /// <summary>Border colour.</summary>
    public Color MinimapBorderBrush
    {
        get => _minimapBorderBrush;
        set
        {
            if (_minimapBorderBrush == value) return;
            _minimapBorderBrush = value;
            Invalidate();
        }
    }

    /// <summary>Fill for each node's thumbnail.</summary>
    public Color NodeBrush
    {
        get => _nodeBrush;
        set
        {
            if (_nodeBrush == value) return;
            _nodeBrush = value;
            Invalidate();
        }
    }

    /// <summary>Stroke for the draggable viewport block.</summary>
    public Color ViewportStroke
    {
        get => _viewportStroke;
        set
        {
            if (_viewportStroke == value) return;
            _viewportStroke = value;
            Invalidate();
        }
    }

    /// <inheritdoc />
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double ScrollOffsetX { get; set; }

    /// <inheritdoc />
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double ScrollOffsetY { get; set; }

    /// <inheritdoc />
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double ContentOffsetX { get; set; }

    /// <inheritdoc />
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double ContentOffsetY { get; set; }

    /// <summary>Always zero: the minimap reserves no ruler band of its own.</summary>
    public double RulerBand => 0;

    /// <inheritdoc />
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double ViewportWidth { get; set; } = 1;

    /// <inheritdoc />
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double ViewportHeight { get; set; } = 1;

    /// <inheritdoc />
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public IWorkflowTreeViewModel? WorkflowTree { get; set; }

    /// <inheritdoc />
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool IsMinimapVisible { get; set; } = true;

    /// <summary>Parses a <c>#RRGGBB</c>, <c>#AARRGGBB</c> or named colour.</summary>
    /// <param name="hex">The colour text.</param>
    /// <returns>The colour.</returns>
    protected static Color ParseColor(string hex) => WorkflowSurfaceColors.Parse(hex);

    /// <inheritdoc />
    protected override void OnParentChanged(EventArgs e)
    {
        base.OnParentChanged(e);
        if (Parent is not null)
        {
            Parent.Resize -= OnParentResized;
            Parent.Resize += OnParentResized;
        }

        PositionAtTopRight();
    }

    /// <inheritdoc />
    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        PositionAtTopRight();
    }

    private void OnParentResized(object? sender, EventArgs e) => PositionAtTopRight();

    // 贴到宿主右上角。宿主的尺寸要等它被加进去、排完版才知道，所以父控件每次 resize 都重算一次位置。
    private void PositionAtTopRight()
    {
        if (Parent is null) return;

        Location = new Point(
            Math.Max(0, Parent.ClientSize.Width - Width - CornerMargin),
            CornerMargin);
    }

    /// <inheritdoc />
    protected override void OnPaint(PaintEventArgs e)
    {
        if (e is null) throw new ArgumentNullException(nameof(e));

        base.OnPaint(e);

        if (!IsMinimapVisible) return;

        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = new RectangleF(0, 0, Width, Height);

        using var bgBrush = new SolidBrush(_minimapBackground);
        using var borderPen = new Pen(_minimapBorderBrush, 1f);
        g.FillRectangle(bgBrush, rect);
        g.DrawRectangle(borderPen, rect.X, rect.Y, rect.Width - 1, rect.Height - 1);

        var layout = ComputeLayout(_nodeRects);
        if (layout is null) return;
        var l = layout.Value;

        using var nodeBrush = new SolidBrush(_nodeBrush);
        using var viewportPen = new Pen(_viewportStroke, 1.5f);

        foreach (var (nx, ny, nw, nh) in _nodeRects)
        {
            double x = l.Ox + (nx - l.MinX) * l.Scale;
            double y = l.Oy + (ny - l.MinY) * l.Scale;
            double w = Math.Max(2, nw * l.Scale);
            double h = Math.Max(2, nh * l.Scale);
            g.FillRectangle(nodeBrush, (float)x, (float)y, (float)w, (float)h);
        }

        var vp = ViewportRect(l);
        g.DrawRectangle(viewportPen, (float)vp.X, (float)vp.Y, (float)vp.Width, (float)vp.Height);
    }

    /// <inheritdoc />
    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e is null) throw new ArgumentNullException(nameof(e));

        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;

        var layout = ComputeLayout();
        if (layout is null) return;

        // 与 Jalium 一致：按下的点直接成为视口中心 —— 不抓指示块，按哪儿就把视图移到哪儿。
        _dragging = true;
        Capture = true;
        UpdateViewportFromPointer(e.X, e.Y, layout.Value);
    }

    /// <inheritdoc />
    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (e is null) throw new ArgumentNullException(nameof(e));

        base.OnMouseMove(e);
        if (!_dragging) return;

        // 这里每次重算：本家不订阅节点，无从知道自上一趟绘制以来节点动没动，缓存会按旧位置反解。
        var layout = ComputeLayout();
        if (layout is null) return;
        UpdateViewportFromPointer(e.X, e.Y, layout.Value);
    }

    private void UpdateViewportFromPointer(int x, int y, MinimapLayout l)
    {
        // 指针就是目标视口中心；与 WPF/Avalonia/WinUI/MAUI/Razor 一致，按哪儿都重新居中。不做内容夹取：
        // 表面会自己长大，所以指示块可以被拖到小地图边缘、把表面平移到空白处。
        double cx = x;
        double cy = y;
        double sx = (cx - l.Ox) / l.Scale + l.MinX - ViewportWidth / 2 + ContentOffsetX;
        double sy = (cy - l.Oy) / l.Scale + l.MinY - ViewportHeight / 2 + ContentOffsetY;

        // 立刻反映目标，让指示块在第一次按下/拖动就动，而不是等画布那一趟往返（画布的同步稍后会确认它）。
        ScrollOffsetX = sx;
        ScrollOffsetY = sy;
        Invalidate();

        ViewportScrollRequested?.Invoke(sx, sy);
    }

    /// <inheritdoc />
    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (e is null) throw new ArgumentNullException(nameof(e));

        base.OnMouseUp(e);
        if (!_dragging) return;

        _dragging = false;
        Capture = false;
    }

    /// <inheritdoc />
    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        base.OnMouseCaptureChanged(e);
        // 捕获被抢走或在别处释放（如 alt-tab）：停止拖动，下次按下重新开始。
        if (!Capture)
        {
            _dragging = false;
        }
    }

    // 绘制与拖拽映射共用的一套「画布范围 + 缩放」。还没有节点时返回 null，小地图在树排好版之前保持空白。
    // 绘制要的缩略框顺手由 thumbnails 收走 —— 一趟把两样拿齐，别为包围盒单独再走一趟节点表。
    private MinimapLayout? ComputeLayout(List<(double X, double Y, double W, double H)>? thumbnails = null)
    {
        var tree = WorkflowTree;
        if (tree?.Nodes is null) return null;

        thumbnails?.Clear();

        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;
        var hasNode = false;

        foreach (var node in tree.Nodes)
        {
            var (x, y, w, h) = (node.Anchor.Horizontal, node.Anchor.Vertical, node.Size.Width, node.Size.Height);
            thumbnails?.Add((x, y, w, h));
            minX = Math.Min(minX, x);
            minY = Math.Min(minY, y);
            maxX = Math.Max(maxX, x + w);
            maxY = Math.Max(maxY, y + h);
            hasNode = true;
        }

        if (!hasNode) return null;

        // 小地图的画布只取节点内容（不与视口求并），与其余五家一致：内容按内容适配比缩放、在可绘制区里居中，
        // 视口块夹在小地图边缘。于是越过内容平移是让表面变长，而不是让小地图围着视口重新适配。
        const double pad = 4;
        double contentW = Math.Max(1, maxX - minX);
        double contentH = Math.Max(1, maxY - minY);
        double drawW = Width - pad * 2;
        double drawH = Height - pad * 2;
        double scale = Math.Min(drawW / contentW, drawH / contentH);
        // 居中的内容适配：ox/oy 是缩放后内容的左上角、居中于可绘制区 —— 与 WPF/Avalonia/WinUI/MAUI 的
        // ComputeTransform 和 Razor 的 Recompute 是同一个变换，所以拖拽/点击反解的是绘制用的同一个映射。
        double ox = pad + (drawW - contentW * scale) / 2;
        double oy = pad + (drawH - contentH * scale) / 2;
        return new MinimapLayout(minX, minY, scale, ox, oy);
    }

    private RectangleF ViewportRect(MinimapLayout l)
    {
        // 视口走与节点同一个居中内容适配变换，再把块夹在小地图内，使它永远不出界 —— 与其余五家一致。
        // 用户把它拖到边上时，请求的滚动量变大、表面平移到空白处。
        double vx = l.Ox + (ScrollOffsetX - ContentOffsetX - l.MinX) * l.Scale;
        double vy = l.Oy + (ScrollOffsetY - ContentOffsetY - l.MinY) * l.Scale;
        double vw = Math.Max(4, ViewportWidth * l.Scale);
        double vh = Math.Max(4, ViewportHeight * l.Scale);
        vx = WorkflowSurfaceMath.ClampValue(vx, 0, Width - vw);
        vy = WorkflowSurfaceMath.ClampValue(vy, 0, Height - vh);
        return new RectangleF((float)vx, (float)vy, (float)vw, (float)vh);
    }

    private readonly struct MinimapLayout
    {
        public readonly double MinX, MinY, Scale, Ox, Oy;

        public MinimapLayout(
            double minX, double minY, double scale, double ox, double oy)
        {
            MinX = minX;
            MinY = minY;
            Scale = scale;
            Ox = ox;
            Oy = oy;
        }
    }
}
