using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace TemplateNamespace;

public sealed class TemplateClass : Decorator, IWorkflowGridDecorator
{
    private static readonly IBrush BackgroundBrush = new ImmutableSolidColorBrush(Color.Parse("#1E1E1E"));
    private static readonly Pen MinorGridPen = CreatePen("#2A2D2E", 1);
    private static readonly Pen MajorGridPen = CreatePen("#3A3D40", 1);
    private static readonly Pen AxisPen = CreatePen("#4D4D4D", 1.2);

    public static readonly StyledProperty<double> GridSpacingProperty =
        AvaloniaProperty.Register<TemplateClass, double>(nameof(GridSpacing), 40d);

    public static readonly StyledProperty<int> MajorLineEveryProperty =
        AvaloniaProperty.Register<TemplateClass, int>(nameof(MajorLineEvery), 5);

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
        AffectsRender<TemplateClass>(
            GridSpacingProperty,
            MajorLineEveryProperty,
            ScrollOffsetXProperty,
            ScrollOffsetYProperty,
            ContentOffsetXProperty,
            ContentOffsetYProperty);
    }

    public TemplateClass()
    {
        ClipToBounds = true;
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

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.DrawRectangle(BackgroundBrush, null, Bounds);

        var spacing = Math.Max(8, GridSpacing);
        var majorStep = spacing * Math.Max(1, MajorLineEvery);
        var worldLeft = ScrollOffsetX - ContentOffsetX;
        var worldTop = ScrollOffsetY - ContentOffsetY;

        for (var value = Math.Floor(worldLeft / spacing) * spacing;
             value <= worldLeft + Bounds.Width + spacing;
             value += spacing)
        {
            var x = value - worldLeft;
            context.DrawLine(SelectPen(value, majorStep), new Point(x, 0), new Point(x, Bounds.Height));
        }

        for (var value = Math.Floor(worldTop / spacing) * spacing;
             value <= worldTop + Bounds.Height + spacing;
             value += spacing)
        {
            var y = value - worldTop;
            context.DrawLine(SelectPen(value, majorStep), new Point(0, y), new Point(Bounds.Width, y));
        }
    }

    private static Pen SelectPen(double value, double majorStep)
        => Math.Abs(value) < 0.001
            ? AxisPen
            : Math.Abs(value % majorStep) < 0.001
                ? MajorGridPen
                : MinorGridPen;

    private static Pen CreatePen(string color, double thickness)
        => new(new ImmutableSolidColorBrush(Color.Parse(color)), thickness);
}
