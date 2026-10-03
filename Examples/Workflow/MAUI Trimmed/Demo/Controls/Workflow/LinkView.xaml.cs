// VeloxDev customization: Customize line geometry, color, and thickness here.
using Microsoft.Maui.Controls.Shapes;
using VeloxDev.WorkflowSystem;

namespace Demo.Controls;

public partial class LinkView : ContentView, ILinkHighlight
{
    // 两个控制点的最小水平拉出量，画与命中都读它 —— 改了公式要两处一起改。
    private const double MinimumPull = 40;

    // 描边宽度，以及高亮时的宽度（与其余六家同值）。
    private const double RestingThickness = 2;
    private const double HighlightThickness = 3.5;

    // 视图盒子的余量：画布坐标有负值，盒子必须把它们一起罩住（见 EnsureBox）。
    private const double BoxMargin = 256;

    private Rect _box;

    // 这个视图上一条发布过曲线的连线；换宿主时先撤回，池化视图不能留下替别人回答命中的旧曲线。
    private IWorkflowLinkViewModel? _publishedLink;

    // 画法（颜色/粗细/虚线/高亮）是否需要重设，以及上一次画的时候这条线是不是虚拟连线
    private bool _paintPending = true;
    private bool _paintedVirtual;

    public LinkView()
    {
        InitializeComponent();
        BindingContextChanged += OnBindingContextChanged;
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
    {
        // 视图还没绑上连线时先记下来：ApplyPaint 要读 BindingContext，等 Rebuild 那一趟再落。
        var view = (LinkView)bindable;
        view._paintPending = true;
        view.ApplyPaint();
    }

    #endregion

    private void OnBindingContextChanged(object? sender, EventArgs e)
    {
        // 池化视图换宿主：上一条线的曲线必须撤回，之后由下一次 Rebuild 发布新的那条。
        // 画法也要重设一遍 —— 新宿主可能是另一条线（虚线/实线、别的高亮色）。
        _paintPending = true;
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

        // 控制点由 Core 的端口规则给：每个控制点沿**自己那个口**所在边的外法线拉（不往节点里折），
        // 两个端点因此是对称的 —— 谁 sender、谁 receiver 不影响曲线本身。画与命中读同一份点。
        var points = LinkCurve.PortCurvePoints(link.Sender, link.Receiver, MinimumPull);
        var curve = LinkCurve.BuildPortCubic(link.Sender, link.Receiver, MinimumPull);
        Publish(link, curve);

        // 几何按盒子的原点烘进视图的局部坐标：Path 只画在自己盒子（布局槽）里，落在盒子外的部分会被裁掉。
        var box = EnsureBox(curve.Bounds);
        PART_Curve.Data = BuildGeometry(points, box.Left, box.Top);
        PART_Halo.Data = BuildGeometry(points, box.Left, box.Top);

        // 几何每帧都在变，**画法**通常没变：拖拽期间描边色、粗细、虚线都是原样。属性写入会走
        // WinUI 的映射、让这条 Path 重新渲染，所以只在画法真的变了（或换了宿主）时才重设。
        var virtualNow = IsVirtual(link);
        if (_paintPending || _paintedVirtual != virtualNow)
        {
            ApplyPaint();
        }
    }

    /// <summary>
    /// The layout box this view keeps, and the canvas-local origin it is baked against.
    ///
    /// The box must be a region that <b>does not follow the curve</b>. Two things go wrong otherwise, both
    /// measured: a shape is clipped to its box, so a curve drawn against its own bounds gets clipped away while
    /// the geometry moves (the bounds write lands a layout pass late); and every write is a change to an
    /// <c>AbsoluteLayout</c> child, which invalidates the whole canvas — with one box write per link per frame
    /// the node drag crawls. So: the canvas plus one margin, created once, and grown only when a link actually
    /// leaves that region (after which it stays put again).
    /// </summary>
    private Rect EnsureBox(WorkflowBounds curveBounds)
    {
        var host = Parent as VisualElement;

        if (_box.Width <= 0)
        {
            _box = new Rect(
                -BoxMargin,
                -BoxMargin,
                Math.Max(1, host?.Width ?? 0) + (2 * BoxMargin),
                Math.Max(1, host?.Height ?? 0) + (2 * BoxMargin));
        }
        else if (!Covers(_box, curveBounds))
        {
            // 曲线跑出盒子：长大到刚好罩住它 + 一圈余量。这只发生一次，之后盒子又稳定下来。
            var left = Math.Min(_box.Left, curveBounds.Left - BoxMargin);
            var top = Math.Min(_box.Top, curveBounds.Top - BoxMargin);
            var right = Math.Max(_box.Right, curveBounds.Right + BoxMargin);
            var bottom = Math.Max(_box.Bottom, curveBounds.Bottom + BoxMargin);
            _box = new Rect(left, top, right - left, bottom - top);
        }
        else
        {
            return _box;
        }

        AbsoluteLayout.SetLayoutBounds(this, _box);
        return _box;
    }

    private static bool Covers(Rect box, WorkflowBounds curve)
        => curve.Left >= box.Left && curve.Top >= box.Top && curve.Right <= box.Right && curve.Bottom <= box.Bottom;

    private void ApplyPaint()
    {
        if (BindingContext is not IWorkflowLinkViewModel link)
        {
            return;
        }

        _paintPending = false;
        _paintedVirtual = IsVirtual(link);

        var color = IsHighlighted ? HighlightColor : LineColor;
        var thickness = IsHighlighted ? HighlightThickness : RestingThickness;
        // 空集合 = 实线：Shape.StrokeDashArray 的类型不可空
        var dash = _paintedVirtual ? new DoubleCollection { 4, 2 } : new DoubleCollection();

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

    // 四个控制点（起点、两个控制点、终点）烘进视图的局部坐标。控制点来自 Core，本层不再自己推一遍。
    private static PathGeometry BuildGeometry((double X, double Y)[] points, double originX, double originY)
    {
        static Point Local((double X, double Y) point, double originX, double originY)
            => new(point.X - originX, point.Y - originY);

        var figure = new PathFigure
        {
            StartPoint = Local(points[0], originX, originY),
            IsClosed = false,
            IsFilled = false,
        };
        figure.Segments.Add(new BezierSegment
        {
            Point1 = Local(points[1], originX, originY),
            Point2 = Local(points[2], originX, originY),
            Point3 = Local(points[3], originX, originY),
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
