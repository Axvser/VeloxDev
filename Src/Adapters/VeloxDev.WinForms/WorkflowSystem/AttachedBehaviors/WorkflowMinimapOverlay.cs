using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using VeloxDev.WinForms.Xaml;
using VeloxDev.WinForms.Xaml.Visuals;
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
        var rect = new RectangleF(0f, 0f, width, height);
        using var background = new SolidBrush(_minimapBackground);
        using var border = new DrawingPen(_minimapBorderBrush, 1f);
        graphics.FillRectangle(background, rect);
        graphics.DrawRectangle(border, 0f, 0f, Math.Max(0f, width - 1f), Math.Max(0f, height - 1f));

        var layout = ComputeLayout(_nodeRects);
        if (layout is null)
        {
            return;
        }

        var fitted = layout.Value;
        using var nodeBrush = new SolidBrush(_nodeBrush);
        using var viewportPen = new DrawingPen(_viewportStroke, 1.5f);
        foreach (var (nx, ny, nw, nh) in _nodeRects)
        {
            var x = fitted.Ox + ((nx - fitted.MinX) * fitted.Scale);
            var y = fitted.Oy + ((ny - fitted.MinY) * fitted.Scale);
            var w = Math.Max(2d, nw * fitted.Scale);
            var h = Math.Max(2d, nh * fitted.Scale);
            graphics.FillRectangle(nodeBrush, (float)x, (float)y, (float)w, (float)h);
        }

        var viewport = ViewportRect(fitted);
        graphics.DrawRectangle(viewportPen, viewport.X, viewport.Y, viewport.Width, viewport.Height);
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
        var sx = ((point.X - layout.Ox) / layout.Scale) + layout.MinX - (ViewportWidth / 2d) + ContentOffsetX;
        var sy = ((point.Y - layout.Oy) / layout.Scale) + layout.MinY - (ViewportHeight / 2d) + ContentOffsetY;
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

        if (!hasNode)
        {
            return null;
        }

        var contentW = Math.Max(1d, maxX - minX);
        var contentH = Math.Max(1d, maxY - minY);
        var drawW = Math.Max(1d, Bounds.Width - (ContentPadding * 2d));
        var drawH = Math.Max(1d, Bounds.Height - (ContentPadding * 2d));
        var scale = Math.Min(drawW / contentW, drawH / contentH);
        var ox = ContentPadding + ((drawW - (contentW * scale)) / 2d);
        var oy = ContentPadding + ((drawH - (contentH * scale)) / 2d);
        return new MinimapLayout(minX, minY, scale, ox, oy);
    }

    private RectangleF ViewportRect(MinimapLayout layout)
    {
        var vx = layout.Ox + ((ScrollOffsetX - ContentOffsetX - layout.MinX) * layout.Scale);
        var vy = layout.Oy + ((ScrollOffsetY - ContentOffsetY - layout.MinY) * layout.Scale);
        var vw = Math.Min(Bounds.Width, Math.Max(4d, ViewportWidth * layout.Scale));
        var vh = Math.Min(Bounds.Height, Math.Max(4d, ViewportHeight * layout.Scale));
        vx = XamlMath.Clamp(vx, 0d, Math.Max(0d, Bounds.Width - vw));
        vy = XamlMath.Clamp(vy, 0d, Math.Max(0d, Bounds.Height - vh));
        return new RectangleF((float)vx, (float)vy, (float)vw, (float)vh);
    }

    private readonly struct MinimapLayout(double minX, double minY, double scale, double ox, double oy)
    {
        public double MinX { get; } = minX;
        public double MinY { get; } = minY;
        public double Scale { get; } = scale;
        public double Ox { get; } = ox;
        public double Oy { get; } = oy;
    }
}
