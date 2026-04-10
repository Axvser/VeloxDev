using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using System;
using System.Globalization;

namespace Demo;

public sealed class WorkflowGridDecorator : Decorator
{
    private const double MajorLineEpsilon = 0.001;

    private static readonly IBrush SurfaceBackgroundBrush = new ImmutableSolidColorBrush(Color.Parse("#141922"));
    private static readonly IBrush RulerBackgroundBrush = new ImmutableSolidColorBrush(Color.Parse("#1C2330"));
    private static readonly IBrush LabelBrush = new ImmutableSolidColorBrush(Color.Parse("#94A3B8"));
    private static readonly Pen MinorGridPen = new(new ImmutableSolidColorBrush(Color.Parse("#223043")), 1);
    private static readonly Pen MajorGridPen = new(new ImmutableSolidColorBrush(Color.Parse("#31445C")), 1);
    private static readonly Pen AxisPen = new(new ImmutableSolidColorBrush(Color.Parse("#38BDF8")), 1.2);
    private static readonly Pen TickPen = new(new ImmutableSolidColorBrush(Color.Parse("#64748B")), 1);
    private static readonly Pen DividerPen = new(new ImmutableSolidColorBrush(Color.Parse("#475569")), 1);

    public static readonly StyledProperty<double> RulerThicknessProperty =
        AvaloniaProperty.Register<WorkflowGridDecorator, double>(nameof(RulerThickness), 28d);

    public static readonly StyledProperty<double> GridSpacingProperty =
        AvaloniaProperty.Register<WorkflowGridDecorator, double>(nameof(GridSpacing), 40d);

    public static readonly StyledProperty<int> MajorLineEveryProperty =
        AvaloniaProperty.Register<WorkflowGridDecorator, int>(nameof(MajorLineEvery), 5);

    public static readonly StyledProperty<double> ScrollOffsetXProperty =
        AvaloniaProperty.Register<WorkflowGridDecorator, double>(nameof(ScrollOffsetX));

    public static readonly StyledProperty<double> ScrollOffsetYProperty =
        AvaloniaProperty.Register<WorkflowGridDecorator, double>(nameof(ScrollOffsetY));

    public static readonly StyledProperty<double> ContentOffsetXProperty =
        AvaloniaProperty.Register<WorkflowGridDecorator, double>(nameof(ContentOffsetX));

    public static readonly StyledProperty<double> ContentOffsetYProperty =
        AvaloniaProperty.Register<WorkflowGridDecorator, double>(nameof(ContentOffsetY));

    static WorkflowGridDecorator()
    {
        AffectsMeasure<WorkflowGridDecorator>(RulerThicknessProperty);
        AffectsArrange<WorkflowGridDecorator>(RulerThicknessProperty);
        AffectsRender<WorkflowGridDecorator>(
            RulerThicknessProperty,
            GridSpacingProperty,
            MajorLineEveryProperty,
            ScrollOffsetXProperty,
            ScrollOffsetYProperty,
            ContentOffsetXProperty,
            ContentOffsetYProperty);
    }

    public WorkflowGridDecorator()
    {
        ClipToBounds = true;
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

    protected override Size MeasureOverride(Size availableSize)
    {
        var ruler = Math.Max(0, RulerThickness);
        var childAvailable = new Size(
            Math.Max(0, availableSize.Width - ruler),
            Math.Max(0, availableSize.Height - ruler));

        Child?.Measure(childAvailable);

        if (Child is null)
        {
            return new Size(ruler, ruler);
        }

        return new Size(
            Child.DesiredSize.Width + ruler,
            Child.DesiredSize.Height + ruler);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var ruler = Math.Max(0, RulerThickness);
        var childBounds = new Rect(
            ruler,
            ruler,
            Math.Max(0, finalSize.Width - ruler),
            Math.Max(0, finalSize.Height - ruler));

        Child?.Arrange(childBounds);
        return finalSize;
    }

    public override void Render(DrawingContext context)
    {
        var bounds = Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        var ruler = Math.Max(0, RulerThickness);
        var contentRect = new Rect(
            ruler,
            ruler,
            Math.Max(0, bounds.Width - ruler),
            Math.Max(0, bounds.Height - ruler));

        context.DrawRectangle(SurfaceBackgroundBrush, null, bounds);
        context.DrawRectangle(RulerBackgroundBrush, null, new Rect(0, 0, bounds.Width, ruler));
        context.DrawRectangle(RulerBackgroundBrush, null, new Rect(0, 0, ruler, bounds.Height));

        if (contentRect.Width > 0 && contentRect.Height > 0)
        {
            using (context.PushClip(contentRect))
            {
                context.DrawRectangle(SurfaceBackgroundBrush, null, contentRect);
                DrawGrid(context, contentRect);
            }
        }

        DrawRulers(context, bounds, contentRect);
    }

    private void DrawGrid(DrawingContext context, Rect contentRect)
    {
        var spacing = Math.Max(8, GridSpacing);
        var majorStep = spacing * Math.Max(1, MajorLineEvery);
        var worldLeft = ScrollOffsetX - ContentOffsetX;
        var worldTop = ScrollOffsetY - ContentOffsetY;
        var worldRight = worldLeft + contentRect.Width;
        var worldBottom = worldTop + contentRect.Height;

        var firstVertical = Math.Floor(worldLeft / spacing) * spacing;
        for (var value = firstVertical; value <= worldRight + spacing; value += spacing)
        {
            var x = contentRect.X + (value - worldLeft);
            var pen = IsNearZero(value) ? AxisPen : IsMajorLine(value, majorStep) ? MajorGridPen : MinorGridPen;
            context.DrawLine(pen, new Point(x, contentRect.Y), new Point(x, contentRect.Bottom));
        }

        var firstHorizontal = Math.Floor(worldTop / spacing) * spacing;
        for (var value = firstHorizontal; value <= worldBottom + spacing; value += spacing)
        {
            var y = contentRect.Y + (value - worldTop);
            var pen = IsNearZero(value) ? AxisPen : IsMajorLine(value, majorStep) ? MajorGridPen : MinorGridPen;
            context.DrawLine(pen, new Point(contentRect.X, y), new Point(contentRect.Right, y));
        }
    }

    private void DrawRulers(DrawingContext context, Rect bounds, Rect contentRect)
    {
        var ruler = Math.Max(0, RulerThickness);
        var spacing = Math.Max(8, GridSpacing);
        var majorStep = spacing * Math.Max(1, MajorLineEvery);
        var worldLeft = ScrollOffsetX - ContentOffsetX;
        var worldTop = ScrollOffsetY - ContentOffsetY;
        var worldRight = worldLeft + contentRect.Width;
        var worldBottom = worldTop + contentRect.Height;

        context.DrawLine(DividerPen, new Point(ruler, 0), new Point(ruler, bounds.Height));
        context.DrawLine(DividerPen, new Point(0, ruler), new Point(bounds.Width, ruler));

        using (context.PushClip(new Rect(ruler, 0, contentRect.Width, ruler)))
        {
            var firstVertical = Math.Floor(worldLeft / spacing) * spacing;
            for (var value = firstVertical; value <= worldRight + spacing; value += spacing)
            {
                var x = contentRect.X + (value - worldLeft);
                var isMajor = IsMajorLine(value, majorStep);
                var tickLength = isMajor ? ruler - 6 : Math.Max(6, ruler * 0.35);
                var pen = IsNearZero(value) ? AxisPen : TickPen;
                context.DrawLine(pen, new Point(x, ruler), new Point(x, ruler - tickLength));

                if (isMajor)
                {
                    DrawLabel(context, value, new Point(x + 3, 2));
                }
            }
        }

        using (context.PushClip(new Rect(0, ruler, ruler, contentRect.Height)))
        {
            var firstHorizontal = Math.Floor(worldTop / spacing) * spacing;
            for (var value = firstHorizontal; value <= worldBottom + spacing; value += spacing)
            {
                var y = contentRect.Y + (value - worldTop);
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
        var text = Math.Round(value).ToString(CultureInfo.InvariantCulture);
        var formattedText = new FormattedText(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            Typeface.Default,
            10,
            LabelBrush);

        context.DrawText(formattedText, point);
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
