using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace Demo.Views.Workflow;

public sealed class WorkflowGridDecorator : Decorator, IWorkflowGridDecorator
{
    private const double MajorLineEpsilon = 0.001;

    private static readonly Brush SurfaceBackgroundBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E1E1E"));
    private static readonly Brush RulerBackgroundBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#252526"));
    private static readonly Brush LabelBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#888888"));
    private static readonly Pen MinorGridPen = CreateFrozenPen("#2A2D2E", 1);
    private static readonly Pen MajorGridPen = CreateFrozenPen("#3A3D40", 1);
    private static readonly Pen AxisPen = CreateFrozenPen("#4D4D4D", 1.2);
    private static readonly Pen TickPen = CreateFrozenPen("#555555", 1);
    private static readonly Pen DividerPen = CreateFrozenPen("#3A3D40", 1);
    private static readonly Typeface LabelTypeface = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

    public static readonly DependencyProperty RulerThicknessProperty =
        DependencyProperty.Register(nameof(RulerThickness), typeof(double), typeof(WorkflowGridDecorator),
            new FrameworkPropertyMetadata(28d, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty GridSpacingProperty =
        DependencyProperty.Register(nameof(GridSpacing), typeof(double), typeof(WorkflowGridDecorator),
            new FrameworkPropertyMetadata(40d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MajorLineEveryProperty =
        DependencyProperty.Register(nameof(MajorLineEvery), typeof(int), typeof(WorkflowGridDecorator),
            new FrameworkPropertyMetadata(5, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ScrollOffsetXProperty =
        DependencyProperty.Register(nameof(ScrollOffsetX), typeof(double), typeof(WorkflowGridDecorator),
            new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ScrollOffsetYProperty =
        DependencyProperty.Register(nameof(ScrollOffsetY), typeof(double), typeof(WorkflowGridDecorator),
            new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ContentOffsetXProperty =
        DependencyProperty.Register(nameof(ContentOffsetX), typeof(double), typeof(WorkflowGridDecorator),
            new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ContentOffsetYProperty =
        DependencyProperty.Register(nameof(ContentOffsetY), typeof(double), typeof(WorkflowGridDecorator),
            new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    static WorkflowGridDecorator()
    {
        SurfaceBackgroundBrush.Freeze();
        RulerBackgroundBrush.Freeze();
        LabelBrush.Freeze();
    }

    public WorkflowGridDecorator()
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
            ruler,
            ruler,
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
            ruler,
            ruler,
            Math.Max(0, bounds.Width - ruler),
            Math.Max(0, bounds.Height - ruler));

        drawingContext.DrawRectangle(SurfaceBackgroundBrush, null, bounds);
        drawingContext.DrawRectangle(RulerBackgroundBrush, null, new Rect(0, 0, bounds.Width, ruler));
        drawingContext.DrawRectangle(RulerBackgroundBrush, null, new Rect(0, 0, ruler, bounds.Height));

        if (contentRect.Width > 0 && contentRect.Height > 0)
        {
            drawingContext.PushClip(new RectangleGeometry(contentRect));
            drawingContext.DrawRectangle(SurfaceBackgroundBrush, null, contentRect);
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

    private static void DrawLabel(DrawingContext context, double value, Point point)
    {
        var text = FormatGridValue(value);
        var formattedText = new FormattedText(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            LabelTypeface,
            10,
            LabelBrush,
            1.0);

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

    private static Pen CreateFrozenPen(string color, double thickness)
    {
        var pen = new Pen(new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)), thickness);
        pen.Freeze();
        return pen;
    }
}
