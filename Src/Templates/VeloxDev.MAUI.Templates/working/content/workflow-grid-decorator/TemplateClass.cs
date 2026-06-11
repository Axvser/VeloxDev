using System;
using VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace TemplateNamespace;

public sealed class TemplateClass : Grid, IWorkflowGridDecorator
{
    private readonly GraphicsView _gridLayer;

    public static readonly BindableProperty GridSpacingProperty = RegisterProperty(nameof(GridSpacing), 40d);
    public static readonly BindableProperty MajorLineEveryProperty = RegisterProperty(nameof(MajorLineEvery), 5);
    public static readonly BindableProperty ScrollOffsetXProperty = RegisterProperty(nameof(ScrollOffsetX), 0d);
    public static readonly BindableProperty ScrollOffsetYProperty = RegisterProperty(nameof(ScrollOffsetY), 0d);
    public static readonly BindableProperty ContentOffsetXProperty = RegisterProperty(nameof(ContentOffsetX), 0d);
    public static readonly BindableProperty ContentOffsetYProperty = RegisterProperty(nameof(ContentOffsetY), 0d);

    public TemplateClass()
    {
        BackgroundColor = Color.FromArgb("#1E1E1E");
        _gridLayer = new GraphicsView
        {
            Drawable = new GridDrawable(this),
            InputTransparent = true
        };
        Children.Add(_gridLayer);
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

    private static BindableProperty RegisterProperty(string name, object defaultValue)
        => BindableProperty.Create(
            name,
            defaultValue.GetType(),
            typeof(TemplateClass),
            defaultValue,
            propertyChanged: OnVisualPropertyChanged);

    private static void OnVisualPropertyChanged(BindableObject bindable, object? oldValue, object? newValue)
        => ((TemplateClass)bindable)._gridLayer?.Invalidate();

    private sealed class GridDrawable(TemplateClass owner) : IDrawable
    {
        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            canvas.FillColor = Color.FromArgb("#1E1E1E");
            canvas.FillRectangle(dirtyRect);

            var spacing = Math.Max(8, owner.GridSpacing);
            var majorStep = spacing * Math.Max(1, owner.MajorLineEvery);
            var worldLeft = owner.ScrollOffsetX - owner.ContentOffsetX;
            var worldTop = owner.ScrollOffsetY - owner.ContentOffsetY;

            for (var value = Math.Floor(worldLeft / spacing) * spacing;
                 value <= worldLeft + dirtyRect.Width + spacing;
                 value += spacing)
            {
                var x = (float)(value - worldLeft);
                SetStroke(canvas, value, majorStep);
                canvas.DrawLine(x, 0, x, dirtyRect.Height);
            }

            for (var value = Math.Floor(worldTop / spacing) * spacing;
                 value <= worldTop + dirtyRect.Height + spacing;
                 value += spacing)
            {
                var y = (float)(value - worldTop);
                SetStroke(canvas, value, majorStep);
                canvas.DrawLine(0, y, dirtyRect.Width, y);
            }
        }

        private static void SetStroke(ICanvas canvas, double value, double majorStep)
        {
            if (Math.Abs(value) < 0.001)
            {
                canvas.StrokeColor = Color.FromArgb("#4D4D4D");
                canvas.StrokeSize = 1.2f;
                return;
            }

            canvas.StrokeColor = Math.Abs(value % majorStep) < 0.001
                ? Color.FromArgb("#3A3D40")
                : Color.FromArgb("#2A2D2E");
            canvas.StrokeSize = 1;
        }
    }
}
