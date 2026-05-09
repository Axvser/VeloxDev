using VeloxDev.WorkflowSystem;

namespace Demo.Controls;

public sealed class BezierCurveView : GraphicsView
{
    public static readonly BindableProperty StartLeftProperty = BindableProperty.Create(nameof(StartLeft), typeof(double), typeof(BezierCurveView), 0d, propertyChanged: OnInvalidateRequested);
    public static readonly BindableProperty StartTopProperty = BindableProperty.Create(nameof(StartTop), typeof(double), typeof(BezierCurveView), 0d, propertyChanged: OnInvalidateRequested);
    public static readonly BindableProperty EndLeftProperty = BindableProperty.Create(nameof(EndLeft), typeof(double), typeof(BezierCurveView), 0d, propertyChanged: OnInvalidateRequested);
    public static readonly BindableProperty EndTopProperty = BindableProperty.Create(nameof(EndTop), typeof(double), typeof(BezierCurveView), 0d, propertyChanged: OnInvalidateRequested);
    public static readonly BindableProperty CanRenderProperty = BindableProperty.Create(nameof(CanRender), typeof(bool), typeof(BezierCurveView), true, propertyChanged: OnInvalidateRequested);
    public static readonly BindableProperty LineColorProperty = BindableProperty.Create(nameof(LineColor), typeof(Color), typeof(BezierCurveView), Color.FromArgb("#22D3EE"), propertyChanged: OnInvalidateRequested);

    public BezierCurveView()
    {
        InputTransparent = true;
        Drawable = new CurveDrawable(this);
    }

    public double StartLeft { get => (double)GetValue(StartLeftProperty); set => SetValue(StartLeftProperty, value); }
    public double StartTop { get => (double)GetValue(StartTopProperty); set => SetValue(StartTopProperty, value); }
    public double EndLeft { get => (double)GetValue(EndLeftProperty); set => SetValue(EndLeftProperty, value); }
    public double EndTop { get => (double)GetValue(EndTopProperty); set => SetValue(EndTopProperty, value); }
    public bool CanRender { get => (bool)GetValue(CanRenderProperty); set => SetValue(CanRenderProperty, value); }
    public Color LineColor { get => (Color)GetValue(LineColorProperty); set => SetValue(LineColorProperty, value); }

    private static void OnInvalidateRequested(BindableObject bindable, object? oldValue, object? newValue)
    {
        if (bindable is BezierCurveView view)
        {
            view.Invalidate();
        }
    }

    private sealed class CurveDrawable(BezierCurveView owner) : IDrawable
    {
        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            if (!owner.CanRender)
            {
                return;
            }

            var startX = (float)owner.StartLeft;
            var startY = (float)owner.StartTop;
            var endX = (float)owner.EndLeft;
            var endY = (float)owner.EndTop;
            var controlOffset = Math.Max(80f, Math.Abs(endX - startX) * 0.45f);

            var path = new PathF();
            path.MoveTo(startX, startY);
            path.CurveTo(startX + controlOffset, startY, endX - controlOffset, endY, endX, endY);

            canvas.StrokeColor = owner.LineColor;
            canvas.StrokeSize = 4;
            canvas.DrawPath(path);

            canvas.FillColor = Color.FromArgb("#67E8F9");
            canvas.FillCircle(startX, startY, 5);
            canvas.FillCircle(endX, endY, 5);
        }
    }
}
