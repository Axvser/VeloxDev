using System.Collections.Specialized;
using System.ComponentModel;
using Microsoft.Maui.Graphics;
using VeloxDev.TransitionSystem;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

/// <summary>
/// Viewport-sized link layer for a workflow surface. Renders in ONE <see cref="GraphicsView"/> draw pass,
/// in the decorator's coordinate space — the same frame the grid and ruler use — the links that have no
/// view of their own: the immediate-mode hosts (one surface draws every link) and the frames before a
/// pooled link view has materialized. A link that published its curve together with the control that drew
/// it (<see cref="ILinkHitTestable.Visual"/>) is painted by that control and skipped here.
///
/// It deliberately does NOT live inside the scrolling world canvas (which grows by 1/Scale on
/// zoom-in): a canvas-sized GraphicsView exceeds the Win2D ~16k device-pixel texture cap at
/// deep zoom and the whole layer silently disappears. Sized to the viewport instead, the overlay
/// transforms each endpoint's collapsed canvas-local anchor <c>c</c> (= slot/node <see cref="Anchor"/>
/// value) to viewport pixels via the identity shared with the grid drawable:
///   px = RulerThickness + c + ContentOffset − ScrollOffset
/// where ContentOffset == Layout.ActualOffset (the world origin) and ScrollOffset == the
/// ScrollViewer offset. No negative-coordinate shift / TranslationX counter-compensation is
/// needed: geometry the canvas itself would clip simply never enters the viewport, and links
/// that fall outside it are culled by bounding box before drawing.
///
/// <para>
/// Each link is one cubic Bézier whose control points are pulled out horizontally, so it leaves each port
/// horizontally and turns through the middle with no corner in it. The travelling light is a comet cut out of
/// that curve <b>by arc length</b> — a bright head, a tail that fades behind it, a halo that follows — rather
/// than a coloured band mapped along a stub/diagonal/stub polyline. <see cref="ICanvas"/> has no stroked
/// gradient, but the comet never needed one: it is geometry sampled by arc length, tinted per piece, so the
/// light tracks the bend instead of the straight line between the two ends.
/// </para>
/// <para>
/// The layer is a painting surface only: its own <see cref="VisualElement.InputTransparent"/> stays
/// <see langword="true"/>, because a viewport-sized view that took input would swallow every canvas gesture
/// (pan, wheel zoom, node drag, slot drag) for the whole surface. Interaction is driven instead by
/// <see cref="InteractionSource"/> — a view that is already on the surface's input path — and the hit test is
/// geometric, walking each drawn curve's sample table: a link was never a view, so no platform hit test can
/// see it, and the drawn body is the only thing that answers. Hovering a link selects it (drawn in
/// <see cref="SelectedLinkColor"/>), <c>Delete</c> removes it through
/// <see cref="IWorkflowLinkViewModel.DeleteCommand"/>, and a right-click on it opens a one-item menu that
/// removes it.
/// </para>
/// </summary>
public sealed class WorkflowLinkOverlay : GraphicsView
{
    private const double CullMargin = 24d;

    private static readonly Color DefaultWhite = Color.FromArgb("#DDFFFFFF");

    // 七家统一的默认高亮色：白色。与 Core 的 LinkInteraction 默认高亮同义，只是这里是平台的颜色类型。
    private static readonly Color DefaultHighlight = Color.FromArgb("#FFFFFFFF");

    public static readonly BindableProperty WorkflowTreeProperty = BindableProperty.Create(
        nameof(WorkflowTree), typeof(IWorkflowTreeViewModel), typeof(WorkflowLinkOverlay), null,
        propertyChanged: OnWorkflowTreeChanged);

    public static readonly BindableProperty ScrollOffsetXProperty = BindableProperty.Create(
        nameof(ScrollOffsetX), typeof(double), typeof(WorkflowLinkOverlay), 0d, propertyChanged: OnVisualPropertyChanged);

    public static readonly BindableProperty ScrollOffsetYProperty = BindableProperty.Create(
        nameof(ScrollOffsetY), typeof(double), typeof(WorkflowLinkOverlay), 0d, propertyChanged: OnVisualPropertyChanged);

    public static readonly BindableProperty ContentOffsetXProperty = BindableProperty.Create(
        nameof(ContentOffsetX), typeof(double), typeof(WorkflowLinkOverlay), 0d, propertyChanged: OnVisualPropertyChanged);

    public static readonly BindableProperty ContentOffsetYProperty = BindableProperty.Create(
        nameof(ContentOffsetY), typeof(double), typeof(WorkflowLinkOverlay), 0d, propertyChanged: OnVisualPropertyChanged);

    public static readonly BindableProperty RulerThicknessProperty = BindableProperty.Create(
        nameof(RulerThickness), typeof(double), typeof(WorkflowLinkOverlay), 0d, propertyChanged: OnVisualPropertyChanged);

    public static readonly BindableProperty LinkLineColorProperty = BindableProperty.Create(
        nameof(LinkLineColor), typeof(Color), typeof(WorkflowLinkOverlay), DefaultWhite, propertyChanged: OnVisualPropertyChanged);

    public static readonly BindableProperty VirtualLineColorProperty = BindableProperty.Create(
        nameof(VirtualLineColor), typeof(Color), typeof(WorkflowLinkOverlay), DefaultWhite, propertyChanged: OnVisualPropertyChanged);

    public static readonly BindableProperty StrokeWidthProperty = BindableProperty.Create(
        nameof(StrokeWidth), typeof(double), typeof(WorkflowLinkOverlay), 4d, propertyChanged: OnVisualPropertyChanged);

    public static readonly BindableProperty LinkFlowEnabledProperty = BindableProperty.Create(
        nameof(LinkFlowEnabled), typeof(bool), typeof(WorkflowLinkOverlay), false, propertyChanged: OnLinkFlowEnabledChanged);

    public static readonly BindableProperty InteractionSourceProperty = BindableProperty.Create(
        nameof(InteractionSource), typeof(View), typeof(WorkflowLinkOverlay), null, propertyChanged: OnInteractionSourceChanged);

    // 高亮是「一团白而模糊的光」，不是换色：静息线本来就是近白的，所以靠**更亮 + 更粗 + 外面那圈光晕**
    // 读出来，而不是靠换一个色相。七家都用白（`#FFFFFFFF`）—— 其它色相都试过，红像告警、青像另一条线。
    public static readonly BindableProperty SelectedLinkColorProperty = BindableProperty.Create(
        nameof(SelectedLinkColor), typeof(Color), typeof(WorkflowLinkOverlay), DefaultHighlight, propertyChanged: OnVisualPropertyChanged);

    private IWorkflowTreeViewModel? _tree;
    private IWorkflowLinkViewModel? _virtualLink;
    private readonly HashSet<IWorkflowNodeViewModel> _subscribedNodes = [];
    private readonly HashSet<IWorkflowLinkViewModel> _subscribedLinks = [];
    private bool _invalidatePending;

    private double _bandCentre;
    private double _bandMix;

    // 指针事件源（见 InteractionSource）；输入本身不落在这层上，所以它是外部元素
    private View? _interactionSource;
#if !WINDOWS
    private PointerGestureRecognizer? _pointer;
#endif
    private IWorkflowLinkViewModel? _selectedLink;
    private Point? _lastPointer;

    // 当前这条链接的几何：canvas-local 的曲线（发布给 Core 做命中）+ 把它平移到视口的那一个偏移。
    // 绘制、弧长取点、命中三边共用这一份 —— 各处自己推一遍几何，弯的地方就会互相对不上。
    private LinkCurve? _curve;
    private float _curveOffsetX;
    private float _curveOffsetY;

    // 指针与键盘翻译成 Core 的标准输入事件后交给它裁决；本层只负责「把事件转发进去、把结果画出来」
    private LinkInteraction? _interaction;

#if WINDOWS
    private Microsoft.UI.Xaml.UIElement? _hookElement;
    private Microsoft.UI.Xaml.Input.PointerEventHandler? _hoverMovedHandler;
    private Microsoft.UI.Xaml.Input.PointerEventHandler? _hoverExitedHandler;
    private Microsoft.UI.Xaml.Input.PointerEventHandler? _secondaryPressedHandler;
    private Microsoft.UI.Xaml.Input.KeyEventHandler? _keyHandler;
    private Microsoft.UI.Xaml.Controls.MenuFlyout? _deleteMenu;
#endif

    public WorkflowLinkOverlay()
    {
        InputTransparent = true;
        Drawable = new LinkOverlayDrawable(this);

        // 逐帧动画要在 UI 线程上从 Loaded 起动、Unloaded 停止；移出树后不能还留着帧到达链接层。
        // 输入同理：挂到源平台元素上的键盘钩子会反向持有这层，离树时必须解掉
        Loaded += (_, _) =>
        {
            AttachInteractionSource(InteractionSource);
            StartFlow();
        };
        Unloaded += (_, _) =>
        {
            DetachInteractionSource();
            StopFlow();
        };
    }

    public IWorkflowTreeViewModel? WorkflowTree { get => (IWorkflowTreeViewModel?)GetValue(WorkflowTreeProperty); set => SetValue(WorkflowTreeProperty, value); }
    public double ScrollOffsetX { get => (double)GetValue(ScrollOffsetXProperty); set => SetValue(ScrollOffsetXProperty, value); }
    public double ScrollOffsetY { get => (double)GetValue(ScrollOffsetYProperty); set => SetValue(ScrollOffsetYProperty, value); }
    public double ContentOffsetX { get => (double)GetValue(ContentOffsetXProperty); set => SetValue(ContentOffsetXProperty, value); }
    public double ContentOffsetY { get => (double)GetValue(ContentOffsetYProperty); set => SetValue(ContentOffsetYProperty, value); }
    public double RulerThickness { get => (double)GetValue(RulerThicknessProperty); set => SetValue(RulerThicknessProperty, value); }
    public Color? LinkLineColor { get => (Color?)GetValue(LinkLineColorProperty); set => SetValue(LinkLineColorProperty, value); }
    public Color? VirtualLineColor { get => (Color?)GetValue(VirtualLineColorProperty); set => SetValue(VirtualLineColorProperty, value); }
    public double StrokeWidth { get => (double)GetValue(StrokeWidthProperty); set => SetValue(StrokeWidthProperty, value); }

    /// <summary>Whether a settled link carries the travelling highlight that shows which way its data
    /// flows. Off by default, because it is decoration: a minimal editor draws plain links.</summary>
    public bool LinkFlowEnabled { get => (bool)GetValue(LinkFlowEnabledProperty); set => SetValue(LinkFlowEnabledProperty, value); }

    /// <summary>The view whose pointer and keyboard input drives link interaction.
    /// <para>
    /// It must be a view that already sits on the surface's input path (an ancestor of the canvas), NOT this
    /// layer: this layer is viewport-sized and <see cref="VisualElement.InputTransparent"/>, so taking input
    /// here would swallow every canvas gesture. Set to <see langword="null"/> (the default) the layer stays
    /// purely visual, which is what the passive hosts want.
    /// </para>
    /// </summary>
    public View? InteractionSource { get => (View?)GetValue(InteractionSourceProperty); set => SetValue(InteractionSourceProperty, value); }

    /// <summary>Colour a hovered link is drawn in, so the selected one reads as picked rather than resting.</summary>
    public Color? SelectedLinkColor { get => (Color?)GetValue(SelectedLinkColorProperty); set => SetValue(SelectedLinkColorProperty, value); }

    /// <summary>Where the band is, as a fraction of a link's length from its sender's end. Written by the
    /// flow every frame; each link derives its geometry and colours from it while drawing.</summary>
    public double BandCentre
    {
        get => _bandCentre;
        set
        {
            _bandCentre = value;
            ScheduleInvalidate();
        }
    }

    /// <summary>How far the band has come up from the line's resting colour to the lit one: 0 is not there
    /// yet, 1 is fully lit. It climbs while the band enters and falls while it leaves.</summary>
    public double BandMix
    {
        get => _bandMix;
        set
        {
            _bandMix = value;
            ScheduleInvalidate();
        }
    }

    #region Flow effect

    // 三段相位各自结束时彗星头走过全长的比例：成形、全亮行进、到达熄灭。
    // 两头都是「没有光」的状态（起点强度为 0、终点强度回到 0），循环接缝才看不出来
    private const double FlowHeadStart = 0d;
    private const double FlowHeadFormed = 0.30;
    private const double FlowHeadLeaving = 0.78;
    private const double FlowHeadArrived = 1d;

    // 匀速（Eases.Default 就是恒等）—— 流水不该有缓动，头部的速度一变化就不像在流了
    private static readonly TimeSpan EnterDuration = TimeSpan.FromMilliseconds(450);
    private static readonly TimeSpan TravelDuration = TimeSpan.FromMilliseconds(700);
    private static readonly TimeSpan ExitDuration = TimeSpan.FromMilliseconds(450);

    // 整层只跑一个动画：本层一趟画完所有链接，彗星头因此一起前进；只动两个数（头的位置、亮度），
    // 各链接自推几何与颜色。
    // 相位成链：段的 LoopTime 只重复该段，Repeat 用首轮捕获的端点重跑整条链
    private static readonly Transition<WorkflowLinkOverlay> Flow =
        Transition<WorkflowLinkOverlay>.Create()
            // 相位一：从发送端出发，一边走一边亮起（走三成路程，同时由静息色变亮）
            .Property(o => o.BandCentre, FlowHeadFormed)
            .Property(o => o.BandMix, 1d)
            .Effect(new TransitionEffect()
            {
                Duration = EnterDuration,
                Ease = Eases.Default,
            })
            .Then()
            // 相位二：全亮行进——这一段读起来才是流动而非脉冲
            .Property(o => o.BandCentre, FlowHeadLeaving)
            .Effect(new TransitionEffect()
            {
                Duration = TravelDuration,
                Ease = Eases.Default,
            })
            .Then()
            // 相位三：到达并熄灭。两端都是「没有光」的状态，循环接缝才看不出来
            .Property(o => o.BandCentre, FlowHeadArrived)
            .Property(o => o.BandMix, 0d)
            .Effect(new TransitionEffect()
            {
                Duration = ExitDuration,
                Ease = Eases.Default,
            })
            .Repeat(int.MaxValue);

    // 起动流动；Loaded 与开关打开时都调，彗星因此总从发送端开始，而不是上次停在哪就从哪
    private void StartFlow()
    {
        if (!LinkFlowEnabled)
        {
            return;
        }

        // 链从目标读起始值，Execute 前先回到起点；循环在每个接缝重放这份捕获值
        _bandCentre = FlowHeadStart;
        _bandMix = 0d;
        Flow.Execute(this);
    }

    // 停流动，并让链释放它持有的资源
    private void StopFlow()
        => Transition.Exit(this, IncludeMutual: true, IncludeNoMutual: true);

    private static void OnLinkFlowEnabledChanged(BindableObject bindable, object? oldValue, object? newValue)
    {
        if (bindable is not WorkflowLinkOverlay overlay)
        {
            return;
        }

        if (newValue is true)
        {
            overlay.StartFlow();
        }
        else
        {
            overlay.StopFlow();
        }

        overlay.ScheduleInvalidate();
    }

    #endregion

    #region Geometry and comet

    // 拖尾占全长的比例。这是彗星唯一的观感旋钮：调大＝更长的尾、更像流光；调小＝更像一个亮点在跑。
    private const double TailFraction = 0.30;

    // 拖尾分几段画。每段一个透明度，衰减因此是连续的、不需要渐变刷 ——
    // ICanvas 没有描边渐变，但彗星本来就是「按弧长切几何、逐段给颜色」，这条限制反而不成立了。
    private const int TailSegments = 16;

    // 控制点的最小水平拉出量（canvas-local 单位）：两个端口靠得很近时，0.5·dx 会让曲线退化成
    // 一条直线段，失去「从端口水平出来」的形状。
    private const float PullMinimum = 40f;

    // 曲线按 Core 的默认密度采样，这里只留一份平移后的视口副本给绘制用：
    // 采样点、弧长、命中判定都归 LinkCurve，本层不再各存一份。
    private readonly PointF[] _samples = new PointF[LinkCurve.DefaultSampleCount];
    private PathF? _body;

    /// <summary>The resting curve of the link currently in the table, as one strokeable path.</summary>
    private PathF Body => _body!;

    /// <summary>Whether the curve last built has any length to draw.</summary>
    private bool HasCurve => _curve is { Length: > 0 } && _body is not null;

    /// <summary>
    /// Prepares the viewport copy of one link's published curve for painting: the curve itself stays in
    /// canvas-local coordinates (that is what Core hit-tests against) and only the draw path is translated.
    /// </summary>
    /// <remarks>
    /// The light travels along <b>arc length</b>, not along the straight line between the two ends: a gradient
    /// brush's axis is that straight line, so on a curve it lights the string rather than the rope — the
    /// brightness stops tracking the bend and the light appears to speed up and slow down as it goes round.
    /// A translation does not change length, so the arc-length table stays the curve's and is read from there.
    /// </remarks>
    private void BuildViewportGeometry(LinkCurve curve, float offsetX, float offsetY)
    {
        _curve = curve;
        _curveOffsetX = offsetX;
        _curveOffsetY = offsetY;

        // 平移不改变弧长，所以弧长一律问曲线，本层不再自己累一份。
        for (var i = 0; i < _samples.Length; i++)
        {
            _samples[i] = new PointF((float)curve.XAt(i) + offsetX, (float)curve.YAt(i) + offsetY);
        }

        // PathF 没有「清空」：这条链接的曲线只能重新装一条。一帧一条链接一次分配，
        // 换来的是一次 DrawPath 覆盖整条曲线（而不是上百次 DrawLine）
        var body = new PathF();
        body.MoveTo(_samples[0]);
        for (var i = 1; i < _samples.Length; i++)
        {
            body.LineTo(_samples[i]);
        }

        _body = body;
    }

    // 画布局部锚点 → 视口像素：px = Ruler + 锚点 + 内容偏移 − 滚动偏移（与网格 drawable 同一身份）。
    // 命中测试必须走同一条变换，否则它会去点一条和画出来的不是同一条的线
    private static float ToViewport(double ruler, double contentOffset, double scrollOffset, float anchor)
        => (float)(ruler + anchor + contentOffset - scrollOffset);

    /// <summary>Whole-curve bounding box against the viewport, expanded by the cull margin.</summary>
    private bool IntersectsViewport(float left, float right, float top, float bottom)
    {
        var minX = float.MaxValue;
        var maxX = float.MinValue;
        var minY = float.MaxValue;
        var maxY = float.MinValue;

        for (var i = 0; i < _samples.Length; i++)
        {
            var point = _samples[i];
            if (point.X < minX) minX = point.X;
            if (point.X > maxX) maxX = point.X;
            if (point.Y < minY) minY = point.Y;
            if (point.Y > maxY) maxY = point.Y;
        }

        return maxX >= left && minX <= right && maxY >= top && minY <= bottom;
    }

    // 弧长 → 视口点。取点归曲线（二分 + 段内插值，精确到亚像素），这里只补上那一个平移。
    private PointF PointAtLength(float length)
    {
        var (x, y) = _curve!.PointAtLength(length);
        return new PointF((float)x + _curveOffsetX, (float)y + _curveOffsetY);
    }

    // 取 [from, to] 这一段弧长上的折线几何。两端各自插值到精确位置，中间用现成采样点
    private void StrokeSegment(ICanvas canvas, float from, float to)
    {
        var total = (float)_curve!.Length;
        to = MathF.Min(to, total);
        from = Math.Clamp(from, 0, total);
        if (to <= from)
        {
            return;
        }

        var previous = PointAtLength(from);

        for (var i = 0; i < _samples.Length; i++)
        {
            var length = (float)_curve.LengthAt(i);
            if (length <= from || length >= to)
            {
                continue;
            }

            canvas.DrawLine(previous.X, previous.Y, _samples[i].X, _samples[i].Y);
            previous = _samples[i];
        }

        var last = PointAtLength(to);
        canvas.DrawLine(previous.X, previous.Y, last.X, last.Y);
    }

    // 按比例压暗本色（Avalonia 那边是给画刷加一个 Opacity，效果就是 alpha 相乘）
    private static Color Fade(Color color, double factor)
        => color.WithAlpha((float)(color.Alpha * factor));

    // 两色之间线性混合（含 alpha），用于尾梢到头部的那一段渐变
    private static Color Mix(Color from, Color to, float t) => new(
        from.Red + ((to.Red - from.Red) * t),
        from.Green + ((to.Green - from.Green) * t),
        from.Blue + ((to.Blue - from.Blue) * t),
        from.Alpha + ((to.Alpha - from.Alpha) * t));

    /// <summary>
    /// The travelling light: one comet — a bright head, a tail that fades behind it, and a halo.
    /// <para>
    /// It is cut out of the curve <b>by arc length</b>, not painted with a gradient brush: <see cref="ICanvas"/>
    /// has no stroked-gradient equivalent, and a gradient's axis would in any case be the straight line between
    /// the two ends, so the light would drift off the rope and onto the string as soon as the link bends.
    /// </para>
    /// </summary>
    private void DrawComet(ICanvas canvas, Color color, float thickness)
    {
        var head = (float)(Math.Clamp(_bandCentre, 0, 1) * _curve!.Length);
        var tail = (float)(TailFraction * _curve.Length);
        if (tail <= 0)
        {
            return;
        }

        // 两遍：先光晕（更宽更淡）再本体，两遍都跟着头走，所以动感在光晕上也读得出来
        for (var pass = 0; pass < 2; pass++)
        {
            var bloom = pass == 0;

            for (var k = 0; k < TailSegments; k++)
            {
                var f0 = k / (float)TailSegments;      // 0 = 尾梢，1 = 头
                var f1 = (k + 1) / (float)TailSegments;

                var l0 = head - (tail * (1 - f0));
                var l1 = head - (tail * (1 - f1));
                if (l1 <= 0 || l0 >= _curve.Length)
                {
                    continue;
                }

                // 平方衰减：让透明集中在尾段，读起来才像拖尾而不是一条均匀的带
                var alpha = _bandMix * f0 * f0;
                if (alpha <= 0.004)
                {
                    continue;
                }

                // 尾梢是本体的颜色，越靠近头越白 —— 白热只发生在头部
                var segmentColor = Mix(color, Colors.White, f0);

                canvas.StrokeColor = bloom ? Fade(segmentColor, alpha * 0.22) : Fade(segmentColor, alpha);
                canvas.StrokeSize = bloom ? thickness + 9 : thickness * (0.45f + (0.95f * f0));

                StrokeSegment(canvas, l0, l1);
            }
        }
    }

    #endregion

    #region Interaction

    private static void OnInteractionSourceChanged(BindableObject bindable, object? oldValue, object? newValue)
    {
        if (bindable is WorkflowLinkOverlay overlay)
        {
            overlay.AttachInteractionSource(newValue as View);
        }
    }

    // 换源只解绑旧的，不重建状态：宿主重复绑定同一个源时不该把选中丢掉
    private void AttachInteractionSource(View? source)
    {
        if (ReferenceEquals(_interactionSource, source))
        {
            return;
        }

        DetachInteractionSource();

        _interactionSource = source;
        if (source is null)
        {
            return;
        }

#if WINDOWS
        // 源自己的平台元素要等它的 handler 建好；HandlerChanged 会在那之后来一发
        source.HandlerChanged += OnInteractionSourceHandlerChanged;
        source.HandlerChanging += OnInteractionSourceHandlerChanging;
        AttachPlatformHooks();
#else
        // 这层自己是 InputTransparent，收不到指针；改由表面已有的输入路径上的元素代收。
        // PointerGestureRecognizer 只是监听，不置 Handled，画布手势因此不受影响
        _pointer = new PointerGestureRecognizer
        {
            Buttons = ButtonsMask.Primary | ButtonsMask.Secondary,
        };
        _pointer.PointerMoved += OnGesturePointerMoved;
        _pointer.PointerExited += OnGesturePointerExited;
        _pointer.PointerPressed += OnGesturePointerPressed;
        source.GestureRecognizers.Add(_pointer);
#endif
    }

    private void DetachInteractionSource()
    {
#if !WINDOWS
        if (_pointer is not null && _interactionSource is not null)
        {
            _interactionSource.GestureRecognizers.Remove(_pointer);
        }

        _pointer?.PointerMoved -= OnGesturePointerMoved;
        _pointer?.PointerExited -= OnGesturePointerExited;
        _pointer?.PointerPressed -= OnGesturePointerPressed;
        _pointer = null;
#endif

#if WINDOWS
        if (_interactionSource is not null)
        {
            _interactionSource.HandlerChanged -= OnInteractionSourceHandlerChanged;
            _interactionSource.HandlerChanging -= OnInteractionSourceHandlerChanging;
        }

        DetachPlatformHooks();
#endif

        _interactionSource = null;
        _lastPointer = null;
        SelectLink(null);
    }

#if WINDOWS
    private void OnInteractionSourceHandlerChanged(object? sender, EventArgs e) => AttachPlatformHooks();

    private void OnInteractionSourceHandlerChanging(object? sender, HandlerChangingEventArgs e) => DetachPlatformHooks();
#endif

    // 悬停落在哪条线上由 Core 裁决（见 LinkInteraction）：本层只把指针翻译成标准事件转发进去。
    // 拉线时指针下面正挂着橡皮筋，逐帧判悬停只会把沿途那些实连线点亮
    private void OnHoverMoved(Point point)
    {
        if (WorkflowSlotConnectionBehavior.IsDraggingConnection)
        {
            return;
        }

        // 同一位置的重复消息（子元素进出会连发）不必再转发一次
        if (_lastPointer is { } last
            && Math.Abs(last.X - point.X) < 0.5
            && Math.Abs(last.Y - point.Y) < 0.5)
        {
            return;
        }

        _lastPointer = point;
        _interaction?.Publish(new PointerEvent(PointerPhase.Moved, ToCanvasLocal(point)));
    }

    // 指针离开整块输入面：选中跟着走 —— 高亮留在身后会让「现在按 Delete 删哪条」变得没有答案。
    // 菜单弹出引起的那一次离开不算：指针是飞到菜单上去了，不是移开了这条线
    private void OnHoverExited()
    {
        if (_interaction?.IsSuspended == true)
        {
            return;
        }

        _interaction?.Publish(new PointerEvent(PointerPhase.Exited, new Anchor()));
    }

    private void OnSecondaryPressed(Point onOverlay, Point onSource)
    {
        _interaction?.Publish(new PointerEvent(
            PointerPhase.Pressed, ToCanvasLocal(onOverlay), PointerButtonKind.Right));

        if (_interaction?.HoveredLink is null)
        {
            // 空白处右键不是这条线的事：不置 Handled，也不弹菜单
            return;
        }

        ShowDeleteMenu(onSource);
    }

    // 视口像素 → canvas-local 锚点：ToViewport 的逆（一条纯平移，所以逐轴减回去就是）。
    // 指针只有过了这一步才能和发布给 Core 的曲线比 —— 两边不在一个坐标系里，命中的就是另一条线。
    private Anchor ToCanvasLocal(Point point)
    {
        var ruler = Math.Max(0d, RulerThickness);
        return new Anchor(
            point.X - ruler - ContentOffsetX + ScrollOffsetX,
            point.Y - ruler - ContentOffsetY + ScrollOffsetY,
            0);
    }

#if !WINDOWS
    private void OnGesturePointerMoved(object? sender, PointerEventArgs e)
    {
        if (e.GetPosition(this) is { } point)
        {
            OnHoverMoved(point);
        }
    }

    private void OnGesturePointerExited(object? sender, PointerEventArgs e) => OnHoverExited();

    private void OnGesturePointerPressed(object? sender, PointerEventArgs e)
    {
        if (e.Button == ButtonsMask.Secondary
            && _interactionSource is not null
            && e.GetPosition(this) is { } onOverlay
            && e.GetPosition(_interactionSource) is { } onSource)
        {
            OnSecondaryPressed(onOverlay, onSource);
        }
    }
#endif

    private void SelectLink(IWorkflowLinkViewModel? link)
    {
        if (ReferenceEquals(_selectedLink, link))
        {
            return;
        }

        _selectedLink = link;

        // 选中是「上色」，Delete 要的是键盘焦点 —— 两者必须同时发生：只上色不取焦点的版本会让
        // 键事件落在别处（焦点不在源里，KeyDown 就不会经过挂钩子的那个元素），于是必须先点一下才删得掉
        if (link is not null)
        {
            _interactionSource?.Focus();
        }

        ScheduleInvalidate();
    }

    private void DeleteSelectedLink()
    {
        var link = _selectedLink;
        if (link is null)
        {
            return;
        }

        _selectedLink = null;
        if (link.DeleteCommand.CanExecute(null))
        {
            link.DeleteCommand.Execute(null);
        }

        ScheduleInvalidate();
    }

#if WINDOWS
    // 指针与键盘都挂在源自己的平台元素上，而且只监听、不置 Handled —— 画布的手势因此一点不受影响。
    // 为什么不走 MAUI 的 PointerGestureRecognizer：它在 Windows 上不派发 PointerMoved（悬停因此永远不亮），
    // 而这层又不可能自己收指针事件（InputTransparent，且它压在滚动视图下面）
    private void AttachPlatformHooks()
    {
        if (_interactionSource?.Handler?.PlatformView is not Microsoft.UI.Xaml.UIElement element
            || _hookElement == element)
        {
            return;
        }

        DetachPlatformHooks();
        _hoverMovedHandler = (_, e) =>
        {
            if (ToOverlayPoint(e) is { } point)
            {
                OnHoverMoved(point);
            }
        };
        _hoverExitedHandler = (_, _) => OnHoverExited();
        _secondaryPressedHandler = (_, e) =>
        {
            if (e.GetCurrentPoint(element).Properties.IsRightButtonPressed
                && ToOverlayPoint(e) is { } onOverlay
                && ToElementPoint(e, element) is { } onSource)
            {
                OnSecondaryPressed(onOverlay, onSource);
            }
        };

        // 键盘挂同一处：MAUI 没有跨平台的按键事件，而 Delete 要的是「悬停已经选中了哪条」。
        // handledEventsToo 取 false 是故意的 —— 聚焦的输入框吃掉 Delete 改自己的光标时必须让它赢
        _keyHandler = OnSourceKeyDown;

        element.AddHandler(Microsoft.UI.Xaml.UIElement.PointerMovedEvent, _hoverMovedHandler, true);
        element.AddHandler(Microsoft.UI.Xaml.UIElement.PointerExitedEvent, _hoverExitedHandler, true);
        element.AddHandler(Microsoft.UI.Xaml.UIElement.PointerPressedEvent, _secondaryPressedHandler, true);
        element.AddHandler(Microsoft.UI.Xaml.UIElement.KeyDownEvent, _keyHandler, false);
        _hookElement = element;
    }

    private void DetachPlatformHooks()
    {
        if (_hookElement is null)
        {
            return;
        }

        if (_hoverMovedHandler is not null)
        {
            _hookElement.RemoveHandler(Microsoft.UI.Xaml.UIElement.PointerMovedEvent, _hoverMovedHandler);
        }

        if (_hoverExitedHandler is not null)
        {
            _hookElement.RemoveHandler(Microsoft.UI.Xaml.UIElement.PointerExitedEvent, _hoverExitedHandler);
        }

        if (_secondaryPressedHandler is not null)
        {
            _hookElement.RemoveHandler(Microsoft.UI.Xaml.UIElement.PointerPressedEvent, _secondaryPressedHandler);
        }

        if (_keyHandler is not null)
        {
            _hookElement.RemoveHandler(Microsoft.UI.Xaml.UIElement.KeyDownEvent, _keyHandler);
        }

        _hookElement = null;
        _hoverMovedHandler = null;
        _hoverExitedHandler = null;
        _secondaryPressedHandler = null;
        _keyHandler = null;
    }

    /// <summary>
    /// The pointer position in this layer's own coordinates — the frame the draw pass works in.
    /// <para>
    /// Asked of the platform element directly, not of MAUI: the overlay's platform element is what
    /// <see cref="ICanvas"/> draws into, so its space is the drawn one by construction.
    /// </para>
    /// </summary>
    private Point? ToOverlayPoint(Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        => this.Handler?.PlatformView is Microsoft.UI.Xaml.UIElement overlay && ToElementPoint(e, overlay) is { } point
            ? point
            : null;

    private static Point? ToElementPoint(Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e, Microsoft.UI.Xaml.UIElement element)
    {
        var position = e.GetCurrentPoint(element).Position;
        return new Point(position.X, position.Y);
    }

    private void OnSourceKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Delete)
        {
            return;
        }

        // 键也过 Core：「现在按 Delete 删哪条」因此与其它六家是同一个答案，不靠各家各记一个 _selectedLink
        if (_interaction?.HoveredLink is null)
        {
            return;
        }

        _interaction.Publish(new KeyEvent(InputKey.Delete));
        e.Handled = true;
    }

    // 删除菜单：只有一项、落在右键处，且只在右键点在连线上时弹。
    // 用平台的 MenuFlyout 而不是 MAUI 那个 —— 后者只能当 ContextFlyout 挂，右键落在哪都弹，
    // 落在空白处也取消不了，而这条契约要求「只有点在连线上才弹」
    private void ShowDeleteMenu(Point onSource)
    {
        if (_interactionSource?.Handler?.PlatformView is not Microsoft.UI.Xaml.UIElement element)
        {
            return;
        }

        if (_deleteMenu is null)
        {
            var item = new Microsoft.UI.Xaml.Controls.MenuFlyoutItem { Text = "删除连线" };
            item.Click += (_, _) => DeleteSelectedLink();
            _deleteMenu = new Microsoft.UI.Xaml.Controls.MenuFlyout();
            _deleteMenu.Items.Add(item);
            _deleteMenu.Closed += (_, _) =>
            {
                if (_interaction is not null) _interaction.IsSuspended = false;
            };
        }

        // 菜单一开指针就落到菜单上，那之后的移动与离开都不该把菜单针对的这条线取消选中
        if (_interaction is not null) _interaction.IsSuspended = true;
        _deleteMenu.ShowAt(element, new Windows.Foundation.Point(onSource.X, onSource.Y));
    }
#else
    // 右键菜单是这家平台侧的能力缺口：MAUI 的跨平台 MenuFlyout 只能整层挂成 ContextFlyout，
    // 无法「只在连线上弹」。其余平台因此只有悬停高亮与 Delete
    private void ShowDeleteMenu(Point onSource)
    {
    }
#endif

    #endregion

    private static void OnWorkflowTreeChanged(BindableObject bindable, object? oldValue, object? newValue)
    {
        if (bindable is WorkflowLinkOverlay overlay)
        {
            overlay.AttachTree(newValue as IWorkflowTreeViewModel);
            overlay.ScheduleInvalidate();
        }
    }

    private static void OnVisualPropertyChanged(BindableObject bindable, object? oldValue, object? newValue)
    {
        if (bindable is WorkflowLinkOverlay overlay)
        {
            overlay.ScheduleInvalidate();
        }
    }

    // ── Model subscriptions (mirrors the superseded LinkLayerView pattern) ─────

    private void AttachTree(IWorkflowTreeViewModel? tree)
    {
        if (ReferenceEquals(_tree, tree))
        {
            return;
        }

        Unsubscribe();
        DetachInteraction();
        _tree = tree;
        // 换树时旧的选中项已经不属于这棵树了，留着它会让 Delete 去打一个不在场上的连线
        _selectedLink = null;
        if (tree is null)
        {
            return;
        }

        AttachInteraction(tree);
        Subscribe(tree);
    }

    // 交互归 Core：本层只做两件平台的事 —— 把原生指针/按键翻译成标准输入事件转发进去，
    // 再把裁决结果画出来（选中上色）。命中的算法不在这家。
    private void AttachInteraction(IWorkflowTreeViewModel tree)
    {
        // hub 只有一个位置：同树同实例（别家也走这个调用，不再各自发明取用方式）。
        // 删除归 hub 自己（AutoDelete）—— 本家不再订 LinkDeleteRequested。
        var interaction = LinkInteraction.For(tree);
        // 本家一个表面画完所有线、没有「每线的可视对象」，所以 hub 的 AutoHighlight 没有东西可点：
        // 选中由这里订阅 HoverChanged 自己画。别家是视觉实现 ILinkHighlight、由 hub 直接点亮。
        interaction.HoverChanged += OnHoverChanged;
        _interaction = interaction;
    }

    private void DetachInteraction()
    {
        if (_interaction is not { } interaction)
        {
            return;
        }

        interaction.HoverChanged -= OnHoverChanged;
        _interaction = null;
    }

    private void OnHoverChanged(object? sender, LinkHoverEventArgs e) => SelectLink(e.Link);

    private void Subscribe(IWorkflowTreeViewModel tree)
    {
        if (tree is INotifyPropertyChanged npc)
        {
            npc.PropertyChanged += OnTreePropertyChanged;
        }

        if (tree.Nodes is INotifyCollectionChanged nc)
        {
            nc.CollectionChanged += OnNodesChanged;
            foreach (var n in tree.Nodes)
            {
                SubscribeNode(n);
            }
        }

        if (tree.Links is INotifyCollectionChanged lc)
        {
            lc.CollectionChanged += OnLinksChanged;
            foreach (var l in tree.Links)
            {
                SubscribeLink(l);
            }
        }

        SubscribeVirtualLink(tree.VirtualLink);
    }

    private void Unsubscribe()
    {
        if (_tree is null)
        {
            return;
        }

        if (_tree is INotifyPropertyChanged npc)
        {
            npc.PropertyChanged -= OnTreePropertyChanged;
        }

        if (_tree.Nodes is INotifyCollectionChanged nc)
        {
            nc.CollectionChanged -= OnNodesChanged;
        }

        if (_tree.Links is INotifyCollectionChanged lc)
        {
            lc.CollectionChanged -= OnLinksChanged;
        }

        foreach (var n in _subscribedNodes)
        {
            if (n is INotifyPropertyChanged np)
            {
                np.PropertyChanged -= OnNodePropertyChanged;
            }
        }

        _subscribedNodes.Clear();

        foreach (var l in _subscribedLinks)
        {
            UnsubscribeLink(l);
        }

        _subscribedLinks.Clear();
        SubscribeVirtualLink(null);
        _tree = null;
    }

    private void SubscribeNode(IWorkflowNodeViewModel node)
    {
        if (_subscribedNodes.Add(node) && node is INotifyPropertyChanged npc)
        {
            npc.PropertyChanged += OnNodePropertyChanged;
        }
    }

    private void SubscribeLink(IWorkflowLinkViewModel link)
    {
        if (!_subscribedLinks.Add(link))
        {
            return;
        }

        if (link is INotifyPropertyChanged npc)
        {
            npc.PropertyChanged += OnLinkPropertyChanged;
        }

        if (link.Sender is INotifyPropertyChanged sp)
        {
            sp.PropertyChanged += OnSlotPropertyChanged;
        }

        if (link.Receiver is INotifyPropertyChanged rp)
        {
            rp.PropertyChanged += OnSlotPropertyChanged;
        }
    }

    private void UnsubscribeLink(IWorkflowLinkViewModel link)
    {
        if (link is INotifyPropertyChanged npc)
        {
            npc.PropertyChanged -= OnLinkPropertyChanged;
        }

        if (link.Sender is INotifyPropertyChanged sp)
        {
            sp.PropertyChanged -= OnSlotPropertyChanged;
        }

        if (link.Receiver is INotifyPropertyChanged rp)
        {
            rp.PropertyChanged -= OnSlotPropertyChanged;
        }

        _subscribedLinks.Remove(link);
    }

    private void SubscribeVirtualLink(IWorkflowLinkViewModel? link)
    {
        if (ReferenceEquals(_virtualLink, link))
        {
            return;
        }

        if (_virtualLink is not null)
        {
            UnsubscribeLink(_virtualLink);
        }

        _virtualLink = link;
        if (link is not null)
        {
            SubscribeLink(link);
        }
    }

    private void OnTreePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IWorkflowTreeViewModel.VirtualLink) && _tree is not null)
        {
            SubscribeVirtualLink(_tree.VirtualLink);
            ScheduleInvalidate();
        }
    }

    private void OnNodesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
        {
            foreach (var i in e.NewItems)
            {
                if (i is IWorkflowNodeViewModel n)
                {
                    SubscribeNode(n);
                }
            }
        }

        if (e.OldItems is not null)
        {
            foreach (var i in e.OldItems)
            {
                if (i is IWorkflowNodeViewModel n && _subscribedNodes.Remove(n) && n is INotifyPropertyChanged npc)
                {
                    npc.PropertyChanged -= OnNodePropertyChanged;
                }
            }
        }

        ScheduleInvalidate();
    }

    private void OnLinksChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
        {
            foreach (var i in e.NewItems)
            {
                if (i is IWorkflowLinkViewModel l)
                {
                    SubscribeLink(l);
                }
            }
        }

        if (e.OldItems is not null)
        {
            foreach (var i in e.OldItems)
            {
                if (i is IWorkflowLinkViewModel l)
                {
                    UnsubscribeLink(l);
                    // 删掉的正好是选中的那条（Delete、右键菜单、撤销都走这里）—— 选中必须跟着消失
                    if (ReferenceEquals(_selectedLink, l))
                    {
                        _selectedLink = null;
                    }
                }
            }
        }

        ScheduleInvalidate();
    }

    private void OnNodePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IWorkflowNodeViewModel.Anchor) or nameof(IWorkflowNodeViewModel.Size))
        {
            ScheduleInvalidate();
        }
    }

    private void OnLinkPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IWorkflowLinkViewModel.IsVisible))
        {
            ScheduleInvalidate();
            return;
        }

        if (e.PropertyName is nameof(IWorkflowLinkViewModel.Sender) or nameof(IWorkflowLinkViewModel.Receiver)
            && sender is IWorkflowLinkViewModel link && _subscribedLinks.Contains(link))
        {
            // Endpoints rewired — re-subscribe to the new slots' anchors.
            if (link.Sender is INotifyPropertyChanged sp)
            {
                sp.PropertyChanged -= OnSlotPropertyChanged;
                sp.PropertyChanged += OnSlotPropertyChanged;
            }

            if (link.Receiver is INotifyPropertyChanged rp)
            {
                rp.PropertyChanged -= OnSlotPropertyChanged;
                rp.PropertyChanged += OnSlotPropertyChanged;
            }

            ScheduleInvalidate();
        }
    }

    private void OnSlotPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IWorkflowSlotViewModel.Anchor))
        {
            ScheduleInvalidate();
        }
    }

    /// <summary>
    /// A scroll/pan/zoom frame writes several offset DPs back-to-back, and every model
    /// event above wants a redraw too. Coalesce them into ONE Win2D Invalidate per frame
    /// by flushing at the next main-thread dispatch instead of invalidating inline.
    /// </summary>
    private void ScheduleInvalidate()
    {
        if (_invalidatePending)
        {
            return;
        }

        _invalidatePending = true;
        MainThread.BeginInvokeOnMainThread(FlushInvalidate);
    }

    private void FlushInvalidate()
    {
        _invalidatePending = false;
        Invalidate();
    }

    // ── Drawing ──────────────────────────────────────────────────────────────

    // 枚举源是 Core 的虚拟化可见集，不是全量的 tree.Links —— 每帧代价是 O(可见) 而不是 O(全部)。
    // 按可见集裁剪不会漏画：曲线恒在两端节点包围盒的并集内（NodePairBoundsProvider），并集不与视口相交时它也不可能可见。
    private static IEnumerable<IWorkflowLinkViewModel> EnumerateVisibleLinks(IWorkflowTreeViewModel tree)
    {
        var virtualLink = tree.VirtualLink;

        foreach (var item in tree.GetHelper().VisibleItems)
        {
            // 可见集里也带着当前虚拟连线（Virtualize 把它放在首位）；它由下面单独补在最后 —— 橡皮筋要压在实连线之上
            if (item is IWorkflowLinkViewModel link && link.IsVisible && !ReferenceEquals(link, virtualLink))
            {
                yield return link;
            }
        }

        if (virtualLink is { IsVisible: true })
        {
            yield return virtualLink;
        }
    }

    private static bool TryGetEndpoints(IWorkflowLinkViewModel link, out float startX, out float startY, out float endX, out float endY)
    {
        startX = (float)link.Sender.Anchor.Horizontal;
        startY = (float)link.Sender.Anchor.Vertical;
        endX = (float)link.Receiver.Anchor.Horizontal;
        endY = (float)link.Receiver.Anchor.Vertical;
        // A slot that has not been laid out yet has a NaN anchor — skip it rather
        // than feed NaN through Win2D.
        return !float.IsNaN(startX) && !float.IsNaN(startY) && !float.IsNaN(endX) && !float.IsNaN(endY);
    }

    private static bool IsVirtualLink(IWorkflowLinkViewModel link)
        => link.Sender.Parent is null || link.Receiver.Parent is null;

    private sealed class LinkOverlayDrawable(WorkflowLinkOverlay owner) : IDrawable
    {
        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            var tree = owner._tree;
            if (tree is null || dirtyRect.Width <= 0 || dirtyRect.Height <= 0)
            {
                return;
            }

            var ruler = Math.Max(0d, owner.RulerThickness);
            var ox = owner.ContentOffsetX;
            var oy = owner.ContentOffsetY;
            var scrollX = owner.ScrollOffsetX;
            var scrollY = owner.ScrollOffsetY;
            var linkColor = owner.LinkLineColor;
            var virtualColor = owner.VirtualLineColor;
            var strokeWidth = (float)Math.Max(0.5d, owner.StrokeWidth);

            // Viewport bounds (expanded a little so a link edge never pops at the frame boundary).
            var left = dirtyRect.Left - (float)CullMargin;
            var right = dirtyRect.Right + (float)CullMargin;
            var top = dirtyRect.Top - (float)CullMargin;
            var bottom = dirtyRect.Bottom + (float)CullMargin;

            // Transform each collapsed canvas-local anchor c to viewport pixels:
            // px = Ruler + c + ContentOffset − ScrollOffset (shared with the grid drawable).
            foreach (var link in EnumerateVisibleLinks(tree))
            {
                // 这条线已经有自己的视图在画（视图发布曲线时把「画它的那个控件」一并交了上来）——
                // 这层只画没人画的：立即模式的主机，以及池化视图还没物化出来的那几帧
                if (link.HitTarget()?.Visual is not null)
                {
                    continue;
                }

                if (!TryGetEndpoints(link, out var csx, out var csy, out var cex, out var cey))
                {
                    continue;
                }

                var isVirtual = IsVirtualLink(link);
                // 选中的线换成选中色并加粗 1.5：与其它六家同一种读法（高亮 = 偏红 + 更粗）
                var isSelected = ReferenceEquals(link, owner._selectedLink);
                var color = isSelected
                    ? owner.SelectedLinkColor ?? DefaultHighlight
                    : isVirtual ? virtualColor : linkColor;
                if (color is null)
                {
                    continue;
                }

                // 几何在 canvas-local 定下来：曲线发布给 Core 做命中，本层只把它平移到视口来画。
                // 于是「画出来的」与「能点中的」是同一条曲线，不是两次各自推导的结果。
                var curve = LinkCurve.BuildCubic(
                    csx, csy, cex, cey, PullMinimum, LinkCurve.DefaultSampleCount);
                link.PublishCurve(curve);

                owner.BuildViewportGeometry(
                    curve,
                    ToViewport(ruler, ox, scrollX, 0),
                    ToViewport(ruler, oy, scrollY, 0));
                if (!owner.HasCurve)
                {
                    continue;
                }

                // Whole-link bounding-box cull: segments outside the viewport are clipped by the
                // canvas anyway, so drawing them is pure waste (and deep zoom could send huge
                // coordinates through Win2D otherwise).
                if (!owner.IntersectsViewport(left, right, top, bottom))
                {
                    continue;
                }

                canvas.StrokeDashPattern = isVirtual ? [4f, 2f] : null;
                canvas.StrokeLineCap = LineCap.Round;

                // 选中的线整条（管壁与彗星都在内）加粗 1.5，与 Avalonia / WPF 的选中读法一致
                var thickness = isSelected ? strokeWidth + 1.5f : strokeWidth;

                // 管壁：两层更宽的同色低透明描边垫在下面，整条线因此像在发光而不是贴在背景上。圆头圆角，
                // 两端才不像被截断的横截面
                canvas.StrokeColor = Fade(color, 0.10);
                canvas.StrokeSize = thickness + 9;
                canvas.DrawPath(owner.Body);

                canvas.StrokeColor = Fade(color, 0.16);
                canvas.StrokeSize = thickness + 4;
                canvas.DrawPath(owner.Body);

                // 虚拟连线是指针下的橡皮筋：虚线、不流动
                if (isVirtual)
                {
                    canvas.StrokeColor = Fade(color, 0.75);
                    canvas.StrokeSize = thickness;
                    canvas.DrawPath(owner.Body);
                    canvas.StrokeDashPattern = null;
                    continue;
                }

                // 线体本身是静息的：光不在时它只是一根暗线，有了对比彗星才亮得出来
                canvas.StrokeColor = Fade(color, isSelected ? 0.85 : 0.55);
                canvas.StrokeSize = thickness;
                canvas.DrawPath(owner.Body);

                if (owner.LinkFlowEnabled && owner.BandMix > 0.001)
                {
                    owner.DrawComet(canvas, color, thickness);
                }

                canvas.StrokeDashPattern = null;
            }
        }

    }
}
