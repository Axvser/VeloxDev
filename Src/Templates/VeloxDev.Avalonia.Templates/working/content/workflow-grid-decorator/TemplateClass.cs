using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace TemplateNamespace;

public sealed class TemplateClass : Decorator, IWorkflowGridDecorator
{
    private const double MajorLineEpsilon = 0.001;

    private static readonly IBrush BackgroundBrush = new ImmutableSolidColorBrush(Color.Parse("TemplateGridBackground"));
    private static readonly IBrush RulerBackgroundBrush = new ImmutableSolidColorBrush(Color.Parse("TemplateRulerBackground"));
    private static readonly IBrush LabelBrush = new ImmutableSolidColorBrush(Color.Parse("TemplateRulerLabelColor"));
    private static readonly Pen MinorGridPen = CreatePen("TemplateMinorGridColor", 1);
    private static readonly Pen MajorGridPen = CreatePen("TemplateMajorGridColor", 1);
    private static readonly Pen AxisPen = CreatePen("TemplateAxisColor", 1.2);
    private static readonly Pen TickPen = CreatePen("TemplateRulerTickColor", 1);
    private static readonly Pen DividerPen = CreatePen("TemplateRulerDividerColor", 1);

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
        AffectsMeasure<TemplateClass>(RulerThicknessProperty);
        AffectsArrange<TemplateClass>(RulerThicknessProperty);
        AffectsRender<TemplateClass>(
            RulerThicknessProperty, GridSpacingProperty, MajorLineEveryProperty,
            ScrollOffsetXProperty, ScrollOffsetYProperty,
            ContentOffsetXProperty, ContentOffsetYProperty);
    }

    public TemplateClass()
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

        return new Size(Child.DesiredSize.Width + ruler, Child.DesiredSize.Height + ruler);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var ruler = Math.Max(0, RulerThickness);
        Child?.Arrange(new Rect(
            ruler, ruler,
            Math.Max(0, finalSize.Width - ruler),
            Math.Max(0, finalSize.Height - ruler)));
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
            ruler, ruler,
            Math.Max(0, bounds.Width - ruler),
            Math.Max(0, bounds.Height - ruler));

        context.DrawRectangle(BackgroundBrush, null, bounds);
        context.DrawRectangle(RulerBackgroundBrush, null, new Rect(0, 0, bounds.Width, ruler));
        context.DrawRectangle(RulerBackgroundBrush, null, new Rect(0, 0, ruler, bounds.Height));

        if (contentRect.Width > 0 && contentRect.Height > 0)
        {
            using (context.PushClip(contentRect))
            {
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
            context.DrawLine(SelectPen(value, majorStep), new Point(x, contentRect.Y), new Point(x, contentRect.Bottom));
        }

        var firstHorizontal = Math.Floor(worldTop / spacing) * spacing;
        for (var value = firstHorizontal; value <= worldBottom + spacing; value += spacing)
        {
            var y = contentRect.Y + (value - worldTop);
            context.DrawLine(SelectPen(value, majorStep), new Point(contentRect.X, y), new Point(contentRect.Right, y));
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
        var text = FormatGridValue(value);
        var formattedText = new FormattedText(
            text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            Typeface.Default, 10, LabelBrush);
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

    private static Pen SelectPen(double value, double majorStep)
        => IsNearZero(value)
            ? AxisPen
            : IsMajorLine(value, majorStep) ? MajorGridPen : MinorGridPen;

    private static bool IsMajorLine(double value, double majorStep)
        => majorStep > 0 && (Math.Abs(value % majorStep) < MajorLineEpsilon
            || Math.Abs(value % majorStep - majorStep) < MajorLineEpsilon
            || Math.Abs(value % majorStep + majorStep) < MajorLineEpsilon);

    private static bool IsNearZero(double value)
        => Math.Abs(value) < MajorLineEpsilon;

    private static Pen CreatePen(string color, double thickness)
        => new(new ImmutableSolidColorBrush(Color.Parse(color)), thickness);
}
