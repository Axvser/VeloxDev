// VeloxDev customization: Adjust drawing while preserving the bindable endpoint properties used by the tree template.
namespace TemplateNamespace;

public partial class TemplateClass : ContentView
{
    public static readonly BindableProperty StartLeftProperty = Create(nameof(StartLeft), 0d);
    public static readonly BindableProperty StartTopProperty = Create(nameof(StartTop), 0d);
    public static readonly BindableProperty EndLeftProperty = Create(nameof(EndLeft), 0d);
    public static readonly BindableProperty EndTopProperty = Create(nameof(EndTop), 0d);
    public static readonly BindableProperty ContentOffsetXProperty = Create(nameof(ContentOffsetX), 0d);
    public static readonly BindableProperty ContentOffsetYProperty = Create(nameof(ContentOffsetY), 0d);
    public static readonly BindableProperty CanRenderProperty = Create(nameof(CanRender), true);
    public static readonly BindableProperty IsVirtualProperty = Create(nameof(IsVirtual), false);
    public static readonly BindableProperty LineColorProperty = Create(nameof(LineColor), Color.FromArgb("#DDFFFFFF"));

    public TemplateClass()
    {
        InitializeComponent();
        PART_Graphics.Drawable = new LinkDrawable(this);
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

    private static BindableProperty Create(string name, object defaultValue)
        => BindableProperty.Create(
            name,
            defaultValue.GetType(),
            typeof(TemplateClass),
            defaultValue,
            propertyChanged: static (bindable, _, _) =>
                ((TemplateClass)bindable).UpdateVisual());

    private void UpdateVisual()
    {
        TranslationX = -ContentOffsetX;
        TranslationY = -ContentOffsetY;
        PART_Graphics.Invalidate();
    }

    private sealed class LinkDrawable(TemplateClass owner) : IDrawable
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
            var firstTurnX = startX + stub;
            var secondTurnX = endX - stub;

            canvas.StrokeColor = owner.LineColor;
            canvas.StrokeSize = 3;
            canvas.StrokeDashPattern = owner.IsVirtual ? [4, 2] : null;
            canvas.DrawLine(startX, startY, firstTurnX, startY);
            canvas.DrawLine(firstTurnX, startY, secondTurnX, endY);
            canvas.DrawLine(secondTurnX, endY, endX, endY);

            if (!owner.IsVirtual)
            {
                DrawArrowhead(canvas, secondTurnX, endY, endX, endY, owner.LineColor);
            }

            canvas.StrokeDashPattern = null;
        }

        private static void DrawArrowhead(
            ICanvas canvas,
            float fromX,
            float fromY,
            float tipX,
            float tipY,
            Color color)
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

            var arrow = new PathF();
            arrow.MoveTo(tipX, tipY);
            arrow.LineTo(
                baseX + (normalX * (arrowWidth / 2f)),
                baseY + (normalY * (arrowWidth / 2f)));
            arrow.LineTo(
                baseX - (normalX * (arrowWidth / 2f)),
                baseY - (normalY * (arrowWidth / 2f)));
            arrow.Close();

            canvas.FillColor = color;
            canvas.FillPath(arrow);
        }
    }
}
