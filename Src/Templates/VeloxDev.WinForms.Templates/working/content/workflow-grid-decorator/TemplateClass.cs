// VeloxDev customization: The workflow grid. This visual decorator layers a world grid, the surface content
// (the scroll viewer and canvas supplied by the tree template) and the viewport-fixed ruler bands.
using System.Drawing;
using System.Drawing.Drawing2D;
using DrawingSize = System.Drawing.Size;
using System.Globalization;
using VeloxDev.WinForms.Xaml;
using VeloxDev.WinForms.Xaml.Visuals;
using VeloxDev.WorkflowSystem;
using DrawingPen = System.Drawing.Pen;

namespace TemplateNamespace;

/// <summary>
/// A workflow surface decorator with three explicit visual layers: the world grid behind the content, the
/// content itself, and the ruler bands above it.
/// </summary>
public sealed class TemplateClass : VisualElement, IWorkflowGridDecorator
{
    private const double MajorLineEpsilon = 0.001;

    private readonly Layer _gridLayer;
    private readonly Layer _rulerLayer;
    private readonly Font _labelFont = new("Segoe UI", 13f, GraphicsUnit.Pixel);
    private VisualElement? _content;

    /// <summary>Whether the decorator paints its own surface background (templates with a surface Border set this false).</summary>
    public static readonly DependencyProperty DrawSurfaceProperty = DependencyProperty.Register(
        nameof(DrawSurface), typeof(bool), typeof(TemplateClass),
        new PropertyMetadata(true) { AffectsRender = true });

    public static readonly DependencyProperty RulerThicknessProperty = DependencyProperty.Register(
        nameof(RulerThickness), typeof(double), typeof(TemplateClass),
        new PropertyMetadata(28d) { AffectsMeasure = true, AffectsRender = true });

    public static readonly DependencyProperty GridSpacingProperty = DependencyProperty.Register(
        nameof(GridSpacing), typeof(double), typeof(TemplateClass),
        new PropertyMetadata(ParseGridValue("TemplateGridSpacing")) { AffectsRender = true });

    public static readonly DependencyProperty MajorLineEveryProperty = DependencyProperty.Register(
        nameof(MajorLineEvery), typeof(int), typeof(TemplateClass),
        new PropertyMetadata(ParseInt("TemplateMajorLineEvery")) { AffectsRender = true });

    public static readonly DependencyProperty ScrollOffsetXProperty = DependencyProperty.Register(
        nameof(ScrollOffsetX), typeof(double), typeof(TemplateClass),
        new PropertyMetadata(0d) { AffectsRender = true });

    public static readonly DependencyProperty ScrollOffsetYProperty = DependencyProperty.Register(
        nameof(ScrollOffsetY), typeof(double), typeof(TemplateClass),
        new PropertyMetadata(0d) { AffectsRender = true });

    public static readonly DependencyProperty ContentOffsetXProperty = DependencyProperty.Register(
        nameof(ContentOffsetX), typeof(double), typeof(TemplateClass),
        new PropertyMetadata(0d) { AffectsRender = true });

    public static readonly DependencyProperty ContentOffsetYProperty = DependencyProperty.Register(
        nameof(ContentOffsetY), typeof(double), typeof(TemplateClass),
        new PropertyMetadata(0d) { AffectsRender = true });

    /// <summary>Creates the decorator and its grid/ruler layers.</summary>
    public TemplateClass()
    {
        _gridLayer = new Layer(this, grid: true) { IsHitTestVisible = false };
        _rulerLayer = new Layer(this, grid: false) { IsHitTestVisible = false };
        base.Add(_gridLayer);
        base.Add(_rulerLayer);
    }

    /// <summary>Assigns the single content child and returns it.</summary>
    public new T Add<T>(T child) where T : VisualElement
    {
        Content = child;
        return child;
    }

    /// <summary>The surface content, arranged between the grid and the rulers.</summary>
    public VisualElement? Content
    {
        get => _content;
        set
        {
            if (ReferenceEquals(_content, value))
            {
                return;
            }

            if (_content is not null)
            {
                RemoveChild(_content);
            }

            _content = value;
            if (value is not null)
            {
                // grid (0), content (1), ruler (2): children render in insertion order.
                base.Add(value);
                Children.Remove(value);
                Children.Insert(1, value);
            }

            InvalidateMeasure();
        }
    }

    /// <summary>Whether the decorator paints its own surface background.</summary>
    public bool DrawSurface
    {
        get => (bool)GetValue(DrawSurfaceProperty)!;
        set => SetValue(DrawSurfaceProperty, value);
    }

    /// <summary>Ruler band thickness in logical pixels.</summary>
    public double RulerThickness
    {
        get => (double)GetValue(RulerThicknessProperty)!;
        set => SetValue(RulerThicknessProperty, value);
    }

    /// <summary>World distance between minor grid lines.</summary>
    public double GridSpacing
    {
        get => (double)GetValue(GridSpacingProperty)!;
        set => SetValue(GridSpacingProperty, value);
    }

    /// <summary>How many minor lines make a major one.</summary>
    public int MajorLineEvery
    {
        get => (int)GetValue(MajorLineEveryProperty)!;
        set => SetValue(MajorLineEveryProperty, value);
    }

    /// <inheritdoc />
    public double ScrollOffsetX
    {
        get => (double)GetValue(ScrollOffsetXProperty)!;
        set => SetValue(ScrollOffsetXProperty, value);
    }

    /// <inheritdoc />
    public double ScrollOffsetY
    {
        get => (double)GetValue(ScrollOffsetYProperty)!;
        set => SetValue(ScrollOffsetYProperty, value);
    }

    /// <inheritdoc />
    public double ContentOffsetX
    {
        get => (double)GetValue(ContentOffsetXProperty)!;
        set => SetValue(ContentOffsetXProperty, value);
    }

    /// <inheritdoc />
    public double ContentOffsetY
    {
        get => (double)GetValue(ContentOffsetYProperty)!;
        set => SetValue(ContentOffsetYProperty, value);
    }

    /// <inheritdoc />
    public double RulerBand => Math.Max(0d, RulerThickness);

    /// <inheritdoc />
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property.Metadata.AffectsRender)
        {
            _gridLayer.InvalidateVisual();
            _rulerLayer.InvalidateVisual();
        }
    }

    /// <inheritdoc />
    protected override DrawingSize MeasureOverride(DrawingSize availableSize)
    {
        _content?.Measure(availableSize);
        _gridLayer.Measure(availableSize);
        _rulerLayer.Measure(availableSize);
        return availableSize;
    }

    /// <inheritdoc />
    protected override void ArrangeOverride(DrawingSize finalSize)
    {
        var bounds = new RectangleF(0f, 0f, finalSize.Width, finalSize.Height);
        _gridLayer.Arrange(bounds);
        _content?.Arrange(bounds);
        _rulerLayer.Arrange(bounds);
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _labelFont.Dispose();
        }

        base.Dispose(disposing);
    }

    private static double ParseGridValue(string token)
    {
        var text = token.Trim();
        if (text.EndsWith("d", StringComparison.OrdinalIgnoreCase))
        {
            text = text.Substring(0, text.Length - 1);
        }

        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : 40d;
    }

    private static int ParseInt(string token)
        => int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : 5;

    private static string FormatGridValue(double value)
    {
        var abs = Math.Abs(value);
        if (abs < 10000d)
        {
            return Math.Round(value).ToString(CultureInfo.InvariantCulture);
        }

        if (abs < 1000000d)
        {
            return Math.Round(value / 1000d, 1).ToString(CultureInfo.InvariantCulture) + "K";
        }

        return Math.Round(value / 1000000d, 1).ToString(CultureInfo.InvariantCulture) + "M";
    }

    private static Color ParseColor(string token) => XamlRuntime.ParseColor(token);

    private sealed class Layer(TemplateClass owner, bool grid) : VisualElement
    {
        private readonly Color _surface = ParseColor("TemplateGridBackground");
        private readonly Color _minor = ParseColor("TemplateMinorGridColor");
        private readonly Color _major = ParseColor("TemplateMajorGridColor");
        private readonly Color _axis = ParseColor("TemplateAxisColor");
        private readonly Color _rulerBackground = ParseColor("TemplateRulerBackground");
        private readonly Color _rulerLabel = ParseColor("TemplateRulerLabelColor");
        private readonly Color _rulerTick = ParseColor("TemplateRulerTickColor");
        private readonly Color _rulerDivider = ParseColor("TemplateRulerDividerColor");

        /// <inheritdoc />
        protected override void OnRender(Graphics graphics)
        {
            var width = Bounds.Width;
            var height = Bounds.Height;
            if (width <= 0f || height <= 0f)
            {
                return;
            }

            if (grid)
            {
                DrawGrid(graphics, width, height);
            }
            else
            {
                DrawRulers(graphics, width, height);
            }
        }

        private void DrawGrid(Graphics graphics, float width, float height)
        {
            if (owner.DrawSurface)
            {
                using var background = new SolidBrush(_surface);
                graphics.FillRectangle(background, 0f, 0f, width, height);
            }

            var spacing = Math.Max(8d, owner.GridSpacing);
            var majorStep = spacing * Math.Max(1, owner.MajorLineEvery);
            var worldLeft = WorkflowSurfaceMath.GridCanvasLeft(owner.ScrollOffsetX, owner.ContentOffsetX);
            var worldTop = WorkflowSurfaceMath.GridCanvasTop(owner.ScrollOffsetY, owner.ContentOffsetY);
            var worldRight = worldLeft + width;
            var worldBottom = worldTop + height;

            graphics.SmoothingMode = SmoothingMode.None;
            using var minorPen = new DrawingPen(_minor, 1f);
            using var majorPen = new DrawingPen(_major, 1f);
            using var axisPen = new DrawingPen(_axis, 1.2f);

            var firstVertical = WorkflowSurfaceMath.GridFirstLine(worldLeft, spacing);
            for (var value = firstVertical; value <= worldRight + spacing; value += spacing)
            {
                var x = (float)WorkflowSurfaceMath.GridX(value, worldLeft, 0);
                var pen = IsNearZero(value) ? axisPen : IsMajorLine(value, majorStep) ? majorPen : minorPen;
                graphics.DrawLine(pen, x, 0f, x, height);
            }

            var firstHorizontal = WorkflowSurfaceMath.GridFirstLine(worldTop, spacing);
            for (var value = firstHorizontal; value <= worldBottom + spacing; value += spacing)
            {
                var y = (float)WorkflowSurfaceMath.GridY(value, worldTop, 0);
                var pen = IsNearZero(value) ? axisPen : IsMajorLine(value, majorStep) ? majorPen : minorPen;
                graphics.DrawLine(pen, 0f, y, width, y);
            }
        }

        private void DrawRulers(Graphics graphics, float width, float height)
        {
            var ruler = (float)owner.RulerBand;
            if (ruler <= 0f)
            {
                return;
            }

            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var rulerBrush = new SolidBrush(_rulerBackground);
            using var dividerPen = new DrawingPen(_rulerDivider, 1f);
            using var tickPen = new DrawingPen(_rulerTick, 1f);
            using var axisPen = new DrawingPen(_axis, 1f);
            using var labelBrush = new SolidBrush(_rulerLabel);

            graphics.FillRectangle(rulerBrush, 0f, 0f, width, ruler);
            graphics.FillRectangle(rulerBrush, 0f, 0f, ruler, height);
            graphics.DrawLine(dividerPen, ruler, 0f, ruler, height);
            graphics.DrawLine(dividerPen, 0f, ruler, width, ruler);

            var spacing = Math.Max(8d, owner.GridSpacing);
            var majorStep = spacing * Math.Max(1, owner.MajorLineEvery);
            var worldLeft = WorkflowSurfaceMath.GridCanvasLeft(owner.ScrollOffsetX, owner.ContentOffsetX);
            var worldTop = WorkflowSurfaceMath.GridCanvasTop(owner.ScrollOffsetY, owner.ContentOffsetY);
            var worldRight = worldLeft + width;
            var worldBottom = worldTop + height;

            var firstVertical = WorkflowSurfaceMath.GridFirstLine(worldLeft, spacing);
            for (var value = firstVertical; value <= worldRight + spacing; value += spacing)
            {
                var x = (float)WorkflowSurfaceMath.GridX(value, worldLeft, 0);
                if (x < ruler)
                {
                    continue;
                }

                var isMajor = IsMajorLine(value, majorStep);
                var tickLength = isMajor ? Math.Max(1f, ruler - 6f) : Math.Max(6f, ruler * 0.35f);
                var pen = IsNearZero(value) ? axisPen : tickPen;
                graphics.DrawLine(pen, x, ruler, x, ruler - tickLength);
                if (isMajor)
                {
                    graphics.DrawString(
                        FormatGridValue(value), owner._labelFont, labelBrush, x + 3f, 2f);
                }
            }

            var firstHorizontal = WorkflowSurfaceMath.GridFirstLine(worldTop, spacing);
            for (var value = firstHorizontal; value <= worldBottom + spacing; value += spacing)
            {
                var y = (float)WorkflowSurfaceMath.GridY(value, worldTop, 0);
                if (y < ruler)
                {
                    continue;
                }

                var isMajor = IsMajorLine(value, majorStep);
                var tickLength = isMajor ? Math.Max(1f, ruler - 6f) : Math.Max(6f, ruler * 0.35f);
                var pen = IsNearZero(value) ? axisPen : tickPen;
                graphics.DrawLine(pen, ruler, y, ruler - tickLength, y);
                if (isMajor)
                {
                    graphics.DrawString(
                        FormatGridValue(value), owner._labelFont, labelBrush, 3f, y + 2f);
                }
            }
        }

        private static bool IsMajorLine(double value, double majorStep)
        {
            if (majorStep <= 0d)
            {
                return false;
            }

            var remainder = value % majorStep;
            return Math.Abs(remainder) < MajorLineEpsilon
                   || Math.Abs(remainder - majorStep) < MajorLineEpsilon
                   || Math.Abs(remainder + majorStep) < MajorLineEpsilon;
        }

        private static bool IsNearZero(double value) => Math.Abs(value) < MajorLineEpsilon;
    }
}
