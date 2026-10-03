using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

/// <summary>
/// The workflow grid: a dotted background, the world axes, and two ruler bands with ticks and labels.
/// </summary>
/// <remarks>
/// <para>
/// Implements <see cref="IWorkflowGridDecorator"/> so <see cref="WorkflowSurfaceBehavior"/> pushes the offsets it
/// draws with. Derive from it for the palette and the spacing; the world-coordinate maths, the tick layout, the
/// label formatting and the clipping are the same in every host.
/// </para>
/// <para>
/// The default palette and spacing match the shared item template's, so a subclass that overrides nothing draws
/// what the template draws.
/// </para>
/// </remarks>
public class WorkflowGridDecorator : Panel, IWorkflowGridDecorator
{
    /// <summary>
    /// Default ruler-band thickness.
    /// </summary>
    /// <remarks>
    /// Other flavours of this control use 28; WinForms reads visually smaller, so the platform default is 36.
    /// </remarks>
    public const double DefaultRulerThickness = 36;

    private readonly Font _labelFont = new("Segoe UI", 13f, GraphicsUnit.Pixel);

    private Color _gridBackground = Color.FromArgb(0x1E, 0x1E, 0x1E);
    private Color _minorGridColor = Color.FromArgb(0x2A, 0x2D, 0x2E);
    private Color _majorGridColor = Color.FromArgb(0x3A, 0x3D, 0x40);
    private Color _axisColor = Color.FromArgb(0x4D, 0x4D, 0x4D);
    private Color _rulerBackground = Color.FromArgb(0x70, 0x25, 0x25, 0x26);
    private Color _rulerTickColor = Color.FromArgb(0x55, 0x55, 0x55);
    private Color _rulerLabelColor = Color.FromArgb(0x88, 0x88, 0x88);
    private Color _rulerDividerColor = Color.FromArgb(0x3A, 0x3D, 0x40);
    private double _gridSpacing = 40;
    private int _majorLineEvery = 5;

    /// <summary>Creates the grid.</summary>
    public WorkflowGridDecorator()
    {
        DoubleBuffered = true;
        BackColor = _gridBackground;
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint,
            true);
    }

    /// <summary>The surface behind the grid.</summary>
    public Color GridBackground
    {
        get => _gridBackground;
        set
        {
            if (_gridBackground == value) return;
            _gridBackground = value;
            BackColor = value;
            Invalidate();
        }
    }

    /// <summary>Colour of the minor grid lines.</summary>
    public Color MinorGridColor
    {
        get => _minorGridColor;
        set { if (_minorGridColor != value) { _minorGridColor = value; Invalidate(); } }
    }

    /// <summary>Colour of the major grid lines.</summary>
    public Color MajorGridColor
    {
        get => _majorGridColor;
        set { if (_majorGridColor != value) { _majorGridColor = value; Invalidate(); } }
    }

    /// <summary>Colour of the world axes.</summary>
    public Color AxisColor
    {
        get => _axisColor;
        set { if (_axisColor != value) { _axisColor = value; Invalidate(); } }
    }

    /// <summary>Fill of the two ruler bands.</summary>
    /// <remarks>
    /// The default keeps a low alpha: node cards paint opaque over the band, so a heavier tint would stop the grid
    /// reading through it.
    /// </remarks>
    public Color RulerBackground
    {
        get => _rulerBackground;
        set { if (_rulerBackground != value) { _rulerBackground = value; Invalidate(); } }
    }

    /// <summary>Colour of the ruler ticks.</summary>
    public Color RulerTickColor
    {
        get => _rulerTickColor;
        set { if (_rulerTickColor != value) { _rulerTickColor = value; Invalidate(); } }
    }

    /// <summary>Colour of the ruler labels.</summary>
    public Color RulerLabelColor
    {
        get => _rulerLabelColor;
        set { if (_rulerLabelColor != value) { _rulerLabelColor = value; Invalidate(); } }
    }

    /// <summary>Colour of the lines dividing each band from the grid.</summary>
    public Color RulerDividerColor
    {
        get => _rulerDividerColor;
        set { if (_rulerDividerColor != value) { _rulerDividerColor = value; Invalidate(); } }
    }

    /// <summary>World distance between minor grid lines, in pixels.</summary>
    public double GridSpacing
    {
        get => _gridSpacing;
        set { if (_gridSpacing != value) { _gridSpacing = value; Invalidate(); } }
    }

    /// <summary>How many minor lines make a major one. Values below one are treated as one.</summary>
    public int MajorLineEvery
    {
        get => _majorLineEvery;
        set { if (_majorLineEvery != value) { _majorLineEvery = value; Invalidate(); } }
    }

    /// <summary>Thickness of the two ruler bands, in pixels.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double RulerThickness { get; set; } = DefaultRulerThickness;

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

    /// <inheritdoc />
    public double RulerBand => RulerThickness;

    /// <inheritdoc />
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        if (e is null) throw new ArgumentNullException(nameof(e));

        base.OnPaintBackground(e);
        // 整块面都在这里画：WinForms 的透明子控件（树的视口、画布、连线层）合成的父控件画的是
        // OnPaintBackground、**不是** OnPaint，所以网格与标尺必须画在这个方法里才透得出来。
        Render(e.Graphics);
    }

    /// <inheritdoc />
    protected override void OnPaint(PaintEventArgs e)
    {
        // 表面绘制在 OnPaintBackground 里；见那里的说明。
    }

    private void Render(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var bounds = new RectangleF(0, 0, Width, Height);
        var ruler = Math.Max(0, RulerThickness);

        using var bgBrush = new SolidBrush(_gridBackground);
        using var rulerBrush = new SolidBrush(_rulerBackground);
        g.FillRectangle(bgBrush, bounds);

        // 网格整幅画（不按内容矩形裁），线因此能伸到半透明带下面；带子随后铺上，把滚过去的东西压暗
        // （Jalium 那套浮动标尺的模型）。网格保留 +ruler 的偏移，因为独立装饰器位于一个被 RulerThickness
        // 平移过的内容视口之下，世界原点因此停在带子的内边。
        DrawGrid(g, bounds, ruler);

        g.FillRectangle(rulerBrush, 0, 0, bounds.Width, (float)ruler);
        g.FillRectangle(rulerBrush, 0, 0, (float)ruler, bounds.Height);

        DrawRulers(g, bounds, ruler);
    }

    private void DrawGrid(Graphics g, RectangleF bounds, double ruler)
    {
        var spacing = Math.Max(8, _gridSpacing);
        var majorStep = spacing * Math.Max(1, _majorLineEvery);
        var worldLeft = WorkflowSurfaceMath.GridWorldLeft(ScrollOffsetX, ContentOffsetX);
        var worldTop = WorkflowSurfaceMath.GridWorldTop(ScrollOffsetY, ContentOffsetY);
        var worldRight = worldLeft + bounds.Width;
        var worldBottom = worldTop + bounds.Height;

        using var minorPen = new Pen(_minorGridColor, 1f);
        using var majorPen = new Pen(_majorGridColor, 1f);
        using var axisPen = new Pen(_axisColor, 1.2f);

        // 网格 x = ruler + (value - worldLeft)：独立装饰器把世界网格画在一个被 RulerThickness 平移过的内容
        // 视口之下，所以原点停在带子内边。线画满视口，于是能伸到带子下面。
        var firstVertical = WorkflowSurfaceMath.GridFirstLine(worldLeft, spacing);
        for (var value = firstVertical; value <= worldRight + spacing; value += spacing)
        {
            var x = (float)WorkflowSurfaceMath.GridX(value, worldLeft, ruler);
            var pen = WorkflowSurfaceGrid.SelectPen(value, majorStep, minorPen, majorPen, axisPen);
            g.DrawLine(pen, x, 0, x, bounds.Height);
        }

        var firstHorizontal = WorkflowSurfaceMath.GridFirstLine(worldTop, spacing);
        for (var value = firstHorizontal; value <= worldBottom + spacing; value += spacing)
        {
            var y = (float)WorkflowSurfaceMath.GridY(value, worldTop, ruler);
            var pen = WorkflowSurfaceGrid.SelectPen(value, majorStep, minorPen, majorPen, axisPen);
            g.DrawLine(pen, 0, y, bounds.Width, y);
        }
    }

    private void DrawRulers(Graphics g, RectangleF bounds, double ruler)
    {
        var spacing = Math.Max(8, _gridSpacing);
        var majorStep = spacing * Math.Max(1, _majorLineEvery);
        var worldLeft = WorkflowSurfaceMath.GridWorldLeft(ScrollOffsetX, ContentOffsetX);
        var worldTop = WorkflowSurfaceMath.GridWorldTop(ScrollOffsetY, ContentOffsetY);
        var worldRight = worldLeft + bounds.Width;
        var worldBottom = worldTop + bounds.Height;

        using var dividerPen = new Pen(_rulerDividerColor, 1f);
        using var tickPen = new Pen(_rulerTickColor, 1f);
        using var axisPen = new Pen(_axisColor, 1f);
        using var labelBrush = new SolidBrush(_rulerLabelColor);
        using var format = new StringFormat(StringFormat.GenericTypographic);

        g.DrawLine(dividerPen, (float)ruler, 0, (float)ruler, bounds.Height);
        g.DrawLine(dividerPen, 0, (float)ruler, bounds.Width, (float)ruler);

        // 上标尺。刻度与网格共用 x = ruler + (value - worldLeft)。跳过 x < ruler，让转角与左侧带子干净
        // （那里不画刻度与标签）。
        var saved = g.Save();
        g.SetClip(new RectangleF((float)ruler, 0, Math.Max(0, bounds.Width - (float)ruler), (float)ruler));
        var firstVertical = WorkflowSurfaceMath.GridFirstLine(worldLeft, spacing);
        for (var value = firstVertical; value <= worldRight + spacing; value += spacing)
        {
            var x = (float)WorkflowSurfaceMath.GridX(value, worldLeft, ruler);
            if (x < ruler)
            {
                continue;
            }

            var isMajor = WorkflowSurfaceGrid.IsMajorLine(value, majorStep);
            var tickLength = isMajor ? (float)(ruler - 6) : Math.Max(6f, (float)(ruler * 0.35));
            var pen = WorkflowSurfaceGrid.IsNearZero(value) ? axisPen : tickPen;
            g.DrawLine(pen, x, (float)ruler, x, (float)(ruler - tickLength));

            if (isMajor)
            {
                var text = WorkflowSurfaceGrid.FormatGridValue(value);
                g.DrawString(text, _labelFont, labelBrush, x + 3, 2, format);
            }
        }
        g.Restore(saved);

        // 左标尺。
        saved = g.Save();
        g.SetClip(new RectangleF(0, (float)ruler, (float)ruler, Math.Max(0, bounds.Height - (float)ruler)));
        var firstHorizontal = WorkflowSurfaceMath.GridFirstLine(worldTop, spacing);
        for (var value = firstHorizontal; value <= worldBottom + spacing; value += spacing)
        {
            var y = (float)WorkflowSurfaceMath.GridY(value, worldTop, ruler);
            if (y < ruler)
            {
                continue;
            }

            var isMajor = WorkflowSurfaceGrid.IsMajorLine(value, majorStep);
            var tickLength = isMajor ? (float)(ruler - 6) : Math.Max(6f, (float)(ruler * 0.35));
            var pen = WorkflowSurfaceGrid.IsNearZero(value) ? axisPen : tickPen;
            g.DrawLine(pen, (float)ruler, y, (float)(ruler - tickLength), y);

            if (isMajor)
            {
                var text = WorkflowSurfaceGrid.FormatGridValue(value);
                g.DrawString(text, _labelFont, labelBrush, 3, y + 2, format);
            }
        }
        g.Restore(saved);
    }

    /// <summary>Parses a colour.</summary>
    /// <param name="hex">The colour text.</param>
    /// <returns>The colour.</returns>
    protected static Color ParseColor(string hex) => WorkflowSurfaceColors.Parse(hex);
}
