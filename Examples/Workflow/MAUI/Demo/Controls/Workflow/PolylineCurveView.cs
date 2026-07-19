namespace Demo.Controls;

public sealed class PolylineCurveView : GraphicsView
{
    public static readonly BindableProperty StartLeftProperty = BindableProperty.Create(nameof(StartLeft), typeof(double), typeof(PolylineCurveView), 0d, propertyChanged: OnInvalidateRequested);
    public static readonly BindableProperty StartTopProperty = BindableProperty.Create(nameof(StartTop), typeof(double), typeof(PolylineCurveView), 0d, propertyChanged: OnInvalidateRequested);
    public static readonly BindableProperty EndLeftProperty = BindableProperty.Create(nameof(EndLeft), typeof(double), typeof(PolylineCurveView), 0d, propertyChanged: OnInvalidateRequested);
    public static readonly BindableProperty EndTopProperty = BindableProperty.Create(nameof(EndTop), typeof(double), typeof(PolylineCurveView), 0d, propertyChanged: OnInvalidateRequested);
    public static readonly BindableProperty ContentOffsetXProperty = BindableProperty.Create(nameof(ContentOffsetX), typeof(double), typeof(PolylineCurveView), 0d, propertyChanged: OnInvalidateRequested);
    public static readonly BindableProperty ContentOffsetYProperty = BindableProperty.Create(nameof(ContentOffsetY), typeof(double), typeof(PolylineCurveView), 0d, propertyChanged: OnInvalidateRequested);
    public static readonly BindableProperty CanRenderProperty = BindableProperty.Create(nameof(CanRender), typeof(bool), typeof(PolylineCurveView), true, propertyChanged: OnInvalidateRequested);
    public static readonly BindableProperty IsVirtualProperty = BindableProperty.Create(nameof(IsVirtual), typeof(bool), typeof(PolylineCurveView), false, propertyChanged: OnInvalidateRequested);
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
    public double ContentOffsetX { get => (double)GetValue(ContentOffsetXProperty); set => SetValue(ContentOffsetXProperty, value); }
    public double ContentOffsetY { get => (double)GetValue(ContentOffsetYProperty); set => SetValue(ContentOffsetYProperty, value); }
    public bool CanRender { get => (bool)GetValue(CanRenderProperty); set => SetValue(CanRenderProperty, value); }
    public bool IsVirtual { get => (bool)GetValue(IsVirtualProperty); set => SetValue(IsVirtualProperty, value); }
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
            if (canvas is null) return;
            if (!owner.CanRender) return;

            var color = owner.LineColor;
            if (color is null) return;

            var startX = (float)owner.StartLeft;
            var startY = (float)owner.StartTop;
            var endX = (float)owner.EndLeft;
            var endY = (float)owner.EndTop;
            const float phi = 0.6180339887f;
            var stub = ((endX - startX) / 2f) * (1f - phi);
            var p1X = startX + stub;
            var p2X = endX - stub;

            // GraphicsView clips drawing to its own bounds (0,0)-(W,H).
            // Offset all coordinates to non-negative and translate the view
            // so the line remains at the correct visual position.
            var minX = Math.Min(startX, Math.Min(p1X, Math.Min(p2X, endX)));
            var minY = Math.Min(startY, endY);
            float offsetX = 0, offsetY = 0;
            if (minX < 0f) offsetX = -minX;
            if (minY < 0f) offsetY = -minY;

            if (offsetX > 0f || offsetY > 0f)
            {
                startX += offsetX;
                endX += offsetX;
                p1X += offsetX;
                p2X += offsetX;
                startY += offsetY;
                endY += offsetY;
                owner.TranslationX = -offsetX;
                owner.TranslationY = -offsetY;
            }
            else
            {
                owner.TranslationX = 0f;
                owner.TranslationY = 0f;
            }

            canvas.StrokeColor = color;
            canvas.StrokeSize = 4;
            canvas.StrokeDashPattern = owner.IsVirtual ? [4, 2] : null;
            canvas.DrawLine(startX, startY, p1X, startY);
            canvas.DrawLine(p1X, startY, p2X, endY);
            canvas.DrawLine(p2X, endY, endX, endY);

            if (!owner.IsVirtual)
            {
                DrawArrowhead(canvas, p2X, endY, endX, endY, color);
            }

            canvas.StrokeDashPattern = null;
        }

        private static void DrawArrowhead(ICanvas canvas, float fromX, float fromY, float tipX, float tipY, Color color)
        {
            if (canvas is null || color is null) return;

            var dx = tipX - fromX;
            var dy = tipY - fromY;
            var length = MathF.Sqrt((dx * dx) + (dy * dy));
            if (length <= float.Epsilon) return;

            dx /= length;
            dy /= length;
            const float arrowLength = 12f;
            const float arrowWidth = 8f;
            var normalX = -dy;
            var normalY = dx;
            var baseX = tipX - (dx * arrowLength);
            var baseY = tipY - (dy * arrowLength);
            var leftX = baseX + (normalX * (arrowWidth / 2f));
            var leftY = baseY + (normalY * (arrowWidth / 2f));
            var rightX = baseX - (normalX * (arrowWidth / 2f));
            var rightY = baseY - (normalY * (arrowWidth / 2f));

            var arrow = new PathF();
            arrow.MoveTo(tipX, tipY);
            arrow.LineTo(leftX, leftY);
            arrow.LineTo(rightX, rightY);
            arrow.Close();

            canvas.FillColor = color;
            canvas.FillPath(arrow);
        }
    }
}
