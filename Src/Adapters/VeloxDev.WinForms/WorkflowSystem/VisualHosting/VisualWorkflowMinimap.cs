using System.Drawing;
using System.Drawing.Drawing2D;
using VeloxDev.WinForms.Xaml.Visuals;
using VeloxDev.WorkflowSystem;
using XamlThickness = VeloxDev.WinForms.Xaml.Controls.Thickness;
using DrawingPointF = System.Drawing.PointF;
using DrawingRectangleF = System.Drawing.RectangleF;
using DrawingSize = System.Drawing.Size;
using Wf = VeloxDev.WorkflowSystem;

namespace VeloxDev.WorkflowSystem.VisualHosting;

/// <summary>
/// A visual overview of the whole tree with the current viewport rectangle, draggable to scroll the surface.
/// </summary>
/// <remarks>
/// The drawing reuses the shared minimap maths (<see cref="WorkflowSurfaceMath.MinimapFit"/>,
/// <see cref="WorkflowSurfaceMath.MinimapLocal"/>, <see cref="WorkflowSurfaceMath.MinimapViewportRect"/>). A drag
/// converts the pointer travel back to canvas-local units and asks the surface's
/// <see cref="IWorkflowSurfaceScroller"/> to move by that amount — the minimap never writes the pan itself.
/// </remarks>
public sealed class VisualWorkflowMinimap : VisualElement
{
    /// <summary>The default width, in pixels.</summary>
    public const float DefaultWidth = 168f;

    /// <summary>The default height, in pixels.</summary>
    public const float DefaultHeight = 112f;

    private const double PaddingValue = 6d;
    private const double MinThumbSize = 2d;

    private ITreeComponent? _tree;
    private IWorkflowSurfaceScroller? _scroller;
    private DrawingPointF _pan;
    private DrawingSize _viewport;

    private double _contentLeft;
    private double _contentTop;
    private double _originX;
    private double _originY;
    private double _scale = 1d;
    private bool _hasContent;

    private bool _dragging;
    private DrawingPointF _lastContentPoint;

    /// <summary>Creates a minimap with the adapter's dark palette.</summary>
    public VisualWorkflowMinimap()
    {
        IsHitTestVisible = true;
        Width = DefaultWidth;
        Height = DefaultHeight;
        Background = Color.FromArgb(0xD2, 0x1F, 0x1F, 0x1F);
        BorderBrush = Color.FromArgb(0xDC, 0x40, 0x40, 0x40);
        BorderThickness = new XamlThickness(1f);
        CornerRadius = 6f;
        Padding = new XamlThickness((float)PaddingValue);
        NodeBrush = Color.FromArgb(0xDC, 0x6E, 0x8B, 0xFF);
        ViewportStroke = Color.FromArgb(0xF0, 0xE7, 0xE7, 0xE7);
    }

    /// <summary>The colour of a node thumbnail.</summary>
    public Color NodeBrush { get; set; }

    /// <summary>The colour of the viewport outline.</summary>
    public Color ViewportStroke { get; set; }

    /// <summary>Whether the minimap is shown.</summary>
    public bool IsMinimapVisible
    {
        get => IsVisible;
        set
        {
            if (IsVisible == value)
            {
                return;
            }

            IsVisible = value;
            InvalidateVisual();
        }
    }

    internal void UpdateFrame(
        ITreeComponent? tree,
        DrawingPointF pan,
        DrawingSize viewport,
        IWorkflowSurfaceScroller? scroller)
    {
        _tree = tree;
        _pan = pan;
        _viewport = viewport;
        _scroller = scroller;
        InvalidateVisual();
    }

    /// <inheritdoc />
    protected override DrawingSize MeasureOverride(DrawingSize availableSize)
        => new((int)Math.Ceiling(Width), (int)Math.Ceiling(Height));

    /// <inheritdoc />
    protected override void OnRender(Graphics graphics)
    {
        if (_tree is null || !IsVisible)
        {
            return;
        }

        var padding = Padding;
        var drawWidth = Bounds.Width - padding.Horizontal;
        var drawHeight = Bounds.Height - padding.Vertical;
        if (drawWidth <= 0f || drawHeight <= 0f)
        {
            return;
        }

        if (!ComputeFit(drawWidth, drawHeight))
        {
            return;
        }

        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var thumb = new SolidBrush(NodeBrush);
        foreach (var node in _tree.Nodes)
        {
            var anchor = node.Anchor;
            var size = node.Size;
            if (!IsFinite(anchor.Horizontal, anchor.Vertical) || size.Width <= 0d || size.Height <= 0d)
            {
                continue;
            }

            var (x, y) = WorkflowSurfaceMath.MinimapLocal(
                anchor.Horizontal, anchor.Vertical, _contentLeft, _contentTop, _originX, _originY, _scale);
            var width = Math.Max(MinThumbSize, WorkflowSurfaceMath.MinThumbSize(size.Width, _scale, MinThumbSize));
            var height = Math.Max(MinThumbSize, WorkflowSurfaceMath.MinThumbSize(size.Height, _scale, MinThumbSize));
            graphics.FillRectangle(
                thumb,
                (float)(padding.Left + x),
                (float)(padding.Top + y),
                (float)width,
                (float)height);
        }

        var (vx, vy, vw, vh) = WorkflowSurfaceMath.MinimapViewportRect(
            _originX, _originY, _scale,
            -_pan.X, -_pan.Y, _viewport.Width, _viewport.Height,
            _contentLeft, _contentTop,
            drawWidth, drawHeight, 4d);

        using var viewportPen = new Pen(ViewportStroke, 1.2f);
        graphics.DrawRectangle(
            viewportPen,
            (float)(padding.Left + vx),
            (float)(padding.Top + vy),
            (float)vw,
            (float)vh);
    }

    /// <inheritdoc />
    protected override void OnPointerPressed(VisualPointerEventArgs args)
    {
        base.OnPointerPressed(args);
        if (args.Button != MouseButtons.Left || !_hasContent)
        {
            return;
        }

        _dragging = true;
        _lastContentPoint = ToContentPoint(args.Position);
    }

    /// <inheritdoc />
    protected override void OnPointerMoved(VisualPointerEventArgs args)
    {
        base.OnPointerMoved(args);
        if (!_dragging || (args.Buttons & MouseButtons.Left) == 0 || !_hasContent)
        {
            _dragging = false;
            return;
        }

        var current = ToContentPoint(args.Position);
        var deltaX = current.X - _lastContentPoint.X;
        var deltaY = current.Y - _lastContentPoint.Y;
        _lastContentPoint = current;
        if (Math.Abs(deltaX) < 0.001f && Math.Abs(deltaY) < 0.001f)
        {
            return;
        }

        // 拖动缩略图向右下 = 视口在内容里向右下走 = 画布 pan 反向移动。
        _scroller?.ScrollBy(
            -deltaX / VisualWorkflowSurface.WheelScrollStep,
            -deltaY / VisualWorkflowSurface.WheelScrollStep);
    }

    /// <inheritdoc />
    protected override void OnPointerReleased(VisualPointerEventArgs args)
    {
        base.OnPointerReleased(args);
        _dragging = false;
    }

    private bool ComputeFit(float drawWidth, float drawHeight)
    {
        var bounds = default(WorkflowBounds);
        var first = true;
        foreach (var node in _tree!.Nodes)
        {
            var anchor = node.Anchor;
            var size = node.Size;
            if (!IsFinite(anchor.Horizontal, anchor.Vertical) || size.Width <= 0d || size.Height <= 0d)
            {
                continue;
            }

            var next = WorkflowBounds.FromNode(anchor.Horizontal, anchor.Vertical, size.Width, size.Height);
            bounds = first ? next : WorkflowBounds.Union(bounds, next);
            first = false;
        }

        if (first || bounds.IsEmpty)
        {
            _hasContent = false;
            return false;
        }

        var fit = WorkflowSurfaceMath.MinimapFit(bounds.Width, bounds.Height, drawWidth, drawHeight, 4d);
        _contentLeft = bounds.Left;
        _contentTop = bounds.Top;
        _originX = fit.OriginX;
        _originY = fit.OriginY;
        _scale = fit.Scale <= 0d ? 1d : fit.Scale;
        _hasContent = true;
        return true;
    }

    private DrawingPointF ToContentPoint(DrawingPointF surfacePoint)
    {
        var local = PointFromSurface(surfacePoint);
        var (x, y) = WorkflowSurfaceMath.MinimapToCanvasLocal(
            local.X - Padding.Left,
            local.Y - Padding.Top,
            _originX, _originY, _scale,
            _contentLeft, _contentTop);
        return new DrawingPointF((float)x, (float)y);
    }

    private static bool IsFinite(double x, double y)
        => !double.IsNaN(x) && !double.IsNaN(y) && !double.IsInfinity(x) && !double.IsInfinity(y);
}
