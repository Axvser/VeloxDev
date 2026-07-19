using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace TemplateNamespace;

public sealed class TemplateClass : Decorator, IWorkflowGridDecorator
{
    private const double MajorLineEpsilon = 0.001;

    private static readonly Brush BackgroundBrush = CreateBrush("TemplateGridBackground");
    private static readonly Brush RulerBackgroundBrush = CreateBrush("TemplateRulerBackground");
    private static readonly Brush LabelBrush = CreateBrush("TemplateRulerLabelColor");
    private static readonly Pen MinorGridPen = CreatePen("TemplateMinorGridColor", 1);
    private static readonly Pen MajorGridPen = CreatePen("TemplateMajorGridColor", 1);
    private static readonly Pen AxisPen = CreatePen("TemplateAxisColor", 1.2);
    private static readonly Pen TickPen = CreatePen("TemplateRulerTickColor", 1);
    private static readonly Pen DividerPen = CreatePen("TemplateRulerDividerColor", 1);
    private static readonly Typeface LabelTypeface = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

    public static readonly DependencyProperty RulerThicknessProperty =
        DependencyProperty.Register(nameof(RulerThickness), typeof(double), typeof(TemplateClass),
            new FrameworkPropertyMetadata(28d, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty GridSpacingProperty = DependencyProperty.Register(
        nameof(GridSpacing), typeof(double), typeof(TemplateClass),
        new FrameworkPropertyMetadata(TemplateGridSpacing, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MajorLineEveryProperty = DependencyProperty.Register(
        nameof(MajorLineEvery), typeof(int), typeof(TemplateClass),
        new FrameworkPropertyMetadata(TemplateMajorLineEvery, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ScrollOffsetXProperty = RegisterOffset(nameof(ScrollOffsetX));
    public static readonly DependencyProperty ScrollOffsetYProperty = RegisterOffset(nameof(ScrollOffsetY));
    public static readonly DependencyProperty ContentOffsetXProperty = RegisterOffset(nameof(ContentOffsetX));
    public static readonly DependencyProperty ContentOffsetYProperty = RegisterOffset(nameof(ContentOffsetY));

    static TemplateClass()
    {
        BackgroundBrush.Freeze();
        RulerBackgroundBrush.Freeze();
        LabelBrush.Freeze();
    }

    public TemplateClass()
    {
        ClipToBounds = true;
        SnapsToDevicePixels = true;
    }

    public double RulerThickness
    {
        get => (double)GetValue(RulerThicknessProperty);
        set => SetValue(RulerThicknessProperty, value);
    }

    public double GridSpacing
    {
        get => (double)GetValue(GridSpacingProperty);
        set => SetValue(GridSpacingProperty, value);
    }

    public int MajorLineEvery
    {
        get => (int)GetValue(MajorLineEveryProperty);
        set => SetValue(MajorLineEveryProperty, value);
    }

    public double ScrollOffsetX
    {
        get => (double)GetValue(ScrollOffsetXProperty);
        set => SetValue(ScrollOffsetXProperty, value);
    }

    public double ScrollOffsetY
    {
        get => (double)GetValue(ScrollOffsetYProperty);
        set => SetValue(ScrollOffsetYProperty, value);
    }

    public double ContentOffsetX
    {
        get => (double)GetValue(ContentOffsetXProperty);
        set => SetValue(ContentOffsetXProperty, value);
    }

    public double ContentOffsetY
    {
        get => (double)GetValue(ContentOffsetYProperty);
        set => SetValue(ContentOffsetYProperty, value);
    }

    protected override Size MeasureOverride(Size constraint)
    {
        var ruler = Math.Max(0, RulerThickness);
        var childAvailable = new Size(
            Math.Max(0, constraint.Width - ruler),
            Math.Max(0, constraint.Height - ruler));

        Child?.Measure(childAvailable);

        if (Child is null)
        {
            return new Size(ruler, ruler);
        }

        return new Size(Child.DesiredSize.Width + ruler, Child.DesiredSize.Height + ruler);
    }

    protected override Size ArrangeOverride(Size arrangeSize)
    {
        var ruler = Math.Max(0, RulerThickness);
        Child?.Arrange(new Rect(
            ruler, ruler,
            Math.Max(0, arrangeSize.Width - ruler),
            Math.Max(0, arrangeSize.Height - ruler)));
        return arrangeSize;
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        var bounds = new Rect(RenderSize);
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        var ruler = Math.Max(0, RulerThickness);
        var contentRect = new Rect(
            ruler, ruler,
            Math.Max(0, bounds.Width - ruler),
            Math.Max(0, bounds.Height - ruler));

        drawingContext.DrawRectangle(BackgroundBrush, null, bounds);
        drawingContext.DrawRectangle(RulerBackgroundBrush, null, new Rect(0, 0, bounds.Width, ruler));
        drawingContext.DrawRectangle(RulerBackgroundBrush, null, new Rect(0, 0, ruler, bounds.Height));

        if (contentRect.Width > 0 && contentRect.Height > 0)
        {
            drawingContext.PushClip(new RectangleGeometry(contentRect));
            DrawGrid(drawingContext, contentRect);
            drawingContext.Pop();
        }

        DrawRulers(drawingContext, bounds, contentRect);
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

        context.PushClip(new RectangleGeometry(new Rect(ruler, 0, contentRect.Width, ruler)));
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
        context.Pop();

        context.PushClip(new RectangleGeometry(new Rect(0, ruler, ruler, contentRect.Height)));
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
        context.Pop();
    }

    private void DrawLabel(DrawingContext context, double value, Point point)
    {
        var text = FormatGridValue(value);
        var formattedText = new FormattedText(
            text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            LabelTypeface, 10, LabelBrush, 1.0);
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

    private static DependencyProperty RegisterOffset(string name)
        => DependencyProperty.Register(
            name, typeof(double), typeof(TemplateClass),
            new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

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

    private static Brush CreateBrush(string color)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        return brush;
    }

    private static Pen CreatePen(string color, double thickness)
    {
        var pen = new Pen(CreateBrush(color), thickness);
        pen.Freeze();
        return pen;
    }
}
