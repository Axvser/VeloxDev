using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using Easel;
using Easel.Visuals;
using VeloxDev.WorkflowSystem.Platform;
using VeloxDev.WorkflowSystem.ViewHosting;
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
/// Tree subscription, content/viewport mapping, placement and drag math live in the Core
/// <see cref="MinimapPipeline"/>. This class keeps the palette, the platform drawing, the surface-pushed offsets
/// and the pointer handlers that feed the input layer.
/// </para>
/// </remarks>
public class WorkflowMinimapOverlay : VisualElement, IWorkflowMinimapOverlay, IWorkflowMinimapScrollSource
{
    private const double DefaultWidth = 200d;
    private const double DefaultHeight = 140d;
    private const double MinimumSize = 20d;
    private const double ContentPadding = 4d;
    private const double MinimumThumbSize = 2d;

    private Color _minimapBackground = Color.FromArgb(0x33, 0x1E, 0x1E, 0x1E);
    private Color _minimapBorderBrush = Color.FromArgb(0xDC, 0x94, 0xA3, 0xB8);
    private Color _nodeBrush = Color.FromArgb(0xDC, 0x38, 0xBD, 0xF8);
    private Color _viewportStroke = Color.FromArgb(0xF0, 0xFF, 0xFF, 0xFF);
    private double _minimapWidth = DefaultWidth;
    private double _minimapHeight = DefaultHeight;
    private double _minimapMargin = 12d;
    private double _availableRight;
    private double _availableTop;
    private double _availableBottom;

    private readonly MinimapInputHost _inputHost;
    private readonly MinimapPipeline _pipeline;

    /// <summary>Creates the overlay as a full-surface layer.</summary>
    public WorkflowMinimapOverlay()
    {
        IsHitTestVisible = true;
        ClipToBounds = true;

        _inputHost = new MinimapInputHost(this);
        _pipeline = new MinimapPipeline(new MinimapRenderer(this), new MinimapViewportHost(this), _inputHost);
        _pipeline.RenderRequested += OnPipelineRenderRequested;
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
    public ITreeComponent? WorkflowTree
    {
        get => _pipeline.Tree;
        set => _pipeline.Tree = value;
    }

    /// <inheritdoc />
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool IsMinimapVisible
    {
        get => _pipeline.IsVisible;
        set { _pipeline.IsVisible = value; InvalidateVisual(); }
    }

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

        RefreshPipelineSettings();
        return _pipeline.HitTest(point.X, point.Y);
    }

    /// <inheritdoc />
    protected override void OnRender(Graphics graphics)
    {
        if (!IsMinimapVisible || Bounds.Width <= 0f || Bounds.Height <= 0f)
        {
            return;
        }

        RefreshPipelineSettings();
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        _pipeline.Render(graphics);
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

    /// <summary>Hands one pass to the overlay's draw hook; used by <see cref="MinimapRenderer"/>.</summary>
    internal void DrawMinimapSurface(Graphics graphics, MinimapDrawContext context) => DrawMinimap(graphics, context);

    /// <summary>Applies a navigation scroll in canvas-local space; used by <see cref="MinimapViewportHost"/>.</summary>
    internal void ApplyMinimapScroll(double scrollX, double scrollY)
    {
        ScrollOffsetX = scrollX;
        ScrollOffsetY = scrollY;
        InvalidateVisual();
        ViewportScrollRequested?.Invoke(scrollX, scrollY);
    }

    // 放置/绘制/命中/拖拽共用同一组输入：请求尺寸 + 可用角落（右侧让开竖条、顶部让开标尺、底部让开横条）。
    private void RefreshPipelineSettings()
    {
        var width = Math.Max(0d, Bounds.Width);
        var height = Math.Max(0d, Bounds.Height);
        var top = Math.Max(0d, AvailableTop);

        _pipeline.Metrics = new MinimapMetrics
        {
            Width = MinimapWidth,
            Height = MinimapHeight,
            MinSize = MinimumSize,
            Padding = ContentPadding,
            NodeCornerRadius = 0d,
            MinimapCornerRadius = 0d,
            MinimumThumbSize = MinimumThumbSize,
            Available = new SurfaceAvailableArea
            {
                Right = AvailableRight > 0d ? AvailableRight : width,
                Top = top,
                Bottom = AvailableBottom > top ? AvailableBottom : height,
                Margin = MinimapMargin,
            },
        };
    }

    private void OnPipelineRenderRequested(object? sender, EventArgs e)
    {
        // 节点/插槽的属性通知可能来自后台线程；InvalidateVisual 必须回到 UI 线程。
        var surface = Surface;
        if (surface is { IsHandleCreated: true, IsDisposed: false } && surface.InvokeRequired)
        {
            try
            {
                surface.BeginInvoke(new Action(InvalidateVisual));
                return;
            }
            catch (InvalidOperationException)
            {
            }
        }

        InvalidateVisual();
    }

    /// <inheritdoc />
    protected override void OnPointerPressed(VisualPointerEventArgs args)
    {
        if (args.Button != System.Windows.Forms.MouseButtons.Left)
        {
            return;
        }

        RefreshPipelineSettings();
        var point = args.GetPosition(this);
        _inputHost.PointerPosition = (point.X, point.Y);
        _inputHost.IsPressed = true;
        if (_pipeline.PointerPressed())
        {
            args.Handled = true;
        }
    }

    /// <inheritdoc />
    protected override void OnPointerMoved(VisualPointerEventArgs args)
    {
        if (!_pipeline.IsDragging)
        {
            return;
        }

        var point = args.GetPosition(this);
        _inputHost.PointerPosition = (point.X, point.Y);
        _pipeline.PointerMoved();
        args.Handled = true;
    }

    /// <inheritdoc />
    protected override void OnPointerReleased(VisualPointerEventArgs args)
    {
        _inputHost.IsPressed = false;
        if (!_pipeline.IsDragging)
        {
            return;
        }

        _pipeline.PointerReleased();
        args.Handled = true;
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // 流水线持有树的属性订阅；覆盖层释放时要一并退订，否则被移除的树仍被这张视图钉住。
            _pipeline.RenderRequested -= OnPipelineRenderRequested;
            _pipeline.Dispose();
        }

        base.Dispose(disposing);
    }
}
