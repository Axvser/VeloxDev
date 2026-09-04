using System;
using System.Globalization;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace Demo.Controls;

/// <summary>
/// A workflow surface decorator that draws the world grid and floating translucent rulers.
/// Content (the scroll viewer + canvas) fills the whole viewport; the world canvas is
/// translated by <see cref="RulerThickness"/> so the world origin stays at the ruler-band
/// edge while content can still scroll under the translucent bands (the Jalium floating
/// ruler model). The decorator layers, bottom to top: surface + grid (<see cref="_gridGraphicsView"/>),
/// the content child, and the ruler overlay (<see cref="_rulerGraphicsView"/>, topmost,
/// input-transparent).
/// </summary>
public sealed class WorkflowGridDecorator : Grid, IWorkflowGridDecorator
{
    private const double MajorLineEpsilon = 0.001;
    private readonly GraphicsView _gridGraphicsView;
    private readonly GraphicsView _rulerGraphicsView;

    public static readonly BindableProperty RulerThicknessProperty = BindableProperty.Create(
        nameof(RulerThickness), typeof(double), typeof(WorkflowGridDecorator), 28d, propertyChanged: OnVisualPropertyChanged);

    public static readonly BindableProperty GridSpacingProperty = BindableProperty.Create(
        nameof(GridSpacing), typeof(double), typeof(WorkflowGridDecorator), 40d, propertyChanged: OnVisualPropertyChanged);

    public static readonly BindableProperty MajorLineEveryProperty = BindableProperty.Create(
        nameof(MajorLineEvery), typeof(int), typeof(WorkflowGridDecorator), 5, propertyChanged: OnVisualPropertyChanged);

    public static readonly BindableProperty ScrollOffsetXProperty = BindableProperty.Create(
        nameof(ScrollOffsetX), typeof(double), typeof(WorkflowGridDecorator), 0d, propertyChanged: OnVisualPropertyChanged);

    public static readonly BindableProperty ScrollOffsetYProperty = BindableProperty.Create(
        nameof(ScrollOffsetY), typeof(double), typeof(WorkflowGridDecorator), 0d, propertyChanged: OnVisualPropertyChanged);

    public static readonly BindableProperty ContentOffsetXProperty = BindableProperty.Create(
        nameof(ContentOffsetX), typeof(double), typeof(WorkflowGridDecorator), 0d, propertyChanged: OnVisualPropertyChanged);

    public static readonly BindableProperty ContentOffsetYProperty = BindableProperty.Create(
        nameof(ContentOffsetY), typeof(double), typeof(WorkflowGridDecorator), 0d, propertyChanged: OnVisualPropertyChanged);

    public WorkflowGridDecorator()
    {
        _gridGraphicsView = new GraphicsView
        {
            Drawable = new GridDrawable(this),
            InputTransparent = true
        };
        _rulerGraphicsView = new GraphicsView
        {
            Drawable = new RulerDrawable(this),
            InputTransparent = true,
            // Above the content child (scroll viewer), so content scrolling under the
            // translucent bands stays visibly dimmed.
            ZIndex = 10
        };
        Children.Add(_gridGraphicsView);
        Children.Add(_rulerGraphicsView);
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

    private static void OnVisualPropertyChanged(BindableObject bindable, object? oldValue, object? newValue)
    {
        if (bindable is WorkflowGridDecorator decorator)
        {
            decorator._gridGraphicsView.Invalidate();
            decorator._rulerGraphicsView.Invalidate();
        }
    }

    /// <summary>Bottom layer: surface background + the world grid over the full viewport (extends under the ruler bands).</summary>
    private sealed class GridDrawable(WorkflowGridDecorator owner) : IDrawable
    {
        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            canvas.SaveState();

            canvas.FillColor = Color.FromArgb("#1E1E1E");
            canvas.FillRectangle(dirtyRect);

            DrawGrid(canvas, dirtyRect, Math.Max(0, owner.RulerThickness));

            canvas.RestoreState();
        }

        private void DrawGrid(ICanvas canvas, RectF bounds, double ruler)
        {
            var spacing = Math.Max(8, owner.GridSpacing);
            var majorStep = spacing * Math.Max(1, owner.MajorLineEvery);
            var worldLeft = WorkflowSurfaceMath.GridWorldLeft(owner.ScrollOffsetX, owner.ContentOffsetX);
            var worldTop = WorkflowSurfaceMath.GridWorldTop(owner.ScrollOffsetY, owner.ContentOffsetY);
            var worldRight = worldLeft + bounds.Width;
            var worldBottom = worldTop + bounds.Height;

            var firstVertical = WorkflowSurfaceMath.GridFirstLine(worldLeft, spacing);
            for (var value = firstVertical; value <= worldRight + spacing; value += spacing)
            {
                var x = (float)WorkflowSurfaceMath.GridX(value, worldLeft, ruler);
                SetGridStroke(canvas, value, majorStep);
                canvas.DrawLine(x, 0, x, bounds.Height);
            }

            var firstHorizontal = WorkflowSurfaceMath.GridFirstLine(worldTop, spacing);
            for (var value = firstHorizontal; value <= worldBottom + spacing; value += spacing)
            {
                var y = (float)WorkflowSurfaceMath.GridY(value, worldTop, ruler);
                SetGridStroke(canvas, value, majorStep);
                canvas.DrawLine(0, y, bounds.Width, y);
            }
        }

        private void SetGridStroke(ICanvas canvas, double value, double majorStep)
        {
            if (IsNearZero(value))
            {
                canvas.StrokeColor = Color.FromArgb("#4D4D4D");
                canvas.StrokeSize = 1.2f;
                return;
            }

            canvas.StrokeColor = IsMajorLine(value, majorStep)
                ? Color.FromArgb("#3A3D40")
                : Color.FromArgb("#2A2D2E");
            canvas.StrokeSize = 1;
        }
    }

    /// <summary>Top layer: translucent ruler bands, dividers, ticks and labels (hit-test transparent).</summary>
    private sealed class RulerDrawable(WorkflowGridDecorator owner) : IDrawable
    {
        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            canvas.SaveState();

            var ruler = Math.Max(0, owner.RulerThickness);
            canvas.FillColor = Color.FromArgb("#C8252526");
            canvas.FillRectangle(0, 0, dirtyRect.Width, (float)ruler);
            canvas.FillRectangle(0, 0, (float)ruler, dirtyRect.Height);

            DrawRulers(canvas, dirtyRect, ruler);

            canvas.RestoreState();
        }

        private void DrawRulers(ICanvas canvas, RectF bounds, double ruler)
        {
            var spacing = Math.Max(8, owner.GridSpacing);
            var majorStep = spacing * Math.Max(1, owner.MajorLineEvery);
            var worldLeft = WorkflowSurfaceMath.GridWorldLeft(owner.ScrollOffsetX, owner.ContentOffsetX);
            var worldTop = WorkflowSurfaceMath.GridWorldTop(owner.ScrollOffsetY, owner.ContentOffsetY);
            var worldRight = worldLeft + bounds.Width;
            var worldBottom = worldTop + bounds.Height;

            canvas.StrokeColor = Color.FromArgb("#3A3D40");
            canvas.StrokeSize = 1f;
            canvas.DrawLine((float)ruler, 0, (float)ruler, bounds.Height);
            canvas.DrawLine(0, (float)ruler, bounds.Width, (float)ruler);

            var firstVertical = WorkflowSurfaceMath.GridFirstLine(worldLeft, spacing);
            for (var value = firstVertical; value <= worldRight + spacing; value += spacing)
            {
                var x = (float)WorkflowSurfaceMath.GridX(value, worldLeft, ruler);
                if (x < ruler)
                {
                    continue;
                }

                var isMajor = IsMajorLine(value, majorStep);
                var tickLength = isMajor ? ruler - 6 : Math.Max(6, ruler * 0.35);
                canvas.StrokeColor = IsNearZero(value)
                    ? Color.FromArgb("#4D4D4D")
                    : Color.FromArgb("#555555");
                canvas.StrokeSize = IsNearZero(value) ? 1.2f : 1f;
                canvas.DrawLine(x, (float)ruler, x, (float)(ruler - tickLength));

                if (isMajor)
                {
                    DrawLabel(canvas, value, x + 3, 10f);
                }
            }

            var firstHorizontal = WorkflowSurfaceMath.GridFirstLine(worldTop, spacing);
            for (var value = firstHorizontal; value <= worldBottom + spacing; value += spacing)
            {
                var y = (float)WorkflowSurfaceMath.GridY(value, worldTop, ruler);
                if (y < ruler)
                {
                    continue;
                }

                var isMajor = IsMajorLine(value, majorStep);
                var tickLength = isMajor ? ruler - 6 : Math.Max(6, ruler * 0.35);
                canvas.StrokeColor = IsNearZero(value)
                    ? Color.FromArgb("#4D4D4D")
                    : Color.FromArgb("#555555");
                canvas.StrokeSize = IsNearZero(value) ? 1.2f : 1f;
                canvas.DrawLine((float)ruler, y, (float)(ruler - tickLength), y);

                if (isMajor)
                {
                    // DrawString baseline positioning: offset by font size so text body starts below tick line
                    DrawLabel(canvas, value, 3, y + 10);
                }
            }
        }

        private static void DrawLabel(ICanvas canvas, double value, float x, float y)
        {
            canvas.FontSize = 10;
            canvas.FontColor = Color.FromArgb("#888888");
            canvas.DrawString(FormatGridValue(value), x, y, HorizontalAlignment.Left);
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
    }

    private static bool IsMajorLine(double value, double majorStep)
        => majorStep > 0 && (Math.Abs(value % majorStep) < MajorLineEpsilon
            || Math.Abs(value % majorStep - majorStep) < MajorLineEpsilon
            || Math.Abs(value % majorStep + majorStep) < MajorLineEpsilon);

    private static bool IsNearZero(double value)
        => Math.Abs(value) < MajorLineEpsilon;
}
