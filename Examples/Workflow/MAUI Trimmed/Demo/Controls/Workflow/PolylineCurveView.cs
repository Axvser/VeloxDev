using VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace Demo.Controls;

public sealed class PolylineCurveView : GraphicsView, IWorkflowLinkRenderView
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
            view.UpdateVisualOffset();
            view.Invalidate();
        }
    }

    private void UpdateVisualOffset()
    {
        TranslationX = -ContentOffsetX;
        TranslationY = -ContentOffsetY;
    }

    private sealed class PolylineDrawable(PolylineCurveView owner) : IDrawable
    {
        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            if (!owner.CanRender)
            {
                return;
            }

            var startX = (float)(owner.StartLeft + owner.ContentOffsetX);
            var startY = (float)(owner.StartTop + owner.ContentOffsetY);
            var endX = (float)(owner.EndLeft + owner.ContentOffsetX);
            var endY = (float)(owner.EndTop + owner.ContentOffsetY);
            const float phi = 0.6180339887f;
            var stub = ((endX - startX) / 2f) * (1f - phi);
            var p1X = startX + stub;
            var p2X = endX - stub;

            canvas.StrokeColor = owner.LineColor;
            canvas.StrokeSize = 4;
            canvas.StrokeDashPattern = owner.IsVirtual ? [4, 2] : null;
            canvas.DrawLine(startX, startY, p1X, startY);
            canvas.DrawLine(p1X, startY, p2X, endY);
            canvas.DrawLine(p2X, endY, endX, endY);

            if (!owner.IsVirtual)
            {
                DrawArrowhead(canvas, p2X, endY, endX, endY, owner.LineColor);
            }

            canvas.StrokeDashPattern = null;
        }

        private static void DrawArrowhead(ICanvas canvas, float fromX, float fromY, float tipX, float tipY, Color color)
        {
            var dx = tipX - fromX;
            var dy = tipY - fromY;
            var length = MathF.Sqrt((dx * dx) + (dy * dy));
            if (length <= float.Epsilon)
            {
                return;
            }

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
