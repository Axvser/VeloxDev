// VeloxDev customization: Customize line geometry, color, and thickness here.
using Microsoft.Maui.Controls.Shapes;
using VeloxDev.WorkflowSystem;

namespace Demo.Controls;

/// <summary>
/// Cubic Bézier connection that leaves each port horizontally.
/// The view only paints: it publishes its curve for hit-testing and implements
/// <see cref="ILinkHighlight"/> so the interaction hub lights it on hover. It handles no input itself.
/// </summary>
public partial class LinkView : ContentView, ILinkHighlight
{
    // 两个控制点的最小水平拉出量，画与命中都读它 —— 改了公式要两处一起改。
    private const double MinimumPull = 40;

    // 描边宽度，以及高亮时的宽度（与其余六家同值）。
    private const double RestingThickness = 2;
    private const double HighlightThickness = 3.5;

    // 视图盒子的余量：画布坐标有负值，盒子必须把它们一起罩住（见 EnsureBox）。
    private const double BoxMargin = 2048;

    private Rect _box;

    // 这个视图上一条发布过曲线的连线；换宿主时先撤回，池化视图不能留下替别人回答命中的旧曲线。
    private IWorkflowLinkViewModel? _publishedLink;

    public LinkView()
    {
        InitializeComponent();
        BindingContextChanged += OnBindingContextChanged;

        PART_Curve.Stroke = new SolidColorBrush(Colors.Lime);
        PART_Curve.StrokeThickness = 8;
        PART_Curve.Data = BuildGeometry(20, 20, 300, 120);
    }

    #region Bindable properties

    public static readonly BindableProperty StartHorizontalProperty = BindableProperty.Create(
        nameof(StartHorizontal), typeof(double), typeof(LinkView), 0d, propertyChanged: OnGeometryChanged);
    public static readonly BindableProperty StartVerticalProperty = BindableProperty.Create(
        nameof(StartVertical), typeof(double), typeof(LinkView), 0d, propertyChanged: OnGeometryChanged);
    public static readonly BindableProperty EndHorizontalProperty = BindableProperty.Create(
        nameof(EndHorizontal), typeof(double), typeof(LinkView), 0d, propertyChanged: OnGeometryChanged);
    public static readonly BindableProperty EndVerticalProperty = BindableProperty.Create(
        nameof(EndVertical), typeof(double), typeof(LinkView), 0d, propertyChanged: OnGeometryChanged);
    public static readonly BindableProperty CanRenderProperty = BindableProperty.Create(
        nameof(CanRender), typeof(bool), typeof(LinkView), true, propertyChanged: OnGeometryChanged);
    public static readonly BindableProperty LineColorProperty = BindableProperty.Create(
        nameof(LineColor), typeof(Color), typeof(LinkView), Color.FromArgb("#DDFFFFFF"), propertyChanged: OnPaintChanged);
    public static readonly BindableProperty HighlightColorProperty = BindableProperty.Create(
        nameof(HighlightColor), typeof(Color), typeof(LinkView), Color.FromArgb("#FFFFFFFF"), propertyChanged: OnPaintChanged);
    public static readonly BindableProperty IsHighlightedProperty = BindableProperty.Create(
        nameof(IsHighlighted), typeof(bool), typeof(LinkView), false, propertyChanged: OnPaintChanged);

    /// <summary>Canvas-local horizontal position of the sender's port.</summary>
    public double StartHorizontal { get => (double)GetValue(StartHorizontalProperty); set => SetValue(StartHorizontalProperty, value); }

    /// <summary>Canvas-local vertical position of the sender's port.</summary>
    public double StartVertical { get => (double)GetValue(StartVerticalProperty); set => SetValue(StartVerticalProperty, value); }

    /// <summary>Canvas-local horizontal position of the receiver's port.</summary>
    public double EndHorizontal { get => (double)GetValue(EndHorizontalProperty); set => SetValue(EndHorizontalProperty, value); }

    /// <summary>Canvas-local vertical position of the receiver's port.</summary>
    public double EndVertical { get => (double)GetValue(EndVerticalProperty); set => SetValue(EndVerticalProperty, value); }

    /// <summary>Whether this link currently draws anything. Bound to the link view model's own visibility.</summary>
    public bool CanRender { get => (bool)GetValue(CanRenderProperty); set => SetValue(CanRenderProperty, value); }

    /// <summary>Colour of the resting line.</summary>
    public Color LineColor { get => (Color)GetValue(LineColorProperty); set => SetValue(LineColorProperty, value); }

    /// <summary>The soft light the link turns into while it is hovered.</summary>
    public Color HighlightColor { get => (Color)GetValue(HighlightColorProperty); set => SetValue(HighlightColorProperty, value); }

    /// <inheritdoc />
    public bool IsHighlighted { get => (bool)GetValue(IsHighlightedProperty); set => SetValue(IsHighlightedProperty, value); }

    private static void OnGeometryChanged(BindableObject bindable, object oldValue, object newValue)
        => ((LinkView)bindable).Rebuild();

    private static void OnPaintChanged(BindableObject bindable, object oldValue, object newValue)
        => ((LinkView)bindable).ApplyPaint();

    #endregion

    private void OnBindingContextChanged(object? sender, EventArgs e)
    {
        // 池化视图换宿主：上一条线的曲线必须撤回，之后由下一次 Rebuild 发布新的那条
        Retract();
        Rebuild();
    }

    #region Paint

    // 画与命中读同一个 pull：这里推一遍控制点，下面 BuildCubic 用同一个公式推一遍采样点。
    // 换形状时两处要一起换，否则「画出来的」和「能点中的」会分叉。
    private void Rebuild()
    {
        var startX = StartHorizontal;
        var startY = StartVertical;
        var endX = EndHorizontal;
        var endY = EndVertical;
        var link = BindingContext as IWorkflowLinkViewModel;

        // 还没量好（NaN 锚点）、不可见、或已被池回收：撤回曲线，什么也不画 ——
        // 留着的旧曲线会替一条已经不在这里的线回答命中
        if (link is null || !CanRender
            || double.IsNaN(startX) || double.IsNaN(startY)
            || double.IsNaN(endX) || double.IsNaN(endY))
        {
            Retract();
            PART_Curve.Data = null;
            PART_Halo.Data = null;
            return;
        }

        var curve = LinkCurve.BuildCubic(startX, startY, endX, endY, MinimumPull);
        Publish(link, curve);

        // 几何按盒子的原点烘进视图的局部坐标：Path 只画在自己盒子（布局槽）里，落在盒子外的部分会被裁掉。
        var box = EnsureBox(curve.Bounds);
        PART_Curve.Data = BuildGeometry(startX - box.Left, startY - box.Top, endX - box.Left, endY - box.Top);
        PART_Halo.Data = BuildGeometry(startX - box.Left, startY - box.Top, endX - box.Left, endY - box.Top);
        ApplyPaint();
    }

    /// <summary>
    /// The layout box this view keeps, and the canvas-local origin it is baked against.
    ///
    /// The box has to be a <b>stable</b> region, not the curve's own bounds: a shape is clipped to its box, and a
    /// bounds write from inside the view lands a layout pass (or several) late — measured, a view pinned to its
    /// own curve's bounds keeps an older, smaller box and its curve is clipped away while the geometry moves.
    /// Size it to the canvas plus a margin instead, and grow it only if a link wanders outside that region.
    /// </summary>
    private Rect EnsureBox(WorkflowBounds curveBounds)
    {
        var host = Parent as VisualElement;
        var width = Math.Max(1, host?.Width ?? 0);
        var height = Math.Max(1, host?.Height ?? 0);

        var wanted = new Rect(
            Math.Min(-BoxMargin, curveBounds.Left - BoxMargin),
            Math.Min(-BoxMargin, curveBounds.Top - BoxMargin),
            Math.Max(width + BoxMargin, curveBounds.Right + BoxMargin) - Math.Min(-BoxMargin, curveBounds.Left - BoxMargin),
            Math.Max(height + BoxMargin, curveBounds.Bottom + BoxMargin) - Math.Min(-BoxMargin, curveBounds.Top - BoxMargin));

        if (wanted != _box)
        {
            _box = wanted;
            AbsoluteLayout.SetLayoutBounds(this, wanted);
        }

        return _box;
    }

    private void ApplyPaint()
    {
        if (BindingContext is not IWorkflowLinkViewModel link)
        {
            return;
        }

        var color = IsHighlighted ? HighlightColor : LineColor;
        var thickness = IsHighlighted ? HighlightThickness : RestingThickness;
        // 空集合 = 实线：Shape.StrokeDashArray 的类型不可空
        var dash = IsVirtual(link) ? new DoubleCollection { 4, 2 } : new DoubleCollection();

        PART_Curve.Stroke = new SolidColorBrush(color);
        PART_Curve.StrokeThickness = thickness;
        PART_Curve.StrokeDashArray = dash;
        PART_Curve.StrokeLineCap = PenLineCap.Round;

        // 高亮是一圈更宽更淡的同色光晕，不是换色：静息线本来就近白，换色相在别家都试过、都被否了
        // ⚠ 已知缺陷（2026-10-03）：这段属性改完，这一层不一定**重画** —— hub 确实把 IsHighlighted 置真了，
        // 屏幕却一个像素都不变（同一次运行里 Delete 立刻有效）。见 memory/modules/WorkflowSystem/adapters/maui.md §四·14。
        PART_Halo.Stroke = new SolidColorBrush(color);
        PART_Halo.StrokeThickness = thickness + 8;
        PART_Halo.StrokeDashArray = dash;
        PART_Halo.StrokeLineCap = PenLineCap.Round;
        PART_Halo.Opacity = 0.18;
        PART_Halo.IsVisible = IsHighlighted;
    }

    // 橡皮筋两端还没有父节点；这条同时覆盖了「拖动中的虚拟连线」与宿主自己造的无主连线
    private static bool IsVirtual(IWorkflowLinkViewModel link)
        => link.Sender.Parent is null || link.Receiver.Parent is null;

    private static PathGeometry BuildGeometry(double startX, double startY, double endX, double endY)
    {
        var pull = Math.Max(MinimumPull, Math.Abs(endX - startX) * 0.5);

        var figure = new PathFigure
        {
            StartPoint = new Point(startX, startY),
            IsClosed = false,
            IsFilled = false,
        };
        figure.Segments.Add(new BezierSegment
        {
            Point1 = new Point(startX + pull, startY),
            Point2 = new Point(endX - pull, endY),
            Point3 = new Point(endX, endY),
        });

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        return geometry;
    }

    #endregion

    #region Publishing

    private void Publish(IWorkflowLinkViewModel link, LinkCurve curve)
    {
        if (!ReferenceEquals(_publishedLink, link))
        {
            _publishedLink?.PublishCurve(null);
            _publishedLink = link;
        }

        link.PublishCurve(curve, this);
    }

    private void Retract()
    {
        _publishedLink?.PublishCurve(null);
        _publishedLink = null;
    }

    #endregion
}
