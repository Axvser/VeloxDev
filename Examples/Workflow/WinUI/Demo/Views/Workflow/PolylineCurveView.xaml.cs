using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using System;
using VeloxDev.TransitionSystem;
using VeloxDev.WorkflowSystem;
using Windows.Foundation;

namespace Demo.Views;

/// <summary>
/// Orthogonal (polyline) connection with golden-ratio stubs.
/// Hover to highlight, Delete to remove, and carries a travelling highlight so the direction of data flow is
/// readable at a glance.
/// </summary>
public sealed partial class PolylineCurveView : UserControl
{
    private static readonly DoubleCollection VirtualStrokeDashArray = [4, 2];

    private readonly Path _path;
    private readonly Path _arrowPath;
    private readonly SolidColorBrush _strokeBrush = new(Colors.Cyan);
    // 箭头画刷与线体分开：它取流光的亮色而非线体描边用的渐变（见 UpdatePath）
    // 由 AimFlowBrush 就地改色，种子色只是链接自身颜色已知前的临时值
    private readonly SolidColorBrush _arrowBrush = new(Colors.Cyan);
    private readonly PathGeometry _pathGeometry = new();
    private readonly PathFigure _pathFigure = new() { IsClosed = false };
    private readonly PathGeometry _arrowGeometry = new();
    private readonly PathFigure _arrowFigure = new() { IsClosed = true };
    private readonly LineSegment _arrowLeftSegment = new();
    private readonly LineSegment _arrowRightSegment = new();
    private readonly Point[] _points = new Point[4];
    private bool _updatePending;
    private bool _isLoaded;

    public PolylineCurveView()
    {
        InitializeComponent();
        Canvas.SetZIndex(this, -100);

        // The polyline/arrow geometry is in raw collapsed (canvas-local) coordinates, so at deep zoom
        // its negative top/left half extends beyond this element's bounds. WinUI clips element content
        // to its bounds unless Clip is nulled (the root/grid pattern the sibling NodeView uses); WPF
        // links are OnRender-drawn and never clipped. Null the whole chain so the retained Paths draw
        // their negative-coordinate geometry the same way WPF does.
        var container = new Grid { Clip = null };
        _path = new Path { Stroke = _strokeBrush, StrokeThickness = 2, StrokeLineJoin = PenLineJoin.Round, IsHitTestVisible = false, Clip = null };
        _arrowPath = new Path { Fill = _arrowBrush, IsHitTestVisible = false, Clip = null };
        container.Children.Add(_path);
        container.Children.Add(_arrowPath);
        this.Content = container;

        // 画刷归视图所有；这里只建它，指向、配色与链都由 AimFlowBrush 完成
        AimFlowBrush();

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        PointerEntered += (_, _) => { IsHighlighted = true; Focus(FocusState.Pointer); };
        PointerExited += (_, _) => IsHighlighted = false;
        PointerMoved += OnHoverPointerMoved;
        UpdateInteractivity();
    }

    #region Dependency properties

    public static readonly DependencyProperty StartLeftProperty =
        DependencyProperty.Register(nameof(StartLeft), typeof(double), typeof(PolylineCurveView), new PropertyMetadata(0d, OnChanged));
    public static readonly DependencyProperty StartTopProperty =
        DependencyProperty.Register(nameof(StartTop), typeof(double), typeof(PolylineCurveView), new PropertyMetadata(0d, OnChanged));
    public static readonly DependencyProperty EndLeftProperty =
        DependencyProperty.Register(nameof(EndLeft), typeof(double), typeof(PolylineCurveView), new PropertyMetadata(0d, OnChanged));
    public static readonly DependencyProperty EndTopProperty =
        DependencyProperty.Register(nameof(EndTop), typeof(double), typeof(PolylineCurveView), new PropertyMetadata(0d, OnChanged));
    public static readonly DependencyProperty CanRenderProperty =
        DependencyProperty.Register(nameof(CanRender), typeof(bool), typeof(PolylineCurveView), new PropertyMetadata(true, OnChanged));
    public static readonly DependencyProperty IsVirtualProperty =
        DependencyProperty.Register(nameof(IsVirtual), typeof(bool), typeof(PolylineCurveView), new PropertyMetadata(false, OnChanged));
    public static readonly DependencyProperty LineColorProperty =
        DependencyProperty.Register(nameof(LineColor), typeof(Windows.UI.Color), typeof(PolylineCurveView), new PropertyMetadata(Colors.Cyan, OnChanged));
    public static readonly DependencyProperty IsHighlightedProperty =
        DependencyProperty.Register(nameof(IsHighlighted), typeof(bool), typeof(PolylineCurveView), new PropertyMetadata(false, OnChanged));

    public double StartLeft { get => (double)GetValue(StartLeftProperty); set => SetValue(StartLeftProperty, value); }
    public double StartTop { get => (double)GetValue(StartTopProperty); set => SetValue(StartTopProperty, value); }
    public double EndLeft { get => (double)GetValue(EndLeftProperty); set => SetValue(EndLeftProperty, value); }
    public double EndTop { get => (double)GetValue(EndTopProperty); set => SetValue(EndTopProperty, value); }
    public bool CanRender { get => (bool)GetValue(CanRenderProperty); set => SetValue(CanRenderProperty, value); }
    public bool IsVirtual { get => (bool)GetValue(IsVirtualProperty); set => SetValue(IsVirtualProperty, value); }
    public Windows.UI.Color LineColor { get => (Windows.UI.Color)GetValue(LineColorProperty); set => SetValue(LineColorProperty, value); }
    public bool IsHighlighted { get => (bool)GetValue(IsHighlightedProperty); set => SetValue(IsHighlightedProperty, value); }

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (PolylineCurveView)d;
        control.UpdateInteractivity();
        control.ScheduleUpdate();

        // 渐变沿链接自身轴向、由链接自身颜色混合：端点与颜色都是画刷的输入，而非绘制代码的
        // 这里只能重新指向：光带位置归周期所有，在此重写停靠点会让光带在整段手势里停在发送端——是抖动不是流动
        if (e.Property == StartLeftProperty || e.Property == StartTopProperty
            || e.Property == EndLeftProperty || e.Property == EndTopProperty
            || e.Property == LineColorProperty)
        {
            control.AimFlowBrush();
        }

        // 两端测量完才可绘制，在那之前流光无物可循；虚拟链接是指针下的橡皮筋，没有稳定连接可描述
        if (e.Property == CanRenderProperty || e.Property == IsVirtualProperty)
        {
            if (control.IsVirtual || !control.CanRender)
            {
                control.StopFlow();
            }
            else
            {
                control.StartFlow();
            }
        }
    }

    private void UpdateInteractivity()
    {
        IsHitTestVisible = !IsVirtual;
        IsTabStop = !IsVirtual;
    }

    #endregion

    #region Flow effect

    // 光带半宽（渐变偏移单位）
    private const double BandHalfWidth = 0.04;

    // 三段相位各自结束时光带中心的位置：成形、全亮行进、退去
    private const double BandStart = 0.06;
    private const double BandFormed = 0.34;
    private const double BandLeaving = 0.66;
    private const double BandExit = 0.94;

    private static readonly TimeSpan EnterDuration = TimeSpan.FromMilliseconds(550);
    private static readonly TimeSpan TravelDuration = TimeSpan.FromMilliseconds(650);
    private static readonly TimeSpan ExitDuration = TimeSpan.FromMilliseconds(550);

    /// <summary>
    /// The brush the link is drawn with, and the object the flow animates: a gradient along the link's own
    /// axis whose middle stop is the band. It is a property of this control rather than something a model
    /// holds, so the animated paths read straight off the view — <c>FlowBrush.GradientStops[1].Offset</c> and
    /// <c>[1].Color</c> — and there is no value in between to map back into geometry.
    /// </summary>
    public LinearGradientBrush FlowBrush { get; } = new()
    {
        SpreadMethod = GradientSpreadMethod.Pad,
    };

    // 光带与箭头颜色：链接本色提到全不透明
    private Windows.UI.Color Lit { get; set; }

    // 线体静息色：亮色按 alpha 变暗到约 62%
    private Windows.UI.Color Dim { get; set; }

    private Transition<PolylineCurveView>? _flow;
    private bool _running;

    // 每视图构建：两个端点取自该链接自己的颜色，静态声明会把读到的那份值共享给之后每次执行
    // WinUI 另有一因：Transition<T> 字段建在首次触碰该类型的线程上（本仓库 WinUI demo 记过的坑），此处调用者都已在 UI 线程
    // 路径直达画刷：GradientStops[1] 是光带、两侧是肩；匀速所以不用缓动
    // 不挂重绘：被替换的设计就是把同样的停靠点原地写进保留模式的 Path，实测能刷新；WPF 要重绘是它 DrawingContext 的事
    private Transition<PolylineCurveView> BuildFlow() => Transition<PolylineCurveView>.Create()
        // 相位一：一边成形一边进入（走三分之一路程，同时由静息色变亮）
        .Property(v => v.FlowBrush.GradientStops[0].Offset, BandFormed - BandHalfWidth)
        .Property(v => v.FlowBrush.GradientStops[1].Offset, BandFormed)
        .Property(v => v.FlowBrush.GradientStops[2].Offset, BandFormed + BandHalfWidth)
        .Property(v => v.FlowBrush.GradientStops[1].Color, Lit)
        .Effect(new TransitionEffect()
        {
            Duration = EnterDuration,
            Ease = Eases.Default,
        })
        .Then()
        // 相位二：保持全亮只移动——这一段读起来才是流动而非脉冲
        .Property(v => v.FlowBrush.GradientStops[0].Offset, BandLeaving - BandHalfWidth)
        .Property(v => v.FlowBrush.GradientStops[1].Offset, BandLeaving)
        .Property(v => v.FlowBrush.GradientStops[2].Offset, BandLeaving + BandHalfWidth)
        .Effect(new TransitionEffect()
        {
            Duration = TravelDuration,
            Ease = Eases.Default,
        })
        .Then()
        // 相位三：一边退回静息色一边离开；周期两端都是均匀暗色，循环接缝才看不出来
        .Property(v => v.FlowBrush.GradientStops[0].Offset, BandExit - BandHalfWidth)
        .Property(v => v.FlowBrush.GradientStops[1].Offset, BandExit)
        .Property(v => v.FlowBrush.GradientStops[2].Offset, BandExit + BandHalfWidth)
        .Property(v => v.FlowBrush.GradientStops[1].Color, Dim)
        .Effect(new TransitionEffect()
        {
            Duration = ExitDuration,
            Ease = Eases.Default,
        })
        .Repeat(int.MaxValue);

    // 链接移动（缩放与拖拽每帧都改锚点）或变色时调用，变色要重建链：两个端点就是它的颜色
    // 这里不写光带位置：那些停靠点归周期所有，手势期间抢写会让光带抖动
    // WinUI 的 LinearGradientBrush 无 MappingMode，轴按所绘几何自身的 0..1 空间算，端点须换算进几何包围盒
    private void AimFlowBrush()
    {
        // 读控件自己的端点而不是读几何：这里跑在移动端点的属性变更上，早于被推迟的 UpdatePath 重建
        BuildPoints();

        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;
        foreach (var point in _points)
        {
            if (point.X < minX) minX = point.X;
            if (point.X > maxX) maxX = point.X;
            if (point.Y < minY) minY = point.Y;
            if (point.Y > maxY) maxY = point.Y;
        }

        // 轴对齐的链接会有一维为零，除零给端点 NaN；该维除数取 1 使两端相等，即轴对齐链接要的平渐变，光带沿有尺寸的那一维走
        var width = maxX > minX ? maxX - minX : 1d;
        var height = maxY > minY ? maxY - minY : 1d;

        FlowBrush.StartPoint = new Point((StartLeft - minX) / width, (StartTop - minY) / height);
        FlowBrush.EndPoint = new Point((EndLeft - minX) / width, (EndTop - minY) / height);

        var lit = LitOf(LineColor);
        if (_flow is not null && lit == Lit)
        {
            return;
        }

        Lit = lit;
        Dim = DimOf(lit);
        _flow = BuildFlow();

        var stops = FlowBrush.GradientStops;
        if (stops.Count == 0)
        {
            stops.Add(new GradientStop { Color = Dim, Offset = BandStart - BandHalfWidth });
            stops.Add(new GradientStop { Color = Dim, Offset = BandStart });
            stops.Add(new GradientStop { Color = Dim, Offset = BandStart + BandHalfWidth });
        }
        else
        {
            stops[0].Color = Dim;
            stops[2].Color = Dim;
        }

        // 箭头是终点标记，用光带亮色而非渐变，所以随声明一起改色而不是留在绘制路径上：它是链接上唯一不能一直暗着的部分
        _arrowBrush.Color = Lit;

        // 视图被复用到另一种颜色的链接上时，按自己的颜色重新起周期
        if (_running)
        {
            StartFlow();
        }
    }

    // 亮色：各通道向白抬 45%（白链接也留出更亮可去处）
    private static Windows.UI.Color LitOf(Windows.UI.Color color)
    {
        const double lift = 0.45;

        byte Up(byte channel) => (byte)Math.Round(channel + (255 - channel) * lift);

        return Windows.UI.Color.FromArgb(255, Up(color.R), Up(color.G), Up(color.B));
    }

    // 靠 alpha 变暗取反差，色相不变；往白里提在本 demo 的白链接（白提白）上完全看不出，青线上也几乎看不出
    private static Windows.UI.Color DimOf(Windows.UI.Color color) => Windows.UI.Color.FromArgb(
        (byte)Math.Round(color.A * 0.62), color.R, color.G, color.B);

    // 从发送端起动周期；挂载时起动，池化视图换到新链接后动的是新链接而不是它当初构建的那条
    private void StartFlow()
    {
        if (IsVirtual || !CanRender)
        {
            StopFlow();
            return;
        }

        // 属性变更可能在池化视图还没上树时到来（比如正为下一条链接做准备），那时起动会去动画一条没人看的链接，
        // 也没有卸载来停它，所以略过；Loaded 真上屏时再起动
        if (!_isLoaded) return;

        AimFlowBrush();

        // 声明从目标读起始值，Execute 前画刷要先落到周期起点；循环在每个接缝重放捕获的起点，故这也是之后每周期的起始状态
        var stops = FlowBrush.GradientStops;
        stops[0].Offset = BandStart - BandHalfWidth;
        stops[1].Offset = BandStart;
        stops[2].Offset = BandStart + BandHalfWidth;
        stops[1].Color = Dim;

        _flow!.Execute(this);
        _running = true;
    }

    // 停周期：视图被释放复用时不能留着旧动画在跑，去驱动复用后那条链接的画刷
    private void StopFlow()
    {
        if (!_running)
        {
            return;
        }

        Transition.Exit(this, IncludeMutual: true, IncludeNoMutual: true);
        _running = false;
    }

    #endregion

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _isLoaded = true;
        EnsureGeometry();
        ScheduleUpdate();

        // 挂载时起动：视图池会把释放的视图交给另一条链接，光带要对这次挂载的链接跑，不是当初构建它的那条
        StartFlow();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _isLoaded = false;
        _updatePending = false;

        // 卸载时停止，同理：释放复用的视图不能留着旧动画在跑
        StopFlow();
    }

    private void EnsureGeometry()
    {
        while (_pathFigure.Segments.Count < _points.Length - 1)
        {
            _pathFigure.Segments.Add(new LineSegment());
        }

        if (_pathGeometry.Figures.Count == 0)
        {
            _pathGeometry.Figures.Add(_pathFigure);
        }

        if (_arrowFigure.Segments.Count == 0)
        {
            _arrowFigure.Segments.Add(_arrowLeftSegment);
            _arrowFigure.Segments.Add(_arrowRightSegment);
            _arrowGeometry.Figures.Add(_arrowFigure);
        }
    }

    private void ScheduleUpdate()
    {
        if (!_isLoaded)
        {
            return;
        }

        if (_updatePending)
        {
            return;
        }

        _updatePending = true;
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, () =>
        {
            _updatePending = false;
            if (_isLoaded)
            {
                UpdatePath();
            }
        });
    }

    private void UpdatePath()
    {
        EnsureGeometry();

        if (!CanRender)
        {
            _path.Data = null;
            _arrowPath.Data = null;
            return;
        }

        BuildPoints();
        var color = IsHighlighted ? Microsoft.UI.Colors.OrangeRed : LineColor;
        var thickness = IsHighlighted ? 3.5 : 2.0;
        _strokeBrush.Color = color;
        _path.StrokeThickness = thickness;

        // 流动高亮只对稳定连接有意义：虚拟链接是指针下的橡皮筋，高亮的已经点亮，两者都用平色
        // 稳定链接用渐变描边，两个外侧停靠点是静息色，所以线静息时就是链接色变暗，亮色光带才有对比可读
        _path.Stroke = IsHighlighted || IsVirtual ? _strokeBrush : FlowBrush;

        // 箭头是终点标记，用光带亮色而非渐变：线静息是暗的，箭头跟着暗就成了链接上永不点亮的那一处
        _arrowPath.Fill = IsHighlighted ? _strokeBrush : _arrowBrush;

        if (IsVirtual)
            _path.StrokeDashArray = VirtualStrokeDashArray;
        else
            _path.StrokeDashArray = null;

        _pathFigure.StartPoint = _points[0];
        for (int i = 1; i < _points.Length; i++)
        {
            ((LineSegment)_pathFigure.Segments[i - 1]).Point = _points[i];
        }

        _path.Data = _pathGeometry;

        // Arrowhead
        if (!IsVirtual)
        {
            var from = _points[^2];
            var tip = _points[^1];
            double tx = tip.X - from.X, ty = tip.Y - from.Y;
            double len = Math.Sqrt(tx * tx + ty * ty);
            if (len <= 0.001)
            {
                _arrowPath.Data = null;
                return;
            }

            tx /= len;
            ty /= len;
            double nx = -ty, ny = tx;
            double al = 12, aw = 8;
            var baseP = new Point(tip.X - tx * al, tip.Y - ty * al);
            _arrowFigure.StartPoint = tip;
            _arrowLeftSegment.Point = new Point(baseP.X + nx * (aw / 2), baseP.Y + ny * (aw / 2));
            _arrowRightSegment.Point = new Point(baseP.X - nx * (aw / 2), baseP.Y - ny * (aw / 2));
            _arrowPath.Data = _arrowGeometry;
        }
        else
        {
            _arrowPath.Data = null;
        }
    }

    private void BuildPoints()
    {
        double dx = EndLeft - StartLeft;
        const double phi = 0.6180339887;
        double stub = dx / 2.0 * (1.0 - phi);
        _points[0] = new Point(StartLeft, StartTop);
        _points[1] = new Point(StartLeft + stub, StartTop);
        _points[2] = new Point(EndLeft - stub, EndTop);
        _points[3] = new Point(EndLeft, EndTop);
    }

    #region Interaction

    private void OnHoverPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        var pt = e.GetCurrentPoint(this).Position;
        bool over = HitTestLine(pt);
        if (over && !IsHighlighted) { IsHighlighted = true; Focus(FocusState.Pointer); }
        else if (!over && IsHighlighted) IsHighlighted = false;
    }

    protected override void OnKeyDown(KeyRoutedEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Windows.System.VirtualKey.Delete && IsHighlighted)
        {
            if (DataContext is IWorkflowLinkViewModel vm)
                vm.DeleteCommand.Execute(null);
            e.Handled = true;
        }
    }

    private bool HitTestLine(Point pt)
    {
        const double hitRadius = 6.0;
        BuildPoints();
        for (int i = 0; i < _points.Length - 1; i++)
            if (DistSeg(pt, _points[i], _points[i + 1]) <= hitRadius) return true;
        return false;
    }

    private static double DistSeg(Point p, Point a, Point b)
    {
        double abx = b.X - a.X, aby = b.Y - a.Y;
        double len2 = abx * abx + aby * aby;
        if (len2 < 0.0001) return Math.Sqrt((p.X - a.X) * (p.X - a.X) + (p.Y - a.Y) * (p.Y - a.Y));
        double t = Math.Clamp(((p.X - a.X) * abx + (p.Y - a.Y) * aby) / len2, 0, 1);
        double px = a.X + t * abx - p.X, py = a.Y + t * aby - p.Y;
        return Math.Sqrt(px * px + py * py);
    }

    #endregion
}
