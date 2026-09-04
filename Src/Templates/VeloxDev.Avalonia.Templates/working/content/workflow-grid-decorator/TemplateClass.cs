using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using System;
using System.Globalization;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.AttachedBehaviors;
using Size = Avalonia.Size;

namespace TemplateNamespace;

/// <summary>
/// A workflow surface decorator that draws the world grid and floating translucent rulers.
/// Content (the scroll viewer + canvas) fills the whole viewport; the world canvas is
/// translated by <see cref="RulerThickness"/> so the world origin stays at the ruler-band
/// edge while content can still scroll under the translucent bands (the Jalium floating
/// ruler model). The decorator layers, bottom to top: surface + grid, the content child,
/// and the ruler overlay (topmost, hit-test transparent).
/// </summary>
public sealed class TemplateClass : Panel, IWorkflowGridDecorator
{
    private const double MajorLineEpsilon = 0.001;

    private static readonly IBrush SurfaceBackgroundBrush = new ImmutableSolidColorBrush(Color.Parse("TemplateGridBackground"));
    private static readonly IBrush RulerBackgroundBrush = new ImmutableSolidColorBrush(Color.Parse("TemplateRulerBackground"));
    private static readonly IBrush LabelBrush = new ImmutableSolidColorBrush(Color.Parse("TemplateRulerLabelColor"));
    private static readonly Pen MinorGridPen = new(new ImmutableSolidColorBrush(Color.Parse("TemplateMinorGridColor")), 1);
    private static readonly Pen MajorGridPen = new(new ImmutableSolidColorBrush(Color.Parse("TemplateMajorGridColor")), 1);
    private static readonly Pen AxisPen = new(new ImmutableSolidColorBrush(Color.Parse("TemplateAxisColor")), 1.2);
    private static readonly Pen TickPen = new(new ImmutableSolidColorBrush(Color.Parse("TemplateRulerTickColor")), 1);
    private static readonly Pen DividerPen = new(new ImmutableSolidColorBrush(Color.Parse("TemplateRulerDividerColor")), 1);

    private readonly GridLayer _gridLayer;
    private readonly RulerLayer _rulerLayer;

    public static readonly StyledProperty<double> RulerThicknessProperty =
        AvaloniaProperty.Register<TemplateClass, double>(nameof(RulerThickness), 28d);

    public static readonly StyledProperty<double> GridSpacingProperty =
        AvaloniaProperty.Register<TemplateClass, double>(nameof(GridSpacing), TemplateGridSpacing);

    public static readonly StyledProperty<int> MajorLineEveryProperty =
        AvaloniaProperty.Register<TemplateClass, int>(nameof(MajorLineEvery), TemplateMajorLineEvery);

    public static readonly StyledProperty<double> ScrollOffsetXProperty =
        AvaloniaProperty.Register<TemplateClass, double>(nameof(ScrollOffsetX));

    public static readonly StyledProperty<double> ScrollOffsetYProperty =
        AvaloniaProperty.Register<TemplateClass, double>(nameof(ScrollOffsetY));

    public static readonly StyledProperty<double> ContentOffsetXProperty =
        AvaloniaProperty.Register<TemplateClass, double>(nameof(ContentOffsetX));

    public static readonly StyledProperty<double> ContentOffsetYProperty =
        AvaloniaProperty.Register<TemplateClass, double>(nameof(ContentOffsetY));

    static TemplateClass()
    {
        RulerThicknessProperty.Changed.AddClassHandler<TemplateClass>(OnVisualPropertyChanged);
        GridSpacingProperty.Changed.AddClassHandler<TemplateClass>(OnVisualPropertyChanged);
        MajorLineEveryProperty.Changed.AddClassHandler<TemplateClass>(OnVisualPropertyChanged);
        ScrollOffsetXProperty.Changed.AddClassHandler<TemplateClass>(OnVisualPropertyChanged);
        ScrollOffsetYProperty.Changed.AddClassHandler<TemplateClass>(OnVisualPropertyChanged);
        ContentOffsetXProperty.Changed.AddClassHandler<TemplateClass>(OnVisualPropertyChanged);
        ContentOffsetYProperty.Changed.AddClassHandler<TemplateClass>(OnVisualPropertyChanged);
    }

    public TemplateClass()
    {
        ClipToBounds = true;

        _gridLayer = new GridLayer(this) { IsHitTestVisible = false };
        _rulerLayer = new RulerLayer(this) { IsHitTestVisible = false, ZIndex = 100 };

        Children.Add(_gridLayer);
        Children.Add(_rulerLayer);
    }

    public double RulerThickness
    {
        get => GetValue(RulerThicknessProperty);
        set => SetValue(RulerThicknessProperty, value);
    }

    public double GridSpacing
    {
        get => GetValue(GridSpacingProperty);
        set => SetValue(GridSpacingProperty, value);
    }

    public int MajorLineEvery
    {
        get => GetValue(MajorLineEveryProperty);
        set => SetValue(MajorLineEveryProperty, value);
    }

    public double ScrollOffsetX
    {
        get => GetValue(ScrollOffsetXProperty);
        set => SetValue(ScrollOffsetXProperty, value);
    }

    public double ScrollOffsetY
    {
        get => GetValue(ScrollOffsetYProperty);
        set => SetValue(ScrollOffsetYProperty, value);
    }

    public double ContentOffsetX
    {
        get => GetValue(ContentOffsetXProperty);
        set => SetValue(ContentOffsetXProperty, value);
    }

    public double ContentOffsetY
    {
        get => GetValue(ContentOffsetYProperty);
        set => SetValue(ContentOffsetYProperty, value);
    }

    private static void OnVisualPropertyChanged(TemplateClass decorator, AvaloniaPropertyChangedEventArgs e)
    {
        decorator._gridLayer.InvalidateVisual();
        decorator._rulerLayer.InvalidateVisual();
    }

    /// <summary>Bottom layer: surface fill + the full-viewport world grid (extends under the ruler bands).</summary>
    private sealed class GridLayer(TemplateClass owner) : Control
    {
        public override void Render(DrawingContext context)
        {
            var bounds = Bounds;
            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                return;
            }

            context.DrawRectangle(SurfaceBackgroundBrush, null, bounds);
            DrawGrid(context, bounds);
        }

        private void DrawGrid(DrawingContext context, Rect bounds)
        {
            var ruler = Math.Max(0, owner.RulerThickness);
            var spacing = Math.Max(8, owner.GridSpacing);
            var majorStep = spacing * Math.Max(1, owner.MajorLineEvery);
            var worldLeft = WorkflowSurfaceMath.GridWorldLeft(owner.ScrollOffsetX, owner.ContentOffsetX);
            var worldTop = WorkflowSurfaceMath.GridWorldTop(owner.ScrollOffsetY, owner.ContentOffsetY);
            var worldRight = worldLeft + bounds.Width;
            var worldBottom = worldTop + bounds.Height;

            var firstVertical = WorkflowSurfaceMath.GridFirstLine(worldLeft, spacing);
            for (var value = firstVertical; value <= worldRight + spacing; value += spacing)
            {
                var x = WorkflowSurfaceMath.GridX(value, worldLeft, ruler);
                var pen = IsNearZero(value) ? AxisPen : IsMajorLine(value, majorStep) ? MajorGridPen : MinorGridPen;
                context.DrawLine(pen, new Point(x, 0), new Point(x, bounds.Height));
            }

            var firstHorizontal = WorkflowSurfaceMath.GridFirstLine(worldTop, spacing);
            for (var value = firstHorizontal; value <= worldBottom + spacing; value += spacing)
            {
                var y = WorkflowSurfaceMath.GridY(value, worldTop, ruler);
                var pen = IsNearZero(value) ? AxisPen : IsMajorLine(value, majorStep) ? MajorGridPen : MinorGridPen;
                context.DrawLine(pen, new Point(0, y), new Point(bounds.Width, y));
            }
        }
    }

    /// <summary>Topmost layer: the translucent ruler bands, dividers, ticks and labels (hit-test transparent).</summary>
    private sealed class RulerLayer(TemplateClass owner) : Control
    {
        public override void Render(DrawingContext context)
        {
            var bounds = Bounds;
            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                return;
            }

            var ruler = Math.Max(0, owner.RulerThickness);
            context.DrawRectangle(RulerBackgroundBrush, null, new Rect(0, 0, bounds.Width, ruler));
            context.DrawRectangle(RulerBackgroundBrush, null, new Rect(0, 0, ruler, bounds.Height));

            context.DrawLine(DividerPen, new Point(ruler, 0), new Point(ruler, bounds.Height));
            context.DrawLine(DividerPen, new Point(0, ruler), new Point(bounds.Width, ruler));

            var spacing = Math.Max(8, owner.GridSpacing);
            var majorStep = spacing * Math.Max(1, owner.MajorLineEvery);
            var worldLeft = WorkflowSurfaceMath.GridWorldLeft(owner.ScrollOffsetX, owner.ContentOffsetX);
            var worldTop = WorkflowSurfaceMath.GridWorldTop(owner.ScrollOffsetY, owner.ContentOffsetY);
            var worldRight = worldLeft + bounds.Width;
            var worldBottom = worldTop + bounds.Height;

            // Top ruler: ticks at world grid x crossing the viewport; skip the left band region.
            var firstVertical = WorkflowSurfaceMath.GridFirstLine(worldLeft, spacing);
            for (var value = firstVertical; value <= worldRight + spacing; value += spacing)
            {
                var x = WorkflowSurfaceMath.GridX(value, worldLeft, ruler);
                if (x < ruler)
                {
                    continue;
                }

                var isMajor = IsMajorLine(value, majorStep);
                var tickLength = isMajor ? ruler - 6 : Math.Max(6, ruler * 0.35);
                var pen = IsNearZero(value) ? AxisPen : TickPen;
                context.DrawLine(pen, new Point(x, ruler), new Point(x, ruler - tickLength));

                if (isMajor)
                {
                    DrawLabel(context, value, new Point(x + 3, 2));
                }
            }

            // Left ruler: ticks at world grid y crossing the viewport; skip the top band region.
            var firstHorizontal = WorkflowSurfaceMath.GridFirstLine(worldTop, spacing);
            for (var value = firstHorizontal; value <= worldBottom + spacing; value += spacing)
            {
                var y = WorkflowSurfaceMath.GridY(value, worldTop, ruler);
                if (y < ruler)
                {
                    continue;
                }

                var isMajor = IsMajorLine(value, majorStep);
                var tickLength = isMajor ? ruler - 6 : Math.Max(6, ruler * 0.35);
                var pen = IsNearZero(value) ? AxisPen : TickPen;
                context.DrawLine(pen, new Point(ruler, y), new Point(ruler - tickLength, y));

                if (isMajor)
                {
                    DrawLabel(context, value, new Point(3, y + 2));
                }
            }
        }
    }

    private static void DrawLabel(DrawingContext context, double value, Point point)
    {
        var text = FormatGridValue(value);
        var formattedText = new FormattedText(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            Typeface.Default,
            10,
            LabelBrush);

        context.DrawText(formattedText, point);
    }

    private static string FormatGridValue(double value)
    {
        var abs = Math.Abs(value);
        if (abs < 10000)
        {
            return Math.Round(value).ToString(CultureInfo.InvariantCulture);
        }

        if (abs < 1000000)
        {
            return Math.Round(value / 1000d, 1).ToString(CultureInfo.InvariantCulture) + "K";
        }

        return Math.Round(value / 1000000d, 1).ToString(CultureInfo.InvariantCulture) + "M";
    }

    private static bool IsMajorLine(double value, double majorStep)
    {
        if (majorStep <= 0)
        {
            return false;
        }

        var remainder = value % majorStep;
        return Math.Abs(remainder) < MajorLineEpsilon
               || Math.Abs(remainder - majorStep) < MajorLineEpsilon
               || Math.Abs(remainder + majorStep) < MajorLineEpsilon;
    }

    private static bool IsNearZero(double value)
        => Math.Abs(value) < MajorLineEpsilon;
}
