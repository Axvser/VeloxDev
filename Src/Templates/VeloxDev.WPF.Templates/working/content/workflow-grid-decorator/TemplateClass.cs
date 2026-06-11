using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace TemplateNamespace;

public sealed class TemplateClass : Decorator, IWorkflowGridDecorator
{
    private static readonly Brush BackgroundBrush = CreateBrush("#1E1E1E");
    private static readonly Pen MinorGridPen = CreatePen("#2A2D2E", 1);
    private static readonly Pen MajorGridPen = CreatePen("#3A3D40", 1);
    private static readonly Pen AxisPen = CreatePen("#4D4D4D", 1.2);

    public static readonly DependencyProperty GridSpacingProperty = DependencyProperty.Register(
        nameof(GridSpacing),
        typeof(double),
        typeof(TemplateClass),
        new FrameworkPropertyMetadata(40d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MajorLineEveryProperty = DependencyProperty.Register(
        nameof(MajorLineEvery),
        typeof(int),
        typeof(TemplateClass),
        new FrameworkPropertyMetadata(5, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ScrollOffsetXProperty = RegisterOffset(nameof(ScrollOffsetX));
    public static readonly DependencyProperty ScrollOffsetYProperty = RegisterOffset(nameof(ScrollOffsetY));
    public static readonly DependencyProperty ContentOffsetXProperty = RegisterOffset(nameof(ContentOffsetX));
    public static readonly DependencyProperty ContentOffsetYProperty = RegisterOffset(nameof(ContentOffsetY));

    public TemplateClass()
    {
        ClipToBounds = true;
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

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        drawingContext.DrawRectangle(BackgroundBrush, null, new Rect(RenderSize));

        var spacing = Math.Max(8, GridSpacing);
        var majorStep = spacing * Math.Max(1, MajorLineEvery);
        var worldLeft = ScrollOffsetX - ContentOffsetX;
        var worldTop = ScrollOffsetY - ContentOffsetY;

        for (var value = Math.Floor(worldLeft / spacing) * spacing;
             value <= worldLeft + RenderSize.Width + spacing;
             value += spacing)
        {
            var x = value - worldLeft;
            drawingContext.DrawLine(SelectPen(value, majorStep), new Point(x, 0), new Point(x, RenderSize.Height));
        }

        for (var value = Math.Floor(worldTop / spacing) * spacing;
             value <= worldTop + RenderSize.Height + spacing;
             value += spacing)
        {
            var y = value - worldTop;
            drawingContext.DrawLine(SelectPen(value, majorStep), new Point(0, y), new Point(RenderSize.Width, y));
        }
    }

    private static DependencyProperty RegisterOffset(string name)
        => DependencyProperty.Register(
            name,
            typeof(double),
            typeof(TemplateClass),
            new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    private static Pen SelectPen(double value, double majorStep)
        => Math.Abs(value) < 0.001
            ? AxisPen
            : Math.Abs(value % majorStep) < 0.001
                ? MajorGridPen
                : MinorGridPen;

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
