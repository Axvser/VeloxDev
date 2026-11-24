// VeloxDev customization: The workflow grid. The whole grid lives here — the world-coordinate maths,
// the tick layout and the label formatting — so this file is where its look and its spacing are decided.
using System;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using VeloxDev.WorkflowSystem;

namespace TemplateNamespace;

/// <summary>
/// A workflow-surface decorator that fills the surface, paints the world grid behind its content and the two
/// floating ruler bands above it, and exposes the scroll/content offsets the surface pushes every pass.
/// </summary>
/// <remarks>
/// <para>
/// It is a container, not a painter: the template nests the surface's scroll viewer inside it, so the content
/// child sits between two self-drawn, hit-test-transparent layers — a bottom layer that fills the surface and
/// draws the world grid, and a top layer that draws the translucent ruler bands fixed to the viewport.
/// </para>
/// <para>
/// The grid is world-aligned and scrolls with the canvas; the ruler bands are viewport-fixed and are drawn after
/// the node and link views so they float above them. Change the palette, the spacing or the line maths here —
/// the whole grid is this project's.
/// </para>
/// <para>
/// Brushes and pens are cached and rebuilt when a colour changes, because a surface redraws these every frame.
/// </para>
/// </remarks>
public sealed class TemplateClass : Grid, IWorkflowGridDecorator
{
    /// <summary>Default ruler band thickness, in pixels; the value <see cref="RulerThickness"/> defaults to.</summary>
    public const double DefaultRulerThickness = 36;

    // Two self-drawn layers: the grid behind the content and the ruler band above it.
    private readonly GridLayer _gridLayer;
    private readonly RulerLayer _rulerLayer;

    /// <summary>Identifies the <see cref="RulerThickness"/> dependency property.</summary>
    public static readonly DependencyProperty RulerThicknessProperty = DependencyProperty.Register(
        nameof(RulerThickness), typeof(double), typeof(TemplateClass),
        new PropertyMetadata(DefaultRulerThickness, OnVisualPropertyChanged));

    /// <summary>Identifies the <see cref="ScrollOffsetX"/> dependency property.</summary>
    public static readonly DependencyProperty ScrollOffsetXProperty = DependencyProperty.Register(
        nameof(ScrollOffsetX), typeof(double), typeof(TemplateClass),
        new PropertyMetadata(0d, OnVisualPropertyChanged));

    /// <summary>Identifies the <see cref="ScrollOffsetY"/> dependency property.</summary>
    public static readonly DependencyProperty ScrollOffsetYProperty = DependencyProperty.Register(
        nameof(ScrollOffsetY), typeof(double), typeof(TemplateClass),
        new PropertyMetadata(0d, OnVisualPropertyChanged));

    /// <summary>Identifies the <see cref="ContentOffsetX"/> dependency property.</summary>
    public static readonly DependencyProperty ContentOffsetXProperty = DependencyProperty.Register(
        nameof(ContentOffsetX), typeof(double), typeof(TemplateClass),
        new PropertyMetadata(0d, OnVisualPropertyChanged));

    /// <summary>Identifies the <see cref="ContentOffsetY"/> dependency property.</summary>
    public static readonly DependencyProperty ContentOffsetYProperty = DependencyProperty.Register(
        nameof(ContentOffsetY), typeof(double), typeof(TemplateClass),
        new PropertyMetadata(0d, OnVisualPropertyChanged));

    /// <summary>Identifies the <see cref="GridSpacing"/> dependency property.</summary>
    public static readonly DependencyProperty GridSpacingProperty = DependencyProperty.Register(
        nameof(GridSpacing), typeof(double), typeof(TemplateClass),
        new PropertyMetadata(40d, OnVisualPropertyChanged));

    /// <summary>Identifies the <see cref="MajorLineEvery"/> dependency property.</summary>
    public static readonly DependencyProperty MajorLineEveryProperty = DependencyProperty.Register(
        nameof(MajorLineEvery), typeof(int), typeof(TemplateClass),
        new PropertyMetadata(5, OnVisualPropertyChanged));

    /// <summary>Identifies the <see cref="MinorGridColor"/> dependency property.</summary>
    public static readonly DependencyProperty MinorGridColorProperty = DependencyProperty.Register(
        nameof(MinorGridColor), typeof(Color), typeof(TemplateClass),
        new PropertyMetadata(Color.FromArgb(0xFF, 0x2A, 0x2D, 0x2E), OnColorChanged));

    /// <summary>Identifies the <see cref="MajorGridColor"/> dependency property.</summary>
    public static readonly DependencyProperty MajorGridColorProperty = DependencyProperty.Register(
        nameof(MajorGridColor), typeof(Color), typeof(TemplateClass),
        new PropertyMetadata(Color.FromArgb(0xFF, 0x3A, 0x3D, 0x40), OnColorChanged));

    /// <summary>Identifies the <see cref="AxisColor"/> dependency property.</summary>
    public static readonly DependencyProperty AxisColorProperty = DependencyProperty.Register(
        nameof(AxisColor), typeof(Color), typeof(TemplateClass),
        new PropertyMetadata(Color.FromArgb(0xFF, 0x4D, 0x4D, 0x4D), OnColorChanged));

    /// <summary>Identifies the <see cref="RulerBackground"/> dependency property.</summary>
    public static readonly DependencyProperty RulerBackgroundProperty = DependencyProperty.Register(
        nameof(RulerBackground), typeof(Color), typeof(TemplateClass),
        new PropertyMetadata(Color.FromArgb(0xC8, 0x25, 0x25, 0x26), OnColorChanged));

    /// <summary>Identifies the <see cref="RulerLabelColor"/> dependency property.</summary>
    public static readonly DependencyProperty RulerLabelColorProperty = DependencyProperty.Register(
        nameof(RulerLabelColor), typeof(Color), typeof(TemplateClass),
        new PropertyMetadata(Color.FromArgb(0xFF, 0x88, 0x88, 0x88), OnColorChanged));

    /// <summary>Identifies the <see cref="RulerTickColor"/> dependency property.</summary>
    public static readonly DependencyProperty RulerTickColorProperty = DependencyProperty.Register(
        nameof(RulerTickColor), typeof(Color), typeof(TemplateClass),
        new PropertyMetadata(Color.FromArgb(0xFF, 0x55, 0x55, 0x55), OnColorChanged));

    /// <summary>Identifies the <see cref="RulerDividerColor"/> dependency property.</summary>
    public static readonly DependencyProperty RulerDividerColorProperty = DependencyProperty.Register(
        nameof(RulerDividerColor), typeof(Color), typeof(TemplateClass),
        new PropertyMetadata(Color.FromArgb(0xFF, 0x3A, 0x3D, 0x40), OnColorChanged));

    /// <summary>Identifies the <see cref="SurfaceBackground"/> dependency property.</summary>
    public static readonly DependencyProperty SurfaceBackgroundProperty = DependencyProperty.Register(
        nameof(SurfaceBackground), typeof(Color), typeof(TemplateClass),
        new PropertyMetadata(Color.FromArgb(0xFF, 0x1E, 0x1E, 0x1E), OnColorChanged));

    /// <summary>Initializes the decorator and its two self-drawn layers.</summary>
    public TemplateClass()
    {
        ClipToBounds = true;
        SnapsToDevicePixels = true;

        _gridLayer = new GridLayer(this) { IsHitTestVisible = false };
        _rulerLayer = new RulerLayer(this) { IsHitTestVisible = false };

        // The ruler layer must render above the content the template inserts.
        Panel.SetZIndex(_rulerLayer, 100);

        Children.Add(_gridLayer);
        Children.Add(_rulerLayer);

        MinorGridColor = ParseColor("TemplateMinorGridColor");
        MajorGridColor = ParseColor("TemplateMajorGridColor");
        AxisColor = ParseColor("TemplateAxisColor");
        RulerBackground = ParseColor("TemplateRulerBackground");
        RulerLabelColor = ParseColor("TemplateRulerLabelColor");
        RulerTickColor = ParseColor("TemplateRulerTickColor");
        RulerDividerColor = ParseColor("TemplateRulerDividerColor");
        SurfaceBackground = ParseColor("TemplateGridBackground");
        GridSpacing = TemplateGridSpacing;
        MajorLineEvery = TemplateMajorLineEvery;

        SizeChanged += (_, _) =>
        {
            _gridLayer.InvalidateVisual();
            _rulerLayer.InvalidateVisual();
        };
    }

    // A colour placeholder is always a valid string at generation time; the fallback keeps the unboxing
    // warning-free for a consumer who edits one by hand.
    private static Color ParseColor(string text)
        => ColorConverter.ConvertFromString(text) is Color color ? color : Color.FromArgb(0xFF, 0, 0, 0);

    /// <summary>Ruler band thickness, in pixels.</summary>
    public double RulerThickness
    {
        get => Read(RulerThicknessProperty, DefaultRulerThickness);
        set => SetValue(RulerThicknessProperty, value);
    }

    /// <summary>The surface scroll viewer's horizontal offset.</summary>
    public double ScrollOffsetX
    {
        get => Read(ScrollOffsetXProperty, 0d);
        set => SetValue(ScrollOffsetXProperty, value);
    }

    /// <summary>The surface scroll viewer's vertical offset.</summary>
    public double ScrollOffsetY
    {
        get => Read(ScrollOffsetYProperty, 0d);
        set => SetValue(ScrollOffsetYProperty, value);
    }

    /// <summary>The canvas content's horizontal offset (<c>CanvasLayout.ActualOffset.Horizontal</c>).</summary>
    public double ContentOffsetX
    {
        get => Read(ContentOffsetXProperty, 0d);
        set => SetValue(ContentOffsetXProperty, value);
    }

    /// <summary>The canvas content's vertical offset (<c>CanvasLayout.ActualOffset.Vertical</c>).</summary>
    public double ContentOffsetY
    {
        get => Read(ContentOffsetYProperty, 0d);
        set => SetValue(ContentOffsetYProperty, value);
    }

    /// <summary>World distance between minor grid lines, in pixels.</summary>
    public double GridSpacing
    {
        get => Read(GridSpacingProperty, 40d);
        set => SetValue(GridSpacingProperty, value);
    }

    /// <summary>How many minor lines make a major one. Values below one are treated as one.</summary>
    public int MajorLineEvery
    {
        get => Read(MajorLineEveryProperty, 5);
        set => SetValue(MajorLineEveryProperty, value);
    }

    /// <summary>Colour of the minor grid lines.</summary>
    public Color MinorGridColor
    {
        get => Read(MinorGridColorProperty, Color.FromArgb(0xFF, 0x2A, 0x2D, 0x2E));
        set => SetValue(MinorGridColorProperty, value);
    }

    /// <summary>Colour of the major grid lines.</summary>
    public Color MajorGridColor
    {
        get => Read(MajorGridColorProperty, Color.FromArgb(0xFF, 0x3A, 0x3D, 0x40));
        set => SetValue(MajorGridColorProperty, value);
    }

    /// <summary>Colour of the world axes.</summary>
    public Color AxisColor
    {
        get => Read(AxisColorProperty, Color.FromArgb(0xFF, 0x4D, 0x4D, 0x4D));
        set => SetValue(AxisColorProperty, value);
    }

    /// <summary>Fill of the two ruler bands.</summary>
    public Color RulerBackground
    {
        get => Read(RulerBackgroundProperty, Color.FromArgb(0xC8, 0x25, 0x25, 0x26));
        set => SetValue(RulerBackgroundProperty, value);
    }

    /// <summary>Colour of the ruler labels.</summary>
    public Color RulerLabelColor
    {
        get => Read(RulerLabelColorProperty, Color.FromArgb(0xFF, 0x88, 0x88, 0x88));
        set => SetValue(RulerLabelColorProperty, value);
    }

    /// <summary>Colour of the ruler ticks.</summary>
    public Color RulerTickColor
    {
        get => Read(RulerTickColorProperty, Color.FromArgb(0xFF, 0x55, 0x55, 0x55));
        set => SetValue(RulerTickColorProperty, value);
    }

    /// <summary>Colour of the lines dividing each band from the grid.</summary>
    public Color RulerDividerColor
    {
        get => Read(RulerDividerColorProperty, Color.FromArgb(0xFF, 0x3A, 0x3D, 0x40));
        set => SetValue(RulerDividerColorProperty, value);
    }

    /// <summary>Fill painted behind the grid and the content.</summary>
    public Color SurfaceBackground
    {
        get => Read(SurfaceBackgroundProperty, Color.FromArgb(0xFF, 0x1E, 0x1E, 0x1E));
        set => SetValue(SurfaceBackgroundProperty, value);
    }

    /// <summary>World distance between major grid lines.</summary>
    public double MajorStep => Math.Max(1, GridSpacing) * Math.Max(1, MajorLineEvery);

    /// <inheritdoc />
    public double RulerBand => RulerThickness;

    // Folds the scroll offset into the canvas-local origin so the draw code keeps one coordinate system.
    private double LayerOriginX => ContentOffsetX + RulerThickness - ScrollOffsetX;

    private double LayerOriginY => ContentOffsetY + RulerThickness - ScrollOffsetY;

    // DP reads go through this fallback reader to avoid unboxing null.
    private T Read<T>(DependencyProperty property, T fallback) where T : struct
        => GetValue(property) is T value ? value : fallback;

    private static void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TemplateClass decorator)
        {
            decorator._gridLayer.InvalidateVisual();
            decorator._rulerLayer.InvalidateVisual();
        }
    }

    // Only colour changes rebuild the cached pens; offsets change every frame and reuse them.
    private static void OnColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TemplateClass decorator)
        {
            decorator.InvalidateResources();
            decorator._gridLayer.InvalidateVisual();
            decorator._rulerLayer.InvalidateVisual();
        }
    }

    private void InvalidateResources()
    {
        _minorPen = _majorPen = _axisPen = _tickPen = _dividerPen = null;
        _rulerLabelBrush = null;
        _surfaceBrush = null;
    }

    private void EnsureResources()
    {
        if (_minorPen is not null) return;

        _minorPen = new Pen(new SolidColorBrush(MinorGridColor), 1);
        _majorPen = new Pen(new SolidColorBrush(MajorGridColor), 1);
        _axisPen = new Pen(new SolidColorBrush(AxisColor), 1.2);
        _tickPen = new Pen(new SolidColorBrush(RulerTickColor), 1);
        _dividerPen = new Pen(new SolidColorBrush(RulerDividerColor), 1);
        _rulerLabelBrush = new SolidColorBrush(RulerLabelColor);
        _surfaceBrush = new SolidColorBrush(SurfaceBackground);
    }

    private Pen? _minorPen, _majorPen, _axisPen, _tickPen, _dividerPen;
    private SolidColorBrush? _rulerLabelBrush;
    private SolidColorBrush? _surfaceBrush;

    // Surface fill first; the grid draws on top.
    private void DrawSurface(DrawingContext dc, double width, double height)
    {
        EnsureResources();
        dc.DrawRectangle(_surfaceBrush!, null, new Rect(0, 0, width, height));
    }

    // World grid; origin is the viewport-local projection of the canvas origin.
    private void DrawGrid(DrawingContext dc, double originX, double originY, double width, double height)
    {
        EnsureResources();

        double step = Math.Max(1, GridSpacing);
        double majorStep = step * Math.Max(1, MajorLineEvery);

        double worldLeft = -originX;
        double worldRight = worldLeft + width;
        for (double g = WorkflowSurfaceMath.GridFirstLine(worldLeft, step); g <= worldRight; g += step)
        {
            double x = g + originX;
            Pen pen = g == 0 ? _axisPen! : (Math.Abs(g % majorStep) < 0.001 ? _majorPen! : _minorPen!);
            dc.DrawLine(pen, new Point(x, 0), new Point(x, height));
        }

        double worldTop = -originY;
        double worldBottom = worldTop + height;
        for (double g = WorkflowSurfaceMath.GridFirstLine(worldTop, step); g <= worldBottom; g += step)
        {
            double y = g + originY;
            Pen pen = g == 0 ? _axisPen! : (Math.Abs(g % majorStep) < 0.001 ? _majorPen! : _minorPen!);
            dc.DrawLine(pen, new Point(0, y), new Point(width, y));
        }
    }

    // Ruler band; scroll is already folded into origin, so scrollX/scrollY stay 0.
    private void DrawRulers(
        DrawingContext dc,
        double originX, double originY,
        double scrollX, double scrollY,
        double viewportWidth, double viewportHeight)
    {
        EnsureResources();

        double ruler = Math.Max(0, RulerThickness);
        double step = Math.Max(1, GridSpacing);
        double majorStep = step * Math.Max(1, MajorLineEvery);

        var bg = new SolidColorBrush(RulerBackground);
        dc.DrawRectangle(bg, null, new Rect(scrollX, scrollY, viewportWidth, ruler));
        dc.DrawRectangle(bg, null, new Rect(scrollX, scrollY, ruler, viewportHeight));
        dc.DrawLine(_dividerPen!, new Point(scrollX + ruler, scrollY), new Point(scrollX + ruler, scrollY + viewportHeight));
        dc.DrawLine(_dividerPen!, new Point(scrollX, scrollY + ruler), new Point(scrollX + viewportWidth, scrollY + ruler));

        // Top ruler ticks at the world grid lines crossing the viewport.
        double worldLeft = WorkflowSurfaceMath.GridCanvasLeft(scrollX, originX);
        for (double g = WorkflowSurfaceMath.GridFirstLine(worldLeft, step); g + originX <= scrollX + viewportWidth; g += step)
        {
            double x = g + originX;
            if (x < scrollX + ruler) continue;
            bool major = IsMajor(g, majorStep);
            double tick = major ? ruler - 6 : Math.Max(6, ruler * 0.35);
            Pen pen = IsNearZero(g) ? _axisPen! : _tickPen!;
            dc.DrawLine(pen, new Point(x, scrollY + ruler), new Point(x, scrollY + ruler - tick));
            if (major)
            {
                var label = new FormattedText(Format(g), "Segoe UI", 13) { Foreground = _rulerLabelBrush! };
                TextMeasurement.MeasureText(label);
                dc.DrawText(label, new Point(x + 3, scrollY + 2));
            }
        }

        // Left ruler ticks at the world grid lines crossing the viewport.
        double worldTop = WorkflowSurfaceMath.GridCanvasTop(scrollY, originY);
        for (double g = WorkflowSurfaceMath.GridFirstLine(worldTop, step); g + originY <= scrollY + viewportHeight; g += step)
        {
            double y = g + originY;
            if (y < scrollY + ruler) continue;
            bool major = IsMajor(g, majorStep);
            double tick = major ? ruler - 6 : Math.Max(6, ruler * 0.35);
            Pen pen = IsNearZero(g) ? _axisPen! : _tickPen!;
            dc.DrawLine(pen, new Point(scrollX + ruler, y), new Point(scrollX + ruler - tick, y));
            if (major)
            {
                var label = new FormattedText(Format(g), "Segoe UI", 13) { Foreground = _rulerLabelBrush! };
                TextMeasurement.MeasureText(label);
                dc.DrawText(label, new Point(scrollX + 3, y + 2));
            }
        }
    }

    private static bool IsMajor(double g, double majorStep) => Math.Abs(g % majorStep) < 0.001;

    private static bool IsNearZero(double g) => Math.Abs(g) < 0.001;

    private static string Format(double value)
    {
        double abs = Math.Abs(value);
        if (abs < 10000) return Math.Round(value).ToString();
        if (abs < 1000000) return Math.Round(value / 1000.0, 1).ToString() + "K";
        return Math.Round(value / 1000000.0, 1).ToString() + "M";
    }

    // Bottom layer: surface fill + world grid, under the ruler band.
    private sealed class GridLayer(TemplateClass owner) : FrameworkElement
    {
        // Fully qualify Size: Core's Size shadows Jalium.UI.Size here.
        protected override Jalium.UI.Size MeasureOverride(Jalium.UI.Size availableSize) => availableSize;

        protected override void OnRender(DrawingContext context)
        {
            var size = RenderSize;
            if (size.Width <= 0 || size.Height <= 0) return;

            owner.DrawSurface(context, size.Width, size.Height);
            owner.DrawGrid(context, owner.LayerOriginX, owner.LayerOriginY, size.Width, size.Height);
        }
    }

    // Top layer: the floating ruler band, drawn last.
    private sealed class RulerLayer(TemplateClass owner) : FrameworkElement
    {
        // Fully qualify Size for the same reason as GridLayer.
        protected override Jalium.UI.Size MeasureOverride(Jalium.UI.Size availableSize) => availableSize;

        protected override void OnRender(DrawingContext context)
        {
            var size = RenderSize;
            if (size.Width <= 0 || size.Height <= 0) return;

            owner.DrawRulers(context, owner.LayerOriginX, owner.LayerOriginY, 0, 0, size.Width, size.Height);
        }
    }
}
