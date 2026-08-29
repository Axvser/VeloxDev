using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Media;
using Size = Jalium.UI.Size;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

/// <summary>Decorator that wraps the workflow ScrollViewer, insets it by the ruler thickness,
/// and paints the dark surface, grid lines and top/left rulers in OnRender. Grid lines are
/// drawn at world multiples of GridSpacing mapped by (ScrollOffset - ContentOffset), so they
/// extend into negative world coordinates when the canvas has grown left/up.</summary>
public class WorkflowGridDecorator : Decorator, IWorkflowGridDecorator
{
    public static readonly DependencyProperty RulerThicknessProperty = DependencyProperty.Register(
        "RulerThickness", typeof(double), typeof(WorkflowGridDecorator), new PropertyMetadata(28.0, OnVisualChanged));

    public static readonly DependencyProperty GridSpacingProperty = DependencyProperty.Register(
        "GridSpacing", typeof(double), typeof(WorkflowGridDecorator), new PropertyMetadata(40.0, OnVisualChanged));

    public static readonly DependencyProperty MajorLineEveryProperty = DependencyProperty.Register(
        "MajorLineEvery", typeof(int), typeof(WorkflowGridDecorator), new PropertyMetadata(5, OnVisualChanged));

    public static readonly DependencyProperty ScrollOffsetXProperty = DependencyProperty.Register(
        "ScrollOffsetX", typeof(double), typeof(WorkflowGridDecorator), new PropertyMetadata(0.0, OnVisualChanged));

    public static readonly DependencyProperty ScrollOffsetYProperty = DependencyProperty.Register(
        "ScrollOffsetY", typeof(double), typeof(WorkflowGridDecorator), new PropertyMetadata(0.0, OnVisualChanged));

    public static readonly DependencyProperty ContentOffsetXProperty = DependencyProperty.Register(
        "ContentOffsetX", typeof(double), typeof(WorkflowGridDecorator), new PropertyMetadata(0.0, OnVisualChanged));

    public static readonly DependencyProperty ContentOffsetYProperty = DependencyProperty.Register(
        "ContentOffsetY", typeof(double), typeof(WorkflowGridDecorator), new PropertyMetadata(0.0, OnVisualChanged));

    public double RulerThickness { get => (double)GetValue(RulerThicknessProperty); set => SetValue(RulerThicknessProperty, value); }
    public double GridSpacing { get => (double)GetValue(GridSpacingProperty); set => SetValue(GridSpacingProperty, value); }
    public int MajorLineEvery { get => (int)GetValue(MajorLineEveryProperty); set => SetValue(MajorLineEveryProperty, value); }

    public double ScrollOffsetX { get => (double)GetValue(ScrollOffsetXProperty); set => SetValue(ScrollOffsetXProperty, value); }
    public double ScrollOffsetY { get => (double)GetValue(ScrollOffsetYProperty); set => SetValue(ScrollOffsetYProperty, value); }
    public double ContentOffsetX { get => (double)GetValue(ContentOffsetXProperty); set => SetValue(ContentOffsetXProperty, value); }
    public double ContentOffsetY { get => (double)GetValue(ContentOffsetYProperty); set => SetValue(ContentOffsetYProperty, value); }

    private static void OnVisualChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is UIElement element)
        {
            element.InvalidateVisual();
        }
    }

    protected override Jalium.UI.Size MeasureOverride(Jalium.UI.Size constraint)
    {
        var child = Child;
        if (child is null)
        {
            return default;
        }

        child.Measure(constraint);
        return child.DesiredSize;
    }

    protected override Jalium.UI.Size ArrangeOverride(Jalium.UI.Size finalSize)
    {
        // No inset: the wrapped ScrollViewer fills the whole surface so the grid
        // aligns to window coordinates (world origin at the window's top-left),
        // matching the Jalium NodeEditorDemo exactly.
        var child = Child;
        if (child is not null)
        {
            child.Arrange(new Rect(0, 0, finalSize.Width, finalSize.Height));
        }

        return finalSize;
    }

    protected override void OnRender(DrawingContext dc)
    {
        var spacing = Math.Max(1.0, GridSpacing);
        var majorEvery = Math.Max(1, MajorLineEvery);
        var majorStep = spacing * majorEvery;
        var w = RenderSize.Width;
        var h = RenderSize.Height;
        var content = new Rect(0, 0, w, h);

        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E)), null, content);

        var worldLeft = WorkflowSurfaceMath.GridWorldLeft(ScrollOffsetX, ContentOffsetX);
        var worldTop = WorkflowSurfaceMath.GridWorldTop(ScrollOffsetY, ContentOffsetY);
        var worldRight = worldLeft + content.Width;
        var worldBottom = worldTop + content.Height;

        var minorPen = new Pen(new SolidColorBrush(Color.FromRgb(0x2A, 0x2D, 0x2E)), 1);
        var majorPen = new Pen(new SolidColorBrush(Color.FromRgb(0x3A, 0x3D, 0x40)), 1);
        var axisPen = new Pen(new SolidColorBrush(Color.FromRgb(0x4D, 0x4D, 0x4D)), 1.2);

        var firstVertical = WorkflowSurfaceMath.GridFirstLine(worldLeft, spacing);
        for (var value = firstVertical; value <= worldRight + spacing; value += spacing)
        {
            var x = WorkflowSurfaceMath.GridX(value, worldLeft, content.X);
            var pen = Math.Abs(value) < 0.001 ? axisPen : (Math.Abs(value % majorStep) < 0.001 ? majorPen : minorPen);
            dc.DrawLine(pen, new Point(x, content.Top), new Point(x, content.Bottom));
        }

        var firstHorizontal = WorkflowSurfaceMath.GridFirstLine(worldTop, spacing);
        for (var value = firstHorizontal; value <= worldBottom + spacing; value += spacing)
        {
            var y = WorkflowSurfaceMath.GridY(value, worldTop, content.Y);
            var pen = Math.Abs(value) < 0.001 ? axisPen : (Math.Abs(value % majorStep) < 0.001 ? majorPen : minorPen);
            dc.DrawLine(pen, new Point(content.Left, y), new Point(content.Right, y));
        }
    }
}
