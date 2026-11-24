using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using Easel;
using Easel.Visuals;
using VeloxDev.WorkflowSystem.Platform;
using DrawingPen = System.Drawing.Pen;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

/// <summary>A visual thumbnail overview of a workflow surface, drawn inside the visual tree.</summary>
/// <remarks>
/// The surface pushes offsets and the viewport through <see cref="IWorkflowMinimapOverlay"/>; a press or drag maps the
/// pointer back to a world viewport origin and raises <see cref="ViewportScrollRequested"/>. The thumbnail and the
/// viewport block share one content-fit transform, so clicking a point centres the surface there.
/// </remarks>
public class WorkflowMinimapOverlay : VisualElement, IWorkflowMinimapOverlay, IWorkflowMinimapScrollSource
{
    private const double DesignWidth = 200d;
    private const double DesignHeight = 140d;
    private const double ContentPadding = 4d;

    private Color _minimapBackground = Color.FromArgb(0xD2, 0x14, 0x19, 0x22);
    private Color _minimapBorderBrush = Color.FromArgb(0xDC, 0x94, 0xA3, 0xB8);
    private Color _nodeBrush = Color.FromArgb(0xDC, 0x38, 0xBD, 0xF8);
    private Color _viewportStroke = Color.FromArgb(0xF0, 0xFF, 0xFF, 0xFF);
    private readonly List<(double X, double Y, double W, double H)> _nodeRects = [];
    private bool _dragging;

    /// <summary>Creates the overlay at its design size.</summary>
    public WorkflowMinimapOverlay()
    {
        Width = DesignWidth;
        Height = DesignHeight;
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

    /// <summary>Parses a <c>#RRGGBB</c>, <c>#AARRGGBB</c> or named colour.</summary>
    protected static Color ParseColor(string hex) => XamlRuntime.ParseColor(hex);

    /// <inheritdoc />
    protected override void OnRender(Graphics graphics)
    {
        if (!IsMinimapVisible)
        {
            return;
        }

        var width = Bounds.Width;
        var height = Bounds.Height;
        if (width <= 0f || height <= 0f)
        {
            return;
        }

        graphics.SmoothingMode = SmoothingMode.AntiAlias;

        var layout = ComputeLayout(_nodeRects);
        if (layout is null)
        {
            return;
        }

        DrawMinimap(graphics, CreateDrawContext(layout.Value, width, height));
    }

    /// <summary>
    /// Draws the minimap from <paramref name="context"/>. Override and call <c>base</c> to add to the default drawing.
    /// </summary>
    protected virtual void DrawMinimap(Graphics graphics, MinimapDrawContext context)
    {
        var width = (float)context.MinimapWidth;
        var height = (float)context.MinimapHeight;
        var rect = new RectangleF(0f, 0f, width, height);
        using var background = new SolidBrush(_minimapBackground);
        using var border = new DrawingPen(_minimapBorderBrush, 1f);
        graphics.FillRectangle(background, rect);
        graphics.DrawRectangle(border, 0f, 0f, Math.Max(0f, width - 1f), Math.Max(0f, height - 1f));

        using var nodeBrush = new SolidBrush(_nodeBrush);
        foreach (var node in context.MappedNodes)
        {
            graphics.FillRectangle(nodeBrush, (float)node.Left, (float)node.Top, (float)node.Width, (float)node.Height);
        }

        if (context.HasViewport && !context.MappedViewport.IsEmpty)
        {
            using var viewportPen = new DrawingPen(_viewportStroke, 1.5f);
            var vp = context.MappedViewport;
            graphics.DrawRectangle(viewportPen, (float)vp.Left, (float)vp.Top, (float)vp.Width, (float)vp.Height);
        }
    }

    /// <summary>Builds the geometry for one draw pass from the computed layout and cached node rects.</summary>
    private MinimapDrawContext CreateDrawContext(MinimapLayout layout, float width, float height)
    {
        var nodes = new List<WorkflowBounds>(_nodeRects.Count);
        var mapped = new List<WorkflowBounds>(_nodeRects.Count);
        foreach (var (nx, ny, nw, nh) in _nodeRects)
        {
            nodes.Add(WorkflowBounds.FromNode(nx, ny, nw, nh));
            var (x, y) = WorkflowSurfaceMath.MinimapLocal(nx, ny, layout.Left, layout.Top, layout.Ox, layout.Oy, layout.Scale);
            mapped.Add(WorkflowBounds.FromNode(x, y,
                WorkflowSurfaceMath.MinThumbSize(nw, layout.Scale, 2d),
                WorkflowSurfaceMath.MinThumbSize(nh, layout.Scale, 2d)));
        }

        var viewport = ViewportRect(layout);
        var viewportBounds = WorkflowBounds.FromNode(
            ScrollOffsetX - ContentOffsetX, ScrollOffsetY - ContentOffsetY, ViewportWidth, ViewportHeight);
        var mappedViewport = viewport.Width > 0 && viewport.Height > 0
            ? WorkflowBounds.FromNode(viewport.X, viewport.Y, viewport.Width, viewport.Height)
            : default;

        return new MinimapDrawContext
        {
            Tree = WorkflowTree,
            ContentBounds = WorkflowBounds.FromNode(layout.Left, layout.Top, layout.Width, layout.Height),
            ViewportBounds = viewportBounds,
            Nodes = nodes,
            MappedNodes = mapped,
            MappedViewport = mappedViewport,
            MinimapWidth = width,
            MinimapHeight = height,
            Padding = Math.Max(0d, ContentPadding),
            OriginX = layout.Ox,
            OriginY = layout.Oy,
            Scale = layout.Scale,
        };
    }

    /// <inheritdoc />
    protected override void OnPointerPressed(VisualPointerEventArgs args)
    {
        if (args.Button != System.Windows.Forms.MouseButtons.Left)
        {
            return;
        }

        var layout = ComputeLayout();
        if (layout is null)
        {
            return;
        }

        _dragging = true;
        UpdateViewport(args.GetPosition(this), layout.Value);
        args.Handled = true;
    }

    /// <inheritdoc />
    protected override void OnPointerMoved(VisualPointerEventArgs args)
    {
        if (!_dragging)
        {
            return;
        }

        var layout = ComputeLayout();
        if (layout is not null)
        {
            UpdateViewport(args.GetPosition(this), layout.Value);
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

    private void UpdateViewport(PointF point, MinimapLayout layout)
    {
        var (localX, localY) = WorkflowSurfaceMath.MinimapToCanvasLocal(
            point.X, point.Y, layout.Ox, layout.Oy, layout.Scale, layout.Left, layout.Top);
        var (sx, sy) = WorkflowSurfaceMath.MinimapToScroll(
            localX, localY, ViewportWidth, ViewportHeight, ContentOffsetX, ContentOffsetY);
        ScrollOffsetX = sx;
        ScrollOffsetY = sy;
        InvalidateVisual();
        ViewportScrollRequested?.Invoke(sx, sy);
    }

    private MinimapLayout? ComputeLayout(List<(double X, double Y, double W, double H)>? thumbnails = null)
    {
        var tree = WorkflowTree;
        if (tree?.Nodes is null)
        {
            return null;
        }

        thumbnails?.Clear();
        var rects = new List<(double X, double Y, double W, double H)>();
        foreach (var node in tree.Nodes)
        {
            if (node.Anchor is not { } anchor || node.Size is not { } size)
            {
                continue;
            }

            var rect = (anchor.Horizontal, anchor.Vertical, size.Width, size.Height);
            rects.Add(rect);
            thumbnails?.Add(rect);
        }

        if (rects.Count == 0)
        {
            return null;
        }

        var global = WorkflowBounds.FromNodes(rects);
        var padding = Math.Max(0d, ContentPadding);
        var (ox, oy, scale) = WorkflowSurfaceMath.MinimapFit(
            global.Width,
            global.Height,
            Math.Max(1d, Bounds.Width - (padding * 2d)),
            Math.Max(1d, Bounds.Height - (padding * 2d)),
            padding);
        return new MinimapLayout(global.Left, global.Top, global.Width, global.Height, scale, ox, oy);
    }

    private RectangleF ViewportRect(MinimapLayout layout)
    {
        var (x, y, width, height) = WorkflowSurfaceMath.MinimapViewportRect(
            layout.Ox,
            layout.Oy,
            layout.Scale,
            ScrollOffsetX - ContentOffsetX,
            ScrollOffsetY - ContentOffsetY,
            ViewportWidth,
            ViewportHeight,
            layout.Left,
            layout.Top,
            Bounds.Width,
            Bounds.Height,
            minRectSize: 4d);
        return new RectangleF((float)x, (float)y, (float)width, (float)height);
    }

    private readonly struct MinimapLayout(double left, double top, double width, double height, double scale, double ox, double oy)
    {
        public double Left { get; } = left;
        public double Top { get; } = top;
        public double Width { get; } = width;
        public double Height { get; } = height;
        public double Scale { get; } = scale;
        public double Ox { get; } = ox;
        public double Oy { get; } = oy;
    }
}
