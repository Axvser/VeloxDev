// VeloxDev customization: Adjust drawing while preserving the bindable endpoint properties used by the tree template.
using VeloxDev.WorkflowSystem;

namespace Demo.Controls;

public partial class LinkView : ContentView
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

    public LinkView()
    {
        InitializeComponent();
        PART_Graphics.Drawable = new LinkDrawable(this);
        BindingContextChanged += (_, _) => UpdateVisual();
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
            typeof(LinkView),
            defaultValue,
            propertyChanged: static (bindable, _, _) =>
                ((LinkView)bindable).UpdateVisual());

    private void UpdateVisual()
    {
        // InputTransparent is set to True in XAML — never override so links
        // remain passive and never block pointer events on the canvas.
        PART_Graphics.Invalidate();
    }

    private bool IsVirtualLink
        => IsVirtual
            || BindingContext is IWorkflowLinkViewModel
            {
                Sender.Parent: null,
                Receiver.Parent: null
            };

    private sealed class LinkDrawable(LinkView owner) : IDrawable
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
            var firstTurnX = startX + stub;
            var secondTurnX = endX - stub;

            // GraphicsView clips drawing to its own bounds (0,0)-(W,H).
            // When link endpoints are in other quadrants (negative coordinates),
            // the line segments are clipped and invisible. Compute the bounding
            // box of all line points, shift them to non-negative coordinates,
            // and translate the GraphicsView to compensate — keeping the line
            // at the correct visual position on the canvas.
            var minX = Math.Min(startX, Math.Min(firstTurnX, Math.Min(secondTurnX, endX)));
            var minY = Math.Min(startY, endY);
            float offsetX = 0, offsetY = 0;
            if (minX < 0f) offsetX = -minX;
            if (minY < 0f) offsetY = -minY;

            if (offsetX > 0f || offsetY > 0f)
            {
                startX += offsetX;
                endX += offsetX;
                firstTurnX += offsetX;
                secondTurnX += offsetX;
                startY += offsetY;
                endY += offsetY;
                owner.PART_Graphics.TranslationX = -offsetX;
                owner.PART_Graphics.TranslationY = -offsetY;
            }
            else
            {
                owner.PART_Graphics.TranslationX = 0f;
                owner.PART_Graphics.TranslationY = 0f;
            }

            canvas.StrokeColor = color;
            canvas.StrokeSize = 2f;
            canvas.StrokeDashPattern = owner.IsVirtualLink ? [4, 2] : null;
            canvas.DrawLine(startX, startY, firstTurnX, startY);
            canvas.DrawLine(firstTurnX, startY, secondTurnX, endY);
            canvas.DrawLine(secondTurnX, endY, endX, endY);

            if (!owner.IsVirtualLink)
            {
                DrawArrowhead(canvas, secondTurnX, endY, endX, endY, color);
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
            if (canvas is null || color is null) return;
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
