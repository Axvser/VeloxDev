using System;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

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
/// the node and link views so they float above them. Derive from it for the palette and the spacing — the grid
/// line maths, the tick layout and the label formatting are the same in every host.
/// </para>
/// <para>
/// Brushes and pens are cached and rebuilt when a colour changes, because a surface redraws these every frame.
/// </para>
/// </remarks>
public class WorkflowGridDecorator : Grid, IWorkflowGridDecorator
{
    /// <summary>Default ruler band thickness, in pixels; the value <see cref="RulerThickness"/> defaults to.</summary>
    public const double DefaultRulerThickness = 36;

    // 两层自绘子元素：底层填表面 + 世界网格，顶层画浮动标尺带。内容子元素由模板插在两者之间（ZIndex 100）。
    private readonly GridLayer _gridLayer;
    private readonly RulerLayer _rulerLayer;

    /// <summary>Identifies the <see cref="RulerThickness"/> dependency property.</summary>
    public static readonly DependencyProperty RulerThicknessProperty = DependencyProperty.Register(
        nameof(RulerThickness), typeof(double), typeof(WorkflowGridDecorator),
        new PropertyMetadata(DefaultRulerThickness, OnVisualPropertyChanged));

    /// <summary>Identifies the <see cref="ScrollOffsetX"/> dependency property.</summary>
    public static readonly DependencyProperty ScrollOffsetXProperty = DependencyProperty.Register(
        nameof(ScrollOffsetX), typeof(double), typeof(WorkflowGridDecorator),
        new PropertyMetadata(0d, OnVisualPropertyChanged));

    /// <summary>Identifies the <see cref="ScrollOffsetY"/> dependency property.</summary>
    public static readonly DependencyProperty ScrollOffsetYProperty = DependencyProperty.Register(
        nameof(ScrollOffsetY), typeof(double), typeof(WorkflowGridDecorator),
        new PropertyMetadata(0d, OnVisualPropertyChanged));

    /// <summary>Identifies the <see cref="ContentOffsetX"/> dependency property.</summary>
    public static readonly DependencyProperty ContentOffsetXProperty = DependencyProperty.Register(
        nameof(ContentOffsetX), typeof(double), typeof(WorkflowGridDecorator),
        new PropertyMetadata(0d, OnVisualPropertyChanged));

    /// <summary>Identifies the <see cref="ContentOffsetY"/> dependency property.</summary>
    public static readonly DependencyProperty ContentOffsetYProperty = DependencyProperty.Register(
        nameof(ContentOffsetY), typeof(double), typeof(WorkflowGridDecorator),
        new PropertyMetadata(0d, OnVisualPropertyChanged));

    /// <summary>Identifies the <see cref="GridStep"/> dependency property.</summary>
    public static readonly DependencyProperty GridStepProperty = DependencyProperty.Register(
        nameof(GridStep), typeof(double), typeof(WorkflowGridDecorator),
        new PropertyMetadata(40d, OnVisualPropertyChanged));

    /// <summary>Identifies the <see cref="MajorLineEvery"/> dependency property.</summary>
    public static readonly DependencyProperty MajorLineEveryProperty = DependencyProperty.Register(
        nameof(MajorLineEvery), typeof(int), typeof(WorkflowGridDecorator),
        new PropertyMetadata(5, OnVisualPropertyChanged));

    /// <summary>Identifies the <see cref="MinorGridColor"/> dependency property.</summary>
    public static readonly DependencyProperty MinorGridColorProperty = DependencyProperty.Register(
        nameof(MinorGridColor), typeof(Color), typeof(WorkflowGridDecorator),
        new PropertyMetadata(Color.FromArgb(0xFF, 0x2A, 0x2D, 0x2E), OnColorChanged));

    /// <summary>Identifies the <see cref="MajorGridColor"/> dependency property.</summary>
    public static readonly DependencyProperty MajorGridColorProperty = DependencyProperty.Register(
        nameof(MajorGridColor), typeof(Color), typeof(WorkflowGridDecorator),
        new PropertyMetadata(Color.FromArgb(0xFF, 0x3A, 0x3D, 0x40), OnColorChanged));

    /// <summary>Identifies the <see cref="AxisColor"/> dependency property.</summary>
    public static readonly DependencyProperty AxisColorProperty = DependencyProperty.Register(
        nameof(AxisColor), typeof(Color), typeof(WorkflowGridDecorator),
        new PropertyMetadata(Color.FromArgb(0xFF, 0x4D, 0x4D, 0x4D), OnColorChanged));

    /// <summary>Identifies the <see cref="RulerBackground"/> dependency property.</summary>
    public static readonly DependencyProperty RulerBackgroundProperty = DependencyProperty.Register(
        nameof(RulerBackground), typeof(Color), typeof(WorkflowGridDecorator),
        new PropertyMetadata(Color.FromArgb(0xC8, 0x25, 0x25, 0x26), OnColorChanged));

    /// <summary>Identifies the <see cref="RulerLabelColor"/> dependency property.</summary>
    public static readonly DependencyProperty RulerLabelColorProperty = DependencyProperty.Register(
        nameof(RulerLabelColor), typeof(Color), typeof(WorkflowGridDecorator),
        new PropertyMetadata(Color.FromArgb(0xFF, 0x88, 0x88, 0x88), OnColorChanged));

    /// <summary>Identifies the <see cref="RulerTickColor"/> dependency property.</summary>
    public static readonly DependencyProperty RulerTickColorProperty = DependencyProperty.Register(
        nameof(RulerTickColor), typeof(Color), typeof(WorkflowGridDecorator),
        new PropertyMetadata(Color.FromArgb(0xFF, 0x55, 0x55, 0x55), OnColorChanged));

    /// <summary>Identifies the <see cref="RulerDividerColor"/> dependency property.</summary>
    public static readonly DependencyProperty RulerDividerColorProperty = DependencyProperty.Register(
        nameof(RulerDividerColor), typeof(Color), typeof(WorkflowGridDecorator),
        new PropertyMetadata(Color.FromArgb(0xFF, 0x3A, 0x3D, 0x40), OnColorChanged));

    /// <summary>Identifies the <see cref="SurfaceBackground"/> dependency property.</summary>
    public static readonly DependencyProperty SurfaceBackgroundProperty = DependencyProperty.Register(
        nameof(SurfaceBackground), typeof(Color), typeof(WorkflowGridDecorator),
        new PropertyMetadata(Color.FromArgb(0xFF, 0x1E, 0x1E, 0x1E), OnColorChanged));

    /// <summary>Initializes the decorator and its two self-drawn layers.</summary>
    public WorkflowGridDecorator()
    {
        ClipToBounds = true;
        SnapsToDevicePixels = true;

        _gridLayer = new GridLayer(this) { IsHitTestVisible = false };
        _rulerLayer = new RulerLayer(this) { IsHitTestVisible = false };

        // 顶层标尺必须压过模板插进来的内容子元素，所以给它一个高 ZIndex；底层网格与内容默认同层，
        // 靠「先加的先画」落在内容之下。
        Panel.SetZIndex(_rulerLayer, 100);

        Children.Add(_gridLayer);
        Children.Add(_rulerLayer);

        SizeChanged += (_, _) =>
        {
            _gridLayer.InvalidateVisual();
            _rulerLayer.InvalidateVisual();
        };
    }

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
    public double GridStep
    {
        get => Read(GridStepProperty, 40d);
        set => SetValue(GridStepProperty, value);
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
    public double MajorStep => Math.Max(1, GridStep) * Math.Max(1, MajorLineEvery);

    /// <inheritdoc />
    public double RulerBand => RulerThickness;

    // 本层是视口局部坐标系（原点在装饰器左上角），而 DrawGrid/DrawRulers 收的是画布系参数：
    // 画布系 x = 世界 + 内容偏移 + 标尺reserve，视口局部 x = 画布系 x − 滚动偏移 ——
    // 把两者折进 origin，滚动就由调用方以 scroll=0 传入，原有数学一行不用改。
    private double LayerOriginX => ContentOffsetX + RulerThickness - ScrollOffsetX;

    private double LayerOriginY => ContentOffsetY + RulerThickness - ScrollOffsetY;

    // 值类型 DP 都有非 null 默认值，取回来必是该类型；`GetValue` 返回 `object?`，直接拆箱会触 CS8605，
    // 所以统一走这个带回退的读取器（与 WorkflowMinimapOverlay 同形）。
    private T Read<T>(DependencyProperty property, T fallback) where T : struct
        => GetValue(property) is T value ? value : fallback;

    private static void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is WorkflowGridDecorator decorator)
        {
            decorator._gridLayer.InvalidateVisual();
            decorator._rulerLayer.InvalidateVisual();
        }
    }

    // 颜色走单独的回调：只有颜色变才重建缓存笔刷。滚动/内容偏移每帧都在变，走上面那条，不碰缓存。
    private static void OnColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is WorkflowGridDecorator decorator)
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

    // 底层第一笔：整块表面填充，网格画在它上面。
    private void DrawSurface(DrawingContext dc, double width, double height)
    {
        EnsureResources();
        dc.DrawRectangle(_surfaceBrush!, null, new Rect(0, 0, width, height));
    }

    // 世界网格。origin 是画布系原点的视口局部投影（见 LayerOriginX）；width/height 是视口尺寸。
    private void DrawGrid(DrawingContext dc, double originX, double originY, double width, double height)
    {
        EnsureResources();

        double step = Math.Max(1, GridStep);
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

    // 顶层标尺带。scrollX/scrollY 传 0：视口局部坐标系里视口左上角就是原点，滚动已折进 origin。
    // viewportWidth/viewportHeight 是视口尺寸。
    private void DrawRulers(
        DrawingContext dc,
        double originX, double originY,
        double scrollX, double scrollY,
        double viewportWidth, double viewportHeight)
    {
        EnsureResources();

        double ruler = Math.Max(0, RulerThickness);
        double step = Math.Max(1, GridStep);
        double majorStep = step * Math.Max(1, MajorLineEvery);

        var bg = new SolidColorBrush(RulerBackground);
        dc.DrawRectangle(bg, null, new Rect(scrollX, scrollY, viewportWidth, ruler));
        dc.DrawRectangle(bg, null, new Rect(scrollX, scrollY, ruler, viewportHeight));
        dc.DrawLine(_dividerPen!, new Point(scrollX + ruler, scrollY), new Point(scrollX + ruler, scrollY + viewportHeight));
        dc.DrawLine(_dividerPen!, new Point(scrollX, scrollY + ruler), new Point(scrollX + viewportWidth, scrollY + ruler));

        // 上标尺：刻度落在穿过视口的世界网格线上，画布 x = world + originX。
        double worldLeft = WorkflowSurfaceMath.GridWorldLeft(scrollX, originX);
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

        // 左标尺：刻度落在穿过视口的世界网格线上，画布 y = world + originY。
        double worldTop = WorkflowSurfaceMath.GridWorldTop(scrollY, originY);
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

    // 底层：表面填充 + 世界网格。撑满装饰器，画到标尺带下面。
    private sealed class GridLayer(WorkflowGridDecorator owner) : FrameworkElement
    {
        // Size 必须全限定：Core 的 VeloxDev.WorkflowSystem.Size 是外层命名空间的类型，会盖过 using 进来的 Jalium.UI.Size。
        protected override Jalium.UI.Size MeasureOverride(Jalium.UI.Size availableSize) => availableSize;

        protected override void OnRender(DrawingContext context)
        {
            var size = RenderSize;
            if (size.Width <= 0 || size.Height <= 0) return;

            owner.DrawSurface(context, size.Width, size.Height);
            owner.DrawGrid(context, owner.LayerOriginX, owner.LayerOriginY, size.Width, size.Height);
        }
    }

    // 顶层：浮动标尺带，最后画以压在内容之上。
    private sealed class RulerLayer(WorkflowGridDecorator owner) : FrameworkElement
    {
        // Size 必须全限定，理由同 GridLayer。
        protected override Jalium.UI.Size MeasureOverride(Jalium.UI.Size availableSize) => availableSize;

        protected override void OnRender(DrawingContext context)
        {
            var size = RenderSize;
            if (size.Width <= 0 || size.Height <= 0) return;

            owner.DrawRulers(context, owner.LayerOriginX, owner.LayerOriginY, 0, 0, size.Width, size.Height);
        }
    }
}
