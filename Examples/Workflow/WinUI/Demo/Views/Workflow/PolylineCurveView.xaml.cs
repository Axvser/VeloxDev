using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using System;
using VeloxDev.TransitionSystem;
using VeloxDev.WorkflowSystem;
using Windows.Foundation;

namespace Demo.Views;

/// <summary>
/// One connection between two ports, drawn as a single cubic curve that leaves each end horizontally.
/// <para>
/// The light travelling along it is a comet — a bright head, a tail that fades behind it, and a halo that
/// follows the head — and it is cut out of the curve <b>by arc length</b> rather than by a gradient brush.
/// That is the whole reason this view keeps its own sample table: a <see cref="LinearGradientBrush"/>'s axis
/// is the straight line between the two ends, so on a curve it lights the string rather than the rope. The
/// brightness stops tracking the bend, the light appears to speed up and slow down as it goes round, and the
/// kink where an elbow met its diagonal reads as a kink in the light itself.
/// </para>
/// <para>
/// <b>What "no OnRender" costs, and how it is paid here.</b> The Avalonia original draws each arc-length
/// slice with <c>DrawingContext.DrawGeometry</c> in one pass. WinUI is retained mode: there is no per-frame
/// draw hook, and each stroked slice needs its own brush, so a slice has to <i>be</i> an element. This view
/// therefore owns a fixed pool of <see cref="Path"/> elements — one per tail slice, one per halo slice, plus
/// three for the resting line — and rewrites only their three points, their colour and their thickness each
/// frame. Nothing is created or destroyed while the animation runs, which is what keeps a per-frame path
/// rewrite affordable. The alternative, a Win2D <c>CanvasControl</c>, would draw this in a single pass and
/// match Avalonia's output more cheaply, but Win2D is not a dependency of this demo (see Demo.csproj) and
/// adding a graphics stack to port a decoration is not a trade this repo makes.
/// </para>
/// <para>
/// It draws nothing at all until both endpoints carry measured coordinates: an unmeasured slot's anchor is
/// <see cref="double.NaN"/>, and a retained <see cref="Path"/> paints whatever its geometry holds, so a NaN point
/// is a line that never appears rather than a line drawn in the wrong place.
/// </para>
/// <para>
/// The type keeps the name <c>PolylineCurveView</c> because the surface template binds it by that name; it has
/// not drawn a polyline since the geometry was replaced.
/// </para>
/// <para>
/// The hit surface is the widest drawn stroke of the resting line — nothing else. The view is canvas sized
/// so every link in view overlaps, but only the halo layer is <see cref="UIElement.IsHitTestVisible"/>, which
/// is what lets "the drawn part responds to the pointer" and "an empty canvas still pans" hold at once.
/// Give the view a <c>Background</c> instead and the whole canvas rectangle would swallow the pan.
/// </para>
/// <para>
/// Supports hover highlight, <c>Delete</c> and a right-click menu to remove.
/// </para>
/// </summary>
public sealed partial class PolylineCurveView : UserControl
{
    // 弧长表的分辨率。128 段在缩放上限下也看不出折线感，而每帧重建它只是几百次算术。
    private const int SampleCount = 128;

    // 拖尾占全长的比例。这是彗星唯一的观感旋钮：调大＝更长的尾、更像流光；调小＝更像一个亮点在跑。
    private const double TailFraction = 0.30;

    // 拖尾分几段画。每段一个不透明度，衰减因此是连续的而不需要渐变刷。
    private const int TailSegments = 16;

    // 光晕分几段画。它比本体少一半：光晕最亮处也只有 0.22，八级台阶在这里看不出来，
    // 而保留模式下每一段都是一个真元素 —— 这一档是平台成本换来的，不是观感上的让步。
    private const int BloomSegments = 8;

    // 光晕的两档宽度与不透明度（相对本体厚度）
    private const double BloomWidth = 9;
    private const double BloomAlpha = 0.22;

    // 三段相位各自结束时头部走过的比例：出发、行进、到达
    private const double BandFormed = 0.30;
    private const double BandLeaving = 0.78;

    private static readonly TimeSpan EnterDuration = TimeSpan.FromMilliseconds(450);
    private static readonly TimeSpan TravelDuration = TimeSpan.FromMilliseconds(700);
    private static readonly TimeSpan ExitDuration = TimeSpan.FromMilliseconds(450);

    private static readonly DoubleCollection VirtualStrokeDashArray = [4, 2];

    // 一条「按弧长切出来的描边」在保留模式里的全部家当：一个 Path、一份可复用的几何、一支可改色的画刷。
    // 三者都只建一次，之后每帧只写 3 个点、1 个颜色、1 个厚度。
    private sealed class StrokeStrip
    {
        public required Path Path { get; init; }
        public required PathFigure Figure { get; init; }
        public required LineSegment Mid { get; init; }
        public required LineSegment End { get; init; }
        public required SolidColorBrush Brush { get; init; }
    }

    /// <summary>
    /// One layer of the resting line: a <see cref="Path"/> and the cubic it draws.
    /// </summary>
    /// <remarks>
    /// There are three of these — halo, glow, body — and each owns its <b>own</b> geometry even though all three
    /// draw the very same curve. Sharing one geometry between them does not work in WinUI: assigning a
    /// <see cref="Geometry"/> to a <see cref="Path"/> parents it, and handing the same instance to a second path
    /// throws <c>E_INVALIDARG</c> ("Value does not fall within the expected range") — observed at runtime, not at
    /// build time. Avalonia has no such rule: there the three layers are three <c>DrawGeometry</c> calls against one
    /// <c>StreamGeometry</c>, because a drawing context is a picture rather than a retained tree it takes ownership
    /// of. Hence the same "one curve, three strokes" design costs three coupled geometries here (see
    /// <see cref="RefreshGeometry"/>, which writes all three).
    /// </remarks>
    private sealed class RestingStroke
    {
        public required Path Path { get; init; }
        public required PathGeometry Geometry { get; init; }
        public required PathFigure Figure { get; init; }
        public required BezierSegment Segment { get; init; }
        public required SolidColorBrush Brush { get; init; }
    }

    private readonly Grid _container;
    private readonly RestingStroke _halo;
    private readonly RestingStroke _glow;
    private readonly RestingStroke _line;

    // 三层一起改的那一份：端点一变，三条曲线要同步写一次
    private readonly RestingStroke[] _resting;

    private readonly StrokeStrip[] _tail = new StrokeStrip[TailSegments];
    private readonly StrokeStrip[] _bloom = new StrokeStrip[BloomSegments];

    // 弧长表：_cumulative[i] 是 _samples[0..i] 的累计长度，_length 是全长。
    // 三者只在端点变化时重建 —— 每帧渲染要按弧长取点，现算不划算。
    // 段数固定，所以两个数组只建一次、就地覆写：拖节点时端点每帧都在变，让每次重算都 new 两个 129 长的数组
    // 是白付的 GC。
    private readonly Point[] _samples = new Point[SampleCount + 1];
    private readonly double[] _cumulative = new double[SampleCount + 1];
    private double _length;

    private Transition<PolylineCurveView>? _flow;
    private bool _running;
    private bool _isLoaded;

    public PolylineCurveView()
    {
        InitializeComponent();
        Canvas.SetZIndex(this, -100);

        // 曲线的几何是画布局部坐标，深缩放时负的 top/left 会落到这个元素自己的框外。WinUI 默认把元素内容
        // 裁到框内，除非整条链上的 Clip 都是 null（同目录 NodeView 用的也是这个办法）；Avalonia 那边是
        // 自绘的，从来没有裁剪这回事。
        _container = new Grid { Clip = null };

        // 只有最外那层可命中：这个视图是整块画布大小，但可命中的只有它画出来的描边，
        // 所以「画出来的部分响应鼠标」与「画布空白处照常平移」可以同时成立。
        // 不要改成给视图加 Background —— 那是整块画布的矩形，会把画布平移整个吃掉（SlotView 那种小控件才那么写）。
        _halo = CreateRestingStroke(_container, LineThickness + 9, true);
        _glow = CreateRestingStroke(_container, LineThickness + 4, false);
        _line = CreateRestingStroke(_container, LineThickness, false);
        _resting = [_halo, _glow, _line];

        for (var i = 0; i < TailSegments; i++)
        {
            // 初始厚度只是个占位：每一帧都会按段重新给，头那一段比尾梢粗一倍
            _tail[i] = CreateStrip(_container, LineThickness);
        }

        for (var i = 0; i < BloomSegments; i++)
        {
            _bloom[i] = CreateStrip(_container, BloomWidth);
        }

        Content = _container;

        // 先按依赖属性的默认值画一遍：端点要等绑定推下来，那时 OnChanged 会再跑
        Refresh();

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        // 池化视图会被改绑给另一条链接，而回收/复用只置 Visibility 与 DataContext —— Loaded/Unloaded 都不触发，
        // 所以「换了链接」这件事只能从这里知道
        DataContextChanged += OnDataContextChanged;
        PointerEntered += (_, _) => { IsHighlighted = true; Focus(FocusState.Pointer); };
        PointerExited += (_, _) => IsHighlighted = false;
        PointerMoved += OnHoverPointerMoved;
        // 右键菜单。WinUI 没有右键「按下」这一档，RightTapped 是抬起时给的
        RightTapped += OnRightTapped;
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
        DependencyProperty.Register(nameof(LineColor), typeof(Windows.UI.Color), typeof(PolylineCurveView), new PropertyMetadata(Windows.UI.Color.FromArgb(0xCC, 0x38, 0xBD, 0xF8), OnChanged));
    public static readonly DependencyProperty LineThicknessProperty =
        DependencyProperty.Register(nameof(LineThickness), typeof(double), typeof(PolylineCurveView), new PropertyMetadata(2.0, OnChanged));
    public static readonly DependencyProperty IsHighlightedProperty =
        DependencyProperty.Register(nameof(IsHighlighted), typeof(bool), typeof(PolylineCurveView), new PropertyMetadata(false, OnChanged));

    public double StartLeft { get => (double)GetValue(StartLeftProperty); set => SetValue(StartLeftProperty, value); }
    public double StartTop { get => (double)GetValue(StartTopProperty); set => SetValue(StartTopProperty, value); }
    public double EndLeft { get => (double)GetValue(EndLeftProperty); set => SetValue(EndLeftProperty, value); }
    public double EndTop { get => (double)GetValue(EndTopProperty); set => SetValue(EndTopProperty, value); }
    public bool CanRender { get => (bool)GetValue(CanRenderProperty); set => SetValue(CanRenderProperty, value); }
    public bool IsVirtual { get => (bool)GetValue(IsVirtualProperty); set => SetValue(IsVirtualProperty, value); }
    public Windows.UI.Color LineColor { get => (Windows.UI.Color)GetValue(LineColorProperty); set => SetValue(LineColorProperty, value); }
    public double LineThickness { get => (double)GetValue(LineThicknessProperty); set => SetValue(LineThicknessProperty, value); }
    public bool IsHighlighted { get => (bool)GetValue(IsHighlightedProperty); set => SetValue(IsHighlightedProperty, value); }

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (PolylineCurveView)d;
        control.UpdateInteractivity();

        if (e.Property == StartLeftProperty || e.Property == StartTopProperty
            || e.Property == EndLeftProperty || e.Property == EndTopProperty)
        {
            // 端点一动就把整幅重算一遍 —— 同步做，Avalonia 那边也是同步的：
            // 推迟一帧会让彗星追不上正在被拖的端点。重算也包含显隐：两端从「没量出来」变成真实坐标时，
            // 正是这一步让线当场出现，而不是等下一个属性碰巧变化
            control.Refresh();
            return;
        }

        if (e.Property == LineColorProperty || e.Property == LineThicknessProperty || e.Property == IsHighlightedProperty)
        {
            control.UpdateRestingLine();
            control.RedrawComet();
            return;
        }

        // 两端测量完才可绘制，在那之前流光无物可循；虚拟链接是指针下的橡皮筋，没有稳定连接可描述
        if (e.Property == CanRenderProperty || e.Property == IsVirtualProperty)
        {
            control.Refresh();
        }
    }

    private void UpdateInteractivity()
    {
        IsHitTestVisible = !IsVirtual;
        IsTabStop = !IsVirtual;
    }

    #endregion

    #region Flow effect

    /// <summary>How far along the link the comet's head has travelled, as a fraction of its length.</summary>
    /// <remarks>
    /// Animated rather than computed, and the armature the whole comet hangs off: the value carries no geometry of
    /// its own — the arc-length table turns it into points — which is what lets the light follow a curve. A plain
    /// property rather than a dependency property, deliberately: the transition system writes animatable members
    /// through compiled reflection paths, and the write lands straight in the redraw instead of going through
    /// <c>SetValue</c> first.
    /// </remarks>
    public double BandHead
    {
        get => _bandHead;
        set
        {
            _bandHead = value;
            RedrawComet();
        }
    }

    /// <summary>How lit the comet is: 0 while it is absent, 1 while it travels.</summary>
    public double BandIntensity
    {
        get => _bandIntensity;
        set
        {
            _bandIntensity = value;
            RedrawComet();
        }
    }

    private double _bandHead;
    private double _bandIntensity;

    // 每视图构建。匀速（Eases.Default 就是恒等）—— 流水不该有缓动，头部的速度一变化就不像在流了。
    // 同一控件上只跑这一条链：Transition.Exit 按目标停，两条会互相打断，所以两个标量合在一条链里。
    private Transition<PolylineCurveView> BuildFlow() => Transition<PolylineCurveView>.Create()
        // 相位一：从发送端出发，一边走一边亮起
        .Property(v => v.BandHead, BandFormed)
        .Property(v => v.BandIntensity, 1d)
        .Effect(new TransitionEffect()
        {
            Duration = EnterDuration,
            Ease = Eases.Default,
        })
        .Then()
        // 相位二：全亮行进——这一段读起来才是流动而非脉冲
        .Property(v => v.BandHead, BandLeaving)
        .Effect(new TransitionEffect()
        {
            Duration = TravelDuration,
            Ease = Eases.Default,
        })
        .Then()
        // 相位三：到达并熄灭。两端都是「没有光」的状态，循环接缝才看不出来
        .Property(v => v.BandHead, 1d)
        .Property(v => v.BandIntensity, 0d)
        .Effect(new TransitionEffect()
        {
            Duration = ExitDuration,
            Ease = Eases.Default,
        })
        .Repeat(int.MaxValue);

    // 从发送端起动周期；视图复用后会换链接，所以在挂载与改绑时都要重起一次
    private void StartFlow()
    {
        // 属性变更可能在池化视图还没上树时到来（正为下一条链接做准备），那时起动会去动画一条没人看的链接，
        // 也没有卸载来停它，所以略过；Loaded 真上屏时再起动
        if (!_isLoaded || IsVirtual || !CanRender)
        {
            return;
        }

        // 已经在跑的就是当前这条链接的周期。端点每帧都在变，重起会把彗星按回起点 —— 那是抖，不是流动
        if (_running)
        {
            return;
        }

        _flow ??= BuildFlow();

        // 声明从目标读起值，所以执行前必须把两个标量摆到周期起点（直接写字段，不经过 setter，
        // 免得在 Execute 之前先画一帧假的）；循环在每个接缝重放这一份起始状态
        _bandHead = 0;
        _bandIntensity = 0;

        _flow.Execute(this);
        _running = true;
    }

    // 停周期：视图被释放复用时不能留着旧动画在跑
    private void StopFlow()
    {
        if (!_running)
        {
            return;
        }

        Transition.Exit(this, IncludeMutual: true, IncludeNoMutual: true);
        _running = false;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _isLoaded = true;
        Refresh();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _isLoaded = false;
        StopFlow();
    }

    // 改绑（池化视图换链接）与回收（DataContext 先被置空）都只在这里露出：两者都不触发 Loaded/Unloaded。
    // 绑定会把新的端点推下来，推不动的那几种情况（值恰好相同）Refresh 也照样按当前值重画一遍，
    // 所以「几何/显隐/周期」三件事在改绑后必定与当前这条链接一致
    private void OnDataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
    {
        if (args.NewValue is IWorkflowLinkViewModel)
        {
            Refresh();
            return;
        }

        // 被回收了：没有链接可描述。Unloaded 对池化视图永远不来，周期只能在这里停，
        // 否则它会一直往一个没人看的视图里写 27 条 Path，直到这个视图被复用
        Hide();
    }

    #endregion

    #region Geometry

    // 弧长表。端点变化时重建，绘制时只读。
    private void RefreshGeometry()
    {
        // 控制点与 t 无关：129 个采样点共用同一对，不然每个点都要重算一次 Controls
        var (c1, c2) = Controls();
        for (var i = 0; i <= SampleCount; i++)
        {
            _samples[i] = BezierAt(i / (double)SampleCount, c1, c2);
        }

        _cumulative[0] = 0;
        for (var i = 1; i <= SampleCount; i++)
        {
            // Windows.Foundation.Point 没有 operator-（Avalonia 的有），所以差在这里手写
            var dx = _samples[i].X - _samples[i - 1].X;
            var dy = _samples[i].Y - _samples[i - 1].Y;
            _cumulative[i] = _cumulative[i - 1] + Math.Sqrt((dx * dx) + (dy * dy));
        }

        _length = _cumulative[SampleCount];

        // 静息线就是那条三次贝塞尔本身，直接用 BezierSegment 画：一个段就是精确曲线，
        // 不必拿 128 个采样点去拼（那是给「按弧长切」用的表）。
        // 三层各写一份 —— 几何不能共用，理由见 RestingStroke
        var start = new Point(StartLeft, StartTop);
        var end = new Point(EndLeft, EndTop);

        foreach (var stroke in _resting)
        {
            stroke.Figure.StartPoint = start;
            stroke.Segment.Point1 = c1;
            stroke.Segment.Point2 = c2;
            stroke.Segment.Point3 = end;
        }
    }

    // 两个控制点各自水平拉开：连线因此从两端水平出线、中间平滑过渡，没有折角
    private (Point C1, Point C2) Controls()
    {
        var dx = EndLeft - StartLeft;

        // 最小拉出量：两个端口靠得很近时，0.5·dx 会让曲线退化成一条直线段，失去「从端口水平出来」的形状。
        // 40 是设计单位，与卡片同一坐标系
        var pull = Math.Max(40, Math.Abs(dx) * 0.5);

        return (new Point(StartLeft + pull, StartTop), new Point(EndLeft - pull, EndTop));
    }

    private Point BezierAt(double t, Point c1, Point c2)
    {
        var u = 1 - t;
        double a = u * u * u, b = 3 * u * u * t, c = 3 * u * t * t, d = t * t * t;

        return new Point(
            (a * StartLeft) + (b * c1.X) + (c * c2.X) + (d * EndLeft),
            (a * StartTop) + (b * c1.Y) + (c * c2.Y) + (d * EndTop));
    }

    // 弧长 → 点。二分找所在采样段再线性插值，所以取点是精确到亚像素的，不受采样密度限制
    private Point PointAtLength(double len)
    {
        if (_length <= 0)
        {
            return new Point(StartLeft, StartTop);
        }

        len = Math.Clamp(len, 0, _length);

        int lo = 0, hi = _cumulative.Length - 1;
        while (hi - lo > 1)
        {
            var mid = (lo + hi) / 2;
            if (_cumulative[mid] <= len)
            {
                lo = mid;
            }
            else
            {
                hi = mid;
            }
        }

        var span = _cumulative[hi] - _cumulative[lo];
        var t = span <= 0 ? 0 : (len - _cumulative[lo]) / span;

        return new Point(
            _samples[lo].X + ((_samples[hi].X - _samples[lo].X) * t),
            _samples[lo].Y + ((_samples[hi].Y - _samples[lo].Y) * t));
    }

    #endregion

    #region Render

    // 有没有线可画：宿主没把它藏起来，且两端都是量出来的坐标。
    // 未测量的 slot 锚点是 NaN，而 ViewManager 是把一个链接和它的两端分在相邻批次里物化、槽位测量又是
    // 异步落地的，所以视图完全可能先绑上、端点后测量 —— 这条判定就是挡在绘制外面的那道门。
    // 必须显式判 NaN 而不能指望绘制路径里的比较：NaN 参与的比较全是 false，`_length <= 0` 这类守卫根本拦不住它，
    // 而保留模式里写进几何的就是画出来的东西，一旦写入 NaN 点，这条线就什么都不会画。
    private bool RenderReady => CanRender
        && !double.IsNaN(StartLeft) && !double.IsNaN(StartTop)
        && !double.IsNaN(EndLeft) && !double.IsNaN(EndTop);

    // 按当前的依赖属性值把「这一帧要画的东西」整体重算一遍：弧长表、三层静息线、显隐、彗星，以及周期。
    // 保留模式没有下一帧自动重算，所以每个入口（属性变更 / 首次挂载 / 改绑）都必须走这里：
    // 漏掉一个，视图就会停在旧链接的几何上、或者该出现时不出现，直到别的属性碰巧变一次。
    private void Refresh()
    {
        if (!RenderReady)
        {
            Hide();
            return;
        }

        RefreshGeometry();
        UpdateRestingLine();
        UpdateVisibility(true);
        RedrawComet();
        StartFlow();
    }

    // 无可绘制：收起静息线与彗星，并停掉周期 —— 停在这一步是有代价的，不停的话周期会一直写下去
    private void Hide()
    {
        UpdateVisibility(false);
        StopFlow();
    }

    // 三层静息线（halo/glow/body）的显隐。收起时彗星一并收起：它依附的那条线都不在
    private void UpdateVisibility(bool visible)
    {
        var state = visible ? Visibility.Visible : Visibility.Collapsed;
        foreach (var stroke in _resting)
        {
            stroke.Path.Visibility = state;
        }

        if (!visible)
        {
            HideComet();
        }
    }

    // 静息线：两层更宽的同色低透明描边垫在下面，整条线因此像在发光而不是贴在背景上。
    // 圆头圆角，两端才不像被截断的横截面
    private void UpdateRestingLine()
    {
        var color = IsHighlighted ? Colors.OrangeRed : LineColor;
        var thickness = CurrentThickness;

        _halo.Brush.Color = WithAlpha(color, 0.10);
        _halo.Path.StrokeThickness = thickness + 9;
        _glow.Brush.Color = WithAlpha(color, 0.16);
        _glow.Path.StrokeThickness = thickness + 4;

        // 线体本身是静息的：光不在时它只是一根暗线，有了对比彗星才亮得出来
        _line.Brush.Color = WithAlpha(color, IsHighlighted ? 0.85 : 0.55);
        _line.Path.StrokeThickness = thickness;
        _line.Path.StrokeDashArray = IsVirtual ? VirtualStrokeDashArray : null;
    }

    private double CurrentThickness => IsHighlighted ? LineThickness + 1.5 : LineThickness;

    // 彗星：沿弧长切出 [头-尾, 头] 这一段，分若干小段画，每段给一个递减的不透明度与变化的颜色。
    // 不用渐变刷是因为它的轴是两端之间的直线，在曲线上会把光打偏（见类注释）。
    private void RedrawComet()
    {
        if (!RenderReady || _length <= 0 || IsVirtual || _bandIntensity <= 0.001)
        {
            HideComet();
            return;
        }

        var color = IsHighlighted ? Colors.OrangeRed : LineColor;
        var thickness = CurrentThickness;

        var head = Math.Clamp(_bandHead, 0, 1) * _length;
        var tail = TailFraction * _length;

        // 本体：尾梢是本体的颜色，越靠近头越白 —— 白热只发生在头部
        for (var k = 0; k < TailSegments; k++)
        {
            var f0 = k / (double)TailSegments;      // 0 = 尾梢，1 = 头
            var f1 = (k + 1) / (double)TailSegments;

            var l0 = head - (tail * (1 - f0));
            var l1 = head - (tail * (1 - f1));

            var strip = _tail[k];

            // 平方衰减：让透明集中在尾段，读起来才像拖尾而不是一条均匀的带
            var a = _bandIntensity * f0 * f0;
            if (l1 <= 0 || l0 >= _length || a <= 0.004)
            {
                strip.Path.Visibility = Visibility.Collapsed;
                continue;
            }

            strip.Path.Visibility = Visibility.Visible;
            SetStrip(strip, l0, l1);
            strip.Brush.Color = WithAlpha(Mix(color, Colors.White, f0), a);
            strip.Path.StrokeThickness = thickness * (0.45 + (0.95 * f0));
        }

        // 光晕：更宽更淡的一层，跟着头走，所以动感在光晕上也读得出来。
        // 它自己的不透明度用的是本段中点而不是起点，八段平均分布才不会整条偏亮
        for (var k = 0; k < BloomSegments; k++)
        {
            var f0 = k / (double)BloomSegments;
            var f1 = (k + 1) / (double)BloomSegments;

            var l0 = head - (tail * (1 - f0));
            var l1 = head - (tail * (1 - f1));

            var strip = _bloom[k];

            var mid = (f0 + f1) * 0.5;
            var a = _bandIntensity * mid * mid * BloomAlpha;
            if (l1 <= 0 || l0 >= _length || a <= 0.004)
            {
                strip.Path.Visibility = Visibility.Collapsed;
                continue;
            }

            strip.Path.Visibility = Visibility.Visible;
            SetStrip(strip, l0, l1);
            strip.Brush.Color = WithAlpha(Mix(color, Colors.White, mid), a);
            strip.Path.StrokeThickness = thickness + BloomWidth;
        }
    }

    private void HideComet()
    {
        for (var k = 0; k < TailSegments; k++)
        {
            _tail[k].Path.Visibility = Visibility.Collapsed;
        }

        for (var k = 0; k < BloomSegments; k++)
        {
            _bloom[k].Path.Visibility = Visibility.Collapsed;
        }
    }

    // 一段弧长上的描边。三点（起、中、末）足够：每段只有几像素长，而两端都是用 PointAtLength
    // 精确取出来的，比 Avalonia 那版「采样点 + 两端插值」还准一点，元素数却是固定的一比三。
    private void SetStrip(StrokeStrip strip, double from, double to)
    {
        var a = Math.Clamp(from, 0, _length);
        var b = Math.Clamp(to, 0, _length);

        strip.Figure.StartPoint = PointAtLength(a);
        strip.Mid.Point = PointAtLength((a + b) * 0.5);
        strip.End.Point = PointAtLength(b);
    }

    // 静息线的一层：自己的几何 + 自己的路径，颜色与厚度都归调用方（UpdateRestingLine）写。
    // hitTestable 只给最外那层（halo）开：命中面因此正好是画出来的最外圈描边（本体 + 9 ⇒ 半宽 5.5），
    // 与 Avalonia/WPF 由框架对描边做命中测试得到的带宽同量级。内侧两层落在它里面，
    // 开了只会多付命中测试、不增命中面积。
    private static RestingStroke CreateRestingStroke(Grid host, double thickness, bool hitTestable)
    {
        var segment = new BezierSegment();
        var figure = new PathFigure { IsClosed = false };
        figure.Segments.Add(segment);

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);

        var brush = new SolidColorBrush(Colors.Transparent);
        var path = new Path
        {
            Data = geometry,
            Stroke = brush,
            StrokeThickness = thickness,
            StrokeLineJoin = PenLineJoin.Round,
            // 圆头：两端因此不像被截断的横截面
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            IsHitTestVisible = hitTestable,
            Clip = null,
        };

        host.Children.Add(path);

        return new RestingStroke
        {
            Path = path,
            Geometry = geometry,
            Figure = figure,
            Segment = segment,
            Brush = brush,
        };
    }

    private static StrokeStrip CreateStrip(Grid host, double thickness)
    {
        var figure = new PathFigure { IsClosed = false };
        var mid = new LineSegment();
        var end = new LineSegment();
        figure.Segments.Add(mid);
        figure.Segments.Add(end);

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);

        var brush = new SolidColorBrush(Colors.Transparent);
        var path = new Path
        {
            Data = geometry,
            Stroke = brush,
            StrokeThickness = thickness,
            StrokeLineJoin = PenLineJoin.Round,
            // 圆头：头部因此是一个逐渐收拢的圆端，而不是截断的一刀；相邻两段也自然接得上
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            // 彗星不参与命中：它整段落在静息线那圈描边之内（不增命中面积），而它的几何每帧都在改写 ——
            // 让它可命中只会让命中面跟着光跑。命中面由静息线的 halo 一层给（见 CreateRestingStroke）。
            IsHitTestVisible = false,
            Clip = null,
        };

        host.Children.Add(path);

        return new StrokeStrip { Path = path, Figure = figure, Mid = mid, End = end, Brush = brush };
    }

    // 两色之间线性混合（含 alpha），用于尾梢到头部的那一段渐变
    private static Windows.UI.Color Mix(Windows.UI.Color from, Windows.UI.Color to, double t)
    {
        byte L(byte a, byte b) => (byte)Math.Round(a + ((b - a) * t));

        return Windows.UI.Color.FromArgb(L(from.A, to.A), L(from.R, to.R), L(from.G, to.G), L(from.B, to.B));
    }

    // 保留了色相、只换透明度的同色。Avalonia 那边是 new ImmutableSolidColorBrush(color, alpha)，
    // 这里是一支复用画刷改 Color —— 每帧不新建对象。
    private static Windows.UI.Color WithAlpha(Windows.UI.Color color, double alpha)
        => Windows.UI.Color.FromArgb(
            (byte)Math.Round(Math.Clamp(alpha, 0, 1) * 255),
            color.R,
            color.G,
            color.B);

    #endregion

    #region Interaction

    private void OnHoverPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        var pt = e.GetCurrentPoint(this).Position;
        var over = HitTestLine(pt);
        if (over && !IsHighlighted)
        {
            IsHighlighted = true;
            Focus(FocusState.Pointer);
        }
        else if (!over && IsHighlighted)
        {
            IsHighlighted = false;
        }
    }

    protected override void OnKeyDown(KeyRoutedEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Windows.System.VirtualKey.Delete && IsHighlighted)
        {
            DeleteLink();
            e.Handled = true;
        }
    }

    private void OnRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        var pt = e.GetPosition(this);

        // 只有落在画出来的线上的右键才算这条线的：沿弧长表逐段判距（半径 6）。
        // 视图的命中面就是画出来的那圈描边（只开了 halo 一层），两者同带宽；这条判据留着是为了命中面
        // 被改粗时（例如按 SlotView 那样给视图加 Background）也不会在空白处弹出菜单
        if (!HitTestLine(pt))
        {
            return;
        }

        // 未选中先选中 —— 菜单里的删除作用于当前这条线
        IsHighlighted = true;
        Focus(FocusState.Pointer);

        _menu ??= BuildMenu();

        // 代码建的 MenuFlyout 不属于任何元素，XamlRoot 得自己给它；视图此刻已上屏，拿到的就是它所在的那一棵
        _menu.XamlRoot = XamlRoot;
        _menu.ShowAt(this, new FlyoutShowOptions { Position = pt });

        e.Handled = true;
    }

    // 菜单只有一项，且不绑命令：视图会被池化改绑给另一条链接，菜单项在点击那一刻才去读视图自己的 DataContext
    private MenuFlyout? _menu;

    private MenuFlyout BuildMenu()
    {
        var item = new MenuFlyoutItem { Text = "删除连线" };
        item.Click += (_, _) => DeleteLink();

        return new MenuFlyout { Items = { item } };
    }

    private void DeleteLink()
    {
        if (DataContext is IWorkflowLinkViewModel vm)
        {
            vm.DeleteCommand.Execute(null);
        }
    }

    // 命中沿弧长表逐段判距，曲线因此整条都能点中，而不只是两端
    private bool HitTestLine(Point pt)
    {
        const double hitRadius = 6.0;

        // 没画出来的线不该能点中：表里那 129 个点跟着端点走，未就绪时它们还是上一次（或初始）的值
        if (!RenderReady)
        {
            return false;
        }

        for (var i = 1; i < _samples.Length; i++)
        {
            if (DistSeg(pt, _samples[i - 1], _samples[i]) <= hitRadius)
            {
                return true;
            }
        }

        return false;
    }

    private static double DistSeg(Point p, Point a, Point b)
    {
        double abx = b.X - a.X, aby = b.Y - a.Y;
        var len2 = (abx * abx) + (aby * aby);
        if (len2 < 0.0001)
        {
            return Math.Sqrt(((p.X - a.X) * (p.X - a.X)) + ((p.Y - a.Y) * (p.Y - a.Y)));
        }

        var t = Math.Clamp((((p.X - a.X) * abx) + ((p.Y - a.Y) * aby)) / len2, 0, 1);
        double px = a.X + (t * abx) - p.X, py = a.Y + (t * aby) - p.Y;
        return Math.Sqrt((px * px) + (py * py));
    }

    #endregion
}
