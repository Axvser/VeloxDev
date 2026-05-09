namespace Demo.Controls;

public sealed class PolylineCurveView : GraphicsView
{
    public static readonly BindableProperty StartLeftProperty = BindableProperty.Create(nameof(StartLeft), typeof(double), typeof(PolylineCurveView), 0d, propertyChanged: OnInvalidateRequested);
    public static readonly BindableProperty StartTopProperty = BindableProperty.Create(nameof(StartTop), typeof(double), typeof(PolylineCurveView), 0d, propertyChanged: OnInvalidateRequested);
    public static readonly BindableProperty EndLeftProperty = BindableProperty.Create(nameof(EndLeft), typeof(double), typeof(PolylineCurveView), 0d, propertyChanged: OnInvalidateRequested);
    public static readonly BindableProperty EndTopProperty = BindableProperty.Create(nameof(EndTop), typeof(double), typeof(PolylineCurveView), 0d, propertyChanged: OnInvalidateRequested);
    public static readonly BindableProperty CanRenderProperty = BindableProperty.Create(nameof(CanRender), typeof(bool), typeof(PolylineCurveView), true, propertyChanged: OnInvalidateRequested);
    public static readonly BindableProperty LineColorProperty = BindableProperty.Create(nameof(LineColor), typeof(Color), typeof(PolylineCurveView), Color.FromArgb("#22D3EE"), propertyChanged: OnInvalidateRequested);

    public PolylineCurveView()
    {
        InputTransparent = true;
        Drawable = new PolylineDrawable(this);
    }

    public double StartLeft { get => (double)GetValue(StartLeftProperty); set => SetValue(StartLeftProperty, value); }
    public double StartTop { get => (double)GetValue(StartTopProperty); set => SetValue(StartTopProperty, value); }
    public double EndLeft { get => (double)GetValue(EndLeftProperty); set => SetValue(EndLeftProperty, value); }
    public double EndTop { get => (double)GetValue(EndTopProperty); set => SetValue(EndTopProperty, value); }
    public bool CanRender { get => (bool)GetValue(CanRenderProperty); set => SetValue(CanRenderProperty, value); }
    public Color LineColor { get => (Color)GetValue(LineColorProperty); set => SetValue(LineColorProperty, value); }

    private static void OnInvalidateRequested(BindableObject bindable, object? oldValue, object? newValue)
    {
        if (bindable is PolylineCurveView view)
        {
            view.Invalidate();
        }
    }

    private sealed class PolylineDrawable(PolylineCurveView owner) : IDrawable
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
            var middleX = startX + ((endX - startX) / 2f);

            canvas.StrokeColor = owner.LineColor;
            canvas.StrokeSize = 4;
            canvas.DrawLine(startX, startY, middleX, startY);
            canvas.DrawLine(middleX, startY, middleX, endY);
            canvas.DrawLine(middleX, endY, endX, endY);

            canvas.FillColor = Color.FromArgb("#67E8F9");
            canvas.FillCircle(startX, startY, 5);
            canvas.FillCircle(endX, endY, 5);
        }
    }
}
