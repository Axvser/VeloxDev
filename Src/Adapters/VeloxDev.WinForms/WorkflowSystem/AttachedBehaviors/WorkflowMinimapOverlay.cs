using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using Easel;
using Easel.Visuals;
using VeloxDev.WorkflowSystem.Platform;
using DrawingPen = System.Drawing.Pen;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

/// <summary>
/// A visual thumbnail overview of a workflow surface, drawn as a full-surface layer inside the visual tree.
/// </summary>
/// <remarks>
/// <para>
/// The layer fills the surface; the default drawing places the thumbnail in the content viewport's top-right
/// corner, below the ruler band and left of the scrollbars, using the area the surface pushes through
/// <see cref="AvailableRight"/>, <see cref="AvailableTop"/> and <see cref="AvailableBottom"/>. Only that rectangle
/// is hit-testable, so the rest of the layer lets pointer input through.
/// </para>
/// <para>
/// The surface pushes offsets and the viewport through <see cref="IWorkflowMinimapOverlay"/>; a press or drag maps
/// the pointer back to a world viewport origin and raises <see cref="ViewportScrollRequested"/>. The thumbnail and
/// the viewport block share one content-fit transform, so clicking a point centres the surface there.
/// </para>
/// </remarks>
public class WorkflowMinimapOverlay : VisualElement, IWorkflowMinimapOverlay, IWorkflowMinimapScrollSource
{
    private const double DefaultWidth = 200d;
    private const double DefaultHeight = 140d;
    private const double MinimumSize = 20d;
    private const double ContentPadding = 4d;

    private Color _minimapBackground = Color.FromArgb(0x33, 0x1E, 0x1E, 0x1E);
    private Color _minimapBorderBrush = Color.FromArgb(0xDC, 0x94, 0xA3, 0xB8);
    private Color _nodeBrush = Color.FromArgb(0xDC, 0x38, 0xBD, 0xF8);
    private Color _viewportStroke = Color.FromArgb(0xF0, 0xFF, 0xFF, 0xFF);
    private readonly List<(double X, double Y, double W, double H)> _nodeRects = [];
    private double _minimapWidth = DefaultWidth;
    private double _minimapHeight = DefaultHeight;
    private double _minimapMargin = 12d;
    private double _availableRight;
    private double _availableTop;
    private double _availableBottom;
    private bool _dragging;

    /// <summary>Creates the overlay as a full-surface layer.</summary>
    public WorkflowMinimapOverlay()
    {
        IsHitTestVisible = true;
        ClipToBounds = true;
    }

    /// <inheritdoc />
    public event Action<double, double>? ViewportScrollRequested;

    /// <summary>Panel background.</summary>
    public Color MinimapBackground
    {
        get => _minimapBackground;
        set { _minimapBackground = value; InvalidateVisual(); }
    }

    /// <summary>Panel border colour.</summary>
    public Color MinimapBorderBrush
    {
        get => _minimapBorderBrush;
        set { _minimapBorderBrush = value; InvalidateVisual(); }
    }

    /// <summary>Fill for each node's thumbnail.</summary>
    public Color NodeBrush
    {
        get => _nodeBrush;
        set { _nodeBrush = value; InvalidateVisual(); }
    }

    /// <summary>Stroke for the draggable viewport block.</summary>
    public Color ViewportStroke
    {
        get => _viewportStroke;
        set { _viewportStroke = value; InvalidateVisual(); }
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
    public ITreeComponent? WorkflowTree { get; set; }

    /// <inheritdoc />
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool IsMinimapVisible { get; set; } = true;

    /// <summary>The requested thumbnail width in surface pixels; the available corner may clamp it.</summary>
    public double MinimapWidth
    {
        get => _minimapWidth;
        set { if (_minimapWidth == value) return; _minimapWidth = value; InvalidateVisual(); }
    }

    /// <summary>The requested thumbnail height in surface pixels; the available corner may clamp it.</summary>
    public double MinimapHeight
    {
        get => _minimapHeight;
        set { if (_minimapHeight == value) return; _minimapHeight = value; InvalidateVisual(); }
    }

    /// <inheritdoc />
    public double MinimapMargin
    {
        get => _minimapMargin;
        set { if (_minimapMargin == value) return; _minimapMargin = value; InvalidateVisual(); }
    }

    /// <inheritdoc />
    public double AvailableRight
    {
        get => _availableRight;
        set { if (_availableRight == value) return; _availableRight = value; InvalidateVisual(); }
    }

    /// <inheritdoc />
    public double AvailableTop
    {
        get => _availableTop;
        set { if (_availableTop == value) return; _availableTop = value; InvalidateVisual(); }
    }

    /// <inheritdoc />
    public double AvailableBottom
    {
        get => _availableBottom;
        set { if (_availableBottom == value) return; _availableBottom = value; InvalidateVisual(); }
    }

    /// <summary>Parses a <c>#RRGGBB</c>, <c>#AARRGGBB</c> or named colour.</summary>
    protected static Color ParseColor(string hex) => XamlRuntime.ParseColor(hex);

    /// <inheritdoc />
    protected override bool HitTestContent(PointF point)
    {
        if (!IsHitTestVisible || !IsMinimapVisible)
        {
            return false;
        }

        return ComputeMinimapRect().Contains(point);
    }

    /// <inheritdoc />
    protected override void OnRender(Graphics graphics)
    {
        if (!IsMinimapVisible || Bounds.Width <= 0f || Bounds.Height <= 0f)
        {
            return;
        }

        var rect = ComputeMinimapRect();
        if (rect.Width <= 0f || rect.Height <= 0f)
        {
            return;
        }

        var context = CreateDrawContext(rect);
        if (context is null)
        {
            return;
        }

        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        DrawMinimap(graphics, context);
    }

    /// <summary>
    /// Draws the minimap from <paramref name="context"/>. Override and call <c>base</c> to add to the default drawing.
    /// </summary>
    protected virtual void DrawMinimap(Graphics graphics, MinimapDrawContext context)
    {
        var bounds = context.GetMinimapRect(MinimumSize);
        var left = (float)bounds.Left;
        var top = (float)bounds.Top;
        var width = (float)bounds.Width;
        var height = (float)bounds.Height;
        var rect = new RectangleF(left, top, width, height);

        using var background = new SolidBrush(_minimapBackground);
        using var border = new DrawingPen(_minimapBorderBrush, 1f);
        graphics.FillRectangle(background, rect);
        graphics.DrawRectangle(border, left, top, Math.Max(0f, width - 1f), Math.Max(0f, height - 1f));

        // 缩略图和视口块只在面板内绘制，最小拇指尺寸带来的溢出不会画到表面空白处。
        var state = graphics.Save();
        graphics.SetClip(rect);
        try
        {
            using var nodeBrush = new SolidBrush(_nodeBrush);
            foreach (var node in context.MappedNodes)
            {
                graphics.FillRectangle(nodeBrush,
                    left + (float)node.Left, top + (float)node.Top,
                    (float)node.Width, (float)node.Height);
            }

            if (context.HasViewport && !context.MappedViewport.IsEmpty)
            {
                using var viewportPen = new DrawingPen(_viewportStroke, 1.5f);
                var vp = context.MappedViewport;
                graphics.DrawRectangle(viewportPen,
                    left + (float)vp.Left, top + (float)vp.Top,
                    (float)vp.Width, (float)vp.Height);
            }
        }
        finally
        {
            graphics.Restore(state);
        }
    }

    // 一次绘制/拖拽所需的节点、视口与拟合变换，全部按小地图矩形的原点与尺寸算。
    private MinimapDrawContext? CreateDrawContext(RectangleF rect)
    {
        var tree = WorkflowTree;
        if (tree?.Nodes is null)
        {
            return null;
        }

        _nodeRects.Clear();
        foreach (var node in tree.Nodes)
        {
            if (node.Anchor is not { } anchor || node.Size is not { } size)
            {
                continue;
            }

            _nodeRects.Add((anchor.Horizontal, anchor.Vertical, size.Width, size.Height));
        }

        if (_nodeRects.Count == 0)
        {
            return null;
        }

        var global = WorkflowBounds.FromNodes(_nodeRects);
        var mmW = Math.Max(1d, rect.Width);
        var mmH = Math.Max(1d, rect.Height);
        var padding = Math.Max(0d, ContentPadding);
        var (ox, oy, scale) = WorkflowSurfaceMath.MinimapFit(
            global.Width,
            global.Height,
            Math.Max(1d, mmW - (padding * 2d)),
            Math.Max(1d, mmH - (padding * 2d)),
            padding);

        var nodes = new List<WorkflowBounds>(_nodeRects.Count);
        var mapped = new List<WorkflowBounds>(_nodeRects.Count);
        foreach (var (nx, ny, nw, nh) in _nodeRects)
        {
            nodes.Add(WorkflowBounds.FromNode(nx, ny, nw, nh));
            var (x, y) = WorkflowSurfaceMath.MinimapLocal(nx, ny, global.Left, global.Top, ox, oy, scale);
            mapped.Add(WorkflowBounds.FromNode(x, y,
                WorkflowSurfaceMath.MinThumbSize(nw, scale, 2d),
                WorkflowSurfaceMath.MinThumbSize(nh, scale, 2d)));
        }

        var viewport = WorkflowBounds.FromNode(
            ScrollOffsetX - ContentOffsetX, ScrollOffsetY - ContentOffsetY, ViewportWidth, ViewportHeight);
        var mappedViewport = default(WorkflowBounds);
        if (!viewport.IsEmpty)
        {
            var (vx, vy, vw, vh) = WorkflowSurfaceMath.MinimapViewportRect(
                ox, oy, scale, viewport.Left, viewport.Top, viewport.Width, viewport.Height,
                global.Left, global.Top, mmW, mmH, minRectSize: 4d);
            mappedViewport = WorkflowBounds.FromNode(vx, vy, vw, vh);
        }

        var placement = CreatePlacementContext();
        return new MinimapDrawContext
        {
            Tree = tree,
            ContentBounds = global,
            ViewportBounds = viewport,
            Nodes = nodes,
            MappedNodes = mapped,
            MappedViewport = mappedViewport,
            MinimapWidth = mmW,
            MinimapHeight = mmH,
            Padding = padding,
            OriginX = ox,
            OriginY = oy,
            Scale = scale,
            AvailableRight = placement.AvailableRight,
            AvailableTop = placement.AvailableTop,
            AvailableBottom = placement.AvailableBottom,
            MinimapMargin = placement.MinimapMargin,
        };
    }

    // 命中、绘制与拖拽导航共用同一组放置输入。
    private MinimapDrawContext CreatePlacementContext()
    {
        var width = Math.Max(0d, Bounds.Width);
        var height = Math.Max(0d, Bounds.Height);
        var top = Math.Max(0d, AvailableTop);
        return new MinimapDrawContext
        {
            MinimapWidth = MinimapWidth,
            MinimapHeight = MinimapHeight,
            MinimapMargin = MinimapMargin,
            AvailableRight = AvailableRight > 0d ? AvailableRight : width,
            AvailableTop = top,
            AvailableBottom = AvailableBottom > top ? AvailableBottom : height,
        };
    }

    // 可用区来自 surface：右侧让开竖滚动条，顶部让开水平刻度，底部让开横滚动条。
    private RectangleF ComputeMinimapRect()
    {
        var bounds = CreatePlacementContext().GetMinimapRect(MinimumSize);
        return new RectangleF(
            (float)bounds.Left,
            (float)bounds.Top,
            (float)bounds.Width,
            (float)bounds.Height);
    }

    /// <inheritdoc />
    protected override void OnPointerPressed(VisualPointerEventArgs args)
    {
        if (args.Button != System.Windows.Forms.MouseButtons.Left)
        {
            return;
        }

        var rect = ComputeMinimapRect();
        var point = args.GetPosition(this);
        if (!rect.Contains(point))
        {
            return;
        }

        var context = CreateDrawContext(rect);
        if (context is null)
        {
            return;
        }

        _dragging = true;
        UpdateViewport(point, rect, context);
        args.Handled = true;
    }

    /// <inheritdoc />
    protected override void OnPointerMoved(VisualPointerEventArgs args)
    {
        if (!_dragging)
        {
            return;
        }

        var rect = ComputeMinimapRect();
        var context = CreateDrawContext(rect);
        if (context is not null)
        {
            UpdateViewport(args.GetPosition(this), rect, context);
        }

        args.Handled = true;
    }

    /// <inheritdoc />
    protected override void OnPointerReleased(VisualPointerEventArgs args)
    {
        if (!_dragging)
        {
            return;
        }

        _dragging = false;
        args.Handled = true;
    }

    private void UpdateViewport(PointF point, RectangleF rect, MinimapDrawContext context)
    {
        // 指针是整层坐标，映射前先减掉小地图矩形的原点。
        var (localX, localY) = WorkflowSurfaceMath.MinimapToCanvasLocal(
            point.X - rect.Left,
            point.Y - rect.Top,
            context.OriginX,
            context.OriginY,
            context.Scale,
            context.ContentBounds.Left,
            context.ContentBounds.Top);
        var (sx, sy) = WorkflowSurfaceMath.MinimapToScroll(
            localX, localY, ViewportWidth, ViewportHeight, ContentOffsetX, ContentOffsetY);
        ScrollOffsetX = sx;
        ScrollOffsetY = sy;
        InvalidateVisual();
        ViewportScrollRequested?.Invoke(sx, sy);
    }
}
