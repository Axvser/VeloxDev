using System.ComponentModel;
using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using VeloxDev.TransitionSystem;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace Demo.Components.Workflow;

/// <summary>
/// A Blazor workflow link view: one cubic curve that leaves each end horizontally, with a comet
/// travelling along it.
/// <para>
/// The comet is a bright head, a tail that fades behind it, and a halo that follows the head — and it
/// is cut out of the curve <b>by arc length</b> rather than by a gradient brush. That is the whole
/// reason this view keeps its own sample table: a <c>LinearGradientBrush</c>'s axis is the straight
/// line between the two ends, so on a curve it lights the string rather than the rope.
/// </para>
/// <para>
/// The view is also the object the flow animates — <c>Transition&lt;TemplateLinkView&gt;</c>. A Razor
/// component is a class, so the cycle's position is this component's own member and the markup reads
/// it straight off, with nothing in between to map back into stops. There is exactly one animated
/// value, <see cref="Phase"/>: the head's position and the comet's intensity are both derived from it
/// rather than being two animations that could interrupt each other.
/// </para>
/// <para>
/// Every number the SVG carries goes through <see cref="N"/>. Razor writes a bare <c>double</c> in the
/// current culture, and a comma decimal separator produces an attribute the browser silently drops —
/// which for a path means no path at all.
/// </para>
/// </summary>
public partial class TemplateLinkView : ComponentBase, IDisposable, ILinkHighlight
{
    // 控制点的最小水平拉出量：两个端口靠得很近时，0.5·dx 会让曲线退化成一条直线段。
    private const double PullMinimum = 40;

    // 拖尾占全长的比例。这是彗星唯一的观感旋钮：调大＝更长的尾、更像流光；调小＝更像一个亮点在跑。
    private const double TailFraction = 0.30;

    // 拖尾分几段画。每段一个颜色与一个不透明度，衰减因此是连续的，不需要渐变刷。
    private const int TailSegments = 16;

    // 三段相位各自结束时头部走过的比例：出发、行进、到达
    private const double BandFormed = 0.30;
    private const double BandLeaving = 0.78;

    private static readonly TimeSpan EnterDuration = TimeSpan.FromMilliseconds(450);
    private static readonly TimeSpan TravelDuration = TimeSpan.FromMilliseconds(700);
    private static readonly TimeSpan ExitDuration = TimeSpan.FromMilliseconds(450);

    private static readonly TimeSpan CycleDuration = EnterDuration + TravelDuration + ExitDuration;

    // 三段在周期里的占比。头部位置与亮度都从 Phase 按这三个占比推出来，所以时长改一处就够。
    private static readonly double EnterShare = EnterDuration.TotalMilliseconds / CycleDuration.TotalMilliseconds;
    private static readonly double TravelShare = TravelDuration.TotalMilliseconds / CycleDuration.TotalMilliseconds;

    private static readonly double ExitStart = EnterShare + TravelShare;

    /// <summary>Gets or sets the link rendered by this view.</summary>
    [Parameter]
    public IWorkflowLinkViewModel? Link { get; set; }

    /// <summary>Gets or sets the canvas size the link spans.</summary>
    [Parameter]
    public double CanvasWidth { get; set; } = 1920;

    /// <summary>Gets or sets the canvas height the link spans.</summary>
    [Parameter]
    public double CanvasHeight { get; set; } = 1080;

    /// <summary>Gets or sets an optional line-color override (defaults to <c>#CC38BDF8</c>).</summary>
    [Parameter]
    public string? LineColorOverride { get; set; }

    /// <summary>Gets or sets an optional thickness override (defaults to <c>2</c>).</summary>
    [Parameter]
    public string? ThicknessOverride { get; set; }

    /// <summary>Gets or sets an explicit virtual-link override (defaults to the sender/receiver-parent heuristic).</summary>
    [Parameter]
    public bool? IsVirtualOverride { get; set; }

    /// <summary>Gets or sets an explicit visibility override (defaults to <see cref="IWorkflowLinkViewModel.IsVisible"/>).</summary>
    [Parameter]
    public bool? CanRenderOverride { get; set; }

    /// <summary>Gets or sets whether the owning surface holds this link as its current selection.</summary>
    /// <remarks>
    /// The surface owns the selection because Delete and the right-click menu both act on it. This view
    /// keeps only a local hover flag for the highlight; the selection has to outlive the hover, or the
    /// highlight would drop the moment the pointer leaves the curve for the menu.
    /// </remarks>
    [Parameter]
    public bool IsSelected { get; set; }

    // 表面把自身级联下来，本视图据此把指针事件转发进它的连线交互枢纽；画在表面之外时为 null
    [CascadingParameter]
    private WorkflowSurfaceBehavior? Surface { get; set; }

    private INotifyPropertyChanged? _notifier;
    private INotifyPropertyChanged? _senderNotifier;
    private INotifyPropertyChanged? _receiverNotifier;

    private string LineColor => LineColorOverride ?? ToCss("#CC38BDF8");

    private double Thickness
    {
        get
        {
            if (ThicknessOverride is not null
                && double.TryParse(ThicknessOverride, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out var t))
            {
                return t;
            }

            return 2;
        }
    }

    /// <summary>
    /// Converts XAML-style <c>#AARRGGBB</c> color literals (as used by the template symbols)
    /// into CSS color values, so symbol-driven colors work in Razor views. Also passes
    /// through named colors and CSS <c>rgb()/rgba()</c> strings unchanged.
    /// </summary>
    private static string ToCss(string value)
    {
        var text = value.Trim();
        if (text.Length == 9 && text[0] == '#')
        {
            var alpha = text.Substring(1, 2);
            var rgb = text.Substring(3);
            if (byte.TryParse(alpha, NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture, out var a))
            {
                return FormattableString.Invariant(
                    $"rgba({HexByte(rgb, 0)},{HexByte(rgb, 2)},{HexByte(rgb, 4)},{a / 255d:0.###})");
            }
        }

        if (text.Length == 7 && text[0] == '#')
        {
            return text;
        }

        return text;
    }

    private static int HexByte(string hex, int offset)
        => Convert.ToInt32(hex.Substring(offset, 2), 16);

    private bool CanRender { get; set; } = true;
    private bool IsVirtual { get; set; }

    private bool EffectiveCanRender => CanRenderOverride ?? CanRender;
    private bool EffectiveIsVirtual => IsVirtualOverride ?? IsVirtual;

    // 数值一律走不变文化：Razor 按当前区域写裸 double，逗号小数点会让浏览器读不出这个属性
    private string CanvasWidthCss => N(CanvasWidth);
    private string CanvasHeightCss => N(CanvasHeight);
    private string ThicknessCss => N(Thickness);
    private double HaloOuterWidth => LitThickness + 9;
    private double HaloInnerWidth => LitThickness + 4;

    private static string N(double value) => value.ToString("0.####", CultureInfo.InvariantCulture);

    #region Flow effect

    /// <summary>
    /// The clock's phase, 0 to 1, repeating. Written by the transition, read by everything the comet
    /// draws — never set by a caller.
    /// </summary>
    private double Phase { get; set; }

    private Transition<TemplateLinkView>? _flow;
    private bool _flowRunning;
    private IWorkflowLinkViewModel? _flowLink;

    /// <summary>How far along the link the comet's head has travelled, as a fraction of its length.</summary>
    /// <remarks>
    /// Derived, not animated: <see cref="Phase"/> is the one animated value, and this is the piecewise
    /// map from the cycle onto the head's three legs (leave, travel, arrive). Two animations on one
    /// component would interrupt each other — <c>Transition.Exit</c> stops per target — so there is
    /// only ever one.
    /// </remarks>
    private double Head
    {
        get
        {
            if (Phase <= EnterShare)
            {
                return BandFormed * (Phase / EnterShare);
            }

            if (Phase <= ExitStart)
            {
                return BandFormed + ((BandLeaving - BandFormed) * ((Phase - EnterShare) / TravelShare));
            }

            return BandLeaving + ((1 - BandLeaving) * ((Phase - ExitStart) / EnterShare));
        }
    }

    /// <summary>How lit the comet is: 0 while it is absent, 1 while it travels.</summary>
    private double Intensity
    {
        get
        {
            if (Phase <= EnterShare)
            {
                return Phase / EnterShare;
            }

            if (Phase <= ExitStart)
            {
                return 1.0;
            }

            return 1.0 - ((Phase - ExitStart) / EnterShare);
        }
    }

    // 每组件构建：周期只写一个标量，几何、配色与弧长表都不参与。
    // 匀速（Eases.Default 就是恒等）—— 流水不该有缓动，头部的速度一变化就不像在流了。
    // LoopTime = int.MaxValue 是这套系统里唯一的「永远」，而时长不能是零：零时长的趟不占时间，
    // 一个永远循环于是空转，Exit 也就再也打断不了它。
    private Transition<TemplateLinkView> BuildFlow()
    {
        var effect = new TransitionEffect
        {
            Duration = CycleDuration,
            Ease = Eases.Default,
            LoopTime = int.MaxValue,
        };

        // 一相的效果：直线时长 + 本组件的重绘。这里没有可达渲染对象的画刷，只能逐帧交给渲染器。
        // 用 LateUpdate 而非 Update：它在当帧的写入落地后才触发，渲染的是刚写下的那帧。
        effect.LateUpdate += (_, _) => InvokeAsync(StateHasChanged);

        return Transition<TemplateLinkView>.Create()
            .Property(v => v.Phase, 1d)
            .Effect(effect);
    }

    // 已连接的链接从发送端起周期，虚拟链接停周期：橡皮筋上流动会宣称一条还不存在的连接
    // 换链接时重起——渲染就绪门让复用成为常态，池中视图常在首个链接测出前就被复用
    private void SyncFlow()
    {
        if (EffectiveIsVirtual || !EffectiveCanRender)
        {
            StopFlow();
            return;
        }

        if (_flowRunning && ReferenceEquals(_flowLink, Link)) return;

        _flowLink = Link;
        StartFlow();
    }

    // 从发送端起周期：视图被复用到另一条链接上时，动的是新那条
    private void StartFlow()
    {
        if (EffectiveIsVirtual || !EffectiveCanRender)
        {
            StopFlow();
            return;
        }

        _flow ??= BuildFlow();

        // 转换从目标读起始值，所以 Execute 前组件必须已经在周期起点；
        // 循环在每个接缝重放它，后续每轮都从它开始。
        Phase = 0;
        _flow.Execute(this);
        _flowRunning = true;
    }

    // 停周期：不再可绘、渲染已结束的视图，不能留着旧动画在跑
    private void StopFlow()
    {
        if (!_flowRunning) return;

        // 周期起在组件自身上，这里也是注销它的那次调用：此后没有帧能经由它到达渲染器
        Transition.Exit(this, IncludeMutual: true, IncludeNoMutual: true);
        _flowRunning = false;
        _flowLink = null;
    }

    #endregion

    #region Geometry

    // 画出来的几何与弧长都问 LinkCurve；本视图不再各存一份采样点、累计长度与取点实现。
    private LinkCurve? _curve;

    // 端点缓存：同一条链接端点没动就不重建曲线，也不重复发布。
    private IWorkflowLinkViewModel? _cacheLink;
    private double _cacheSx = double.NaN;
    private double _cacheSy;
    private double _cacheEx;
    private double _cacheEy;

    // 彗星的两遍（光晕在前、本体在后）拼成一条列表，Razor 只跑一次循环。
    // 够不着的段把不透明度压到零而不是删掉：元素数恒定，diff 才不会每帧建删元素。
    private readonly List<CometStop> _comet = new(TailSegments * 2);

    /// <summary>One drawn piece of the comet: a polyline run, its colour, its opacity and its width.</summary>
    private readonly record struct CometStop(string D, string Color, string Opacity, string Width);

    /// <summary>
    /// Refreshes the arc-length table (only when the endpoints moved) and rebuilds the comet for this
    /// frame's <see cref="Phase"/>. Returns false when the link cannot be drawn at all.
    /// </summary>
    private bool Prepare()
    {
        if (!TryEndpoints(out var sx, out var sy, out var ex, out var ey))
        {
            Link?.PublishCurve(null);
            return false;
        }

        // 换链接也要重建：池中视图可能被复用，端点凑巧相同不代表曲线属于新那条。
        if (!ReferenceEquals(_cacheLink, Link)
            || sx != _cacheSx || sy != _cacheSy || ex != _cacheEx || ey != _cacheEy)
        {
            _cacheLink = Link;
            _cacheSx = sx;
            _cacheSy = sy;
            _cacheEx = ex;
            _cacheEy = ey;
            _curve = LinkCurve.BuildCubic(sx, sy, ex, ey, PullMinimum, LinkCurve.DefaultSampleCount);
            Link?.PublishCurve(_curve, this);
        }

        BuildComet();
        return true;
    }

    /// <summary>
    /// Reads the two endpoints, honouring the render gates the XAML adapters use. Returns false when
    /// the link has no drawable geometry yet.
    /// </summary>
    /// <remarks>
    /// NaN gate: slot anchors default to NaN (unmeasured placeholder). Rendering before the GUI
    /// measures the endpoints would serialize NaN coordinates and paint a stale frame that jumps back
    /// once measurement lands — the first-entry flicker the XAML adapters guard against via
    /// WorkflowLinkRenderEx.IsRenderReady(). Skip until both non-virtual endpoints are measured.
    /// Placeholder endpoints (Parent is null, e.g. the VirtualLink gesture) are exempt.
    /// </remarks>
    private bool TryEndpoints(out double sx, out double sy, out double ex, out double ey)
    {
        sx = sy = ex = ey = 0;

        var link = Link;
        if (link?.Sender is null || link.Receiver is null) return false;
        if (!WorkflowSlotUpdateGate.IsLinkRenderReady(link)) return false;

        sx = link.Sender.Anchor.Horizontal;
        sy = link.Sender.Anchor.Vertical;
        ex = link.Receiver.Anchor.Horizontal;
        ey = link.Receiver.Anchor.Vertical;

        // Placeholder endpoints (VirtualLink gesture) can still carry NaN anchors on the reset
        // intermediate frames, which would serialize a "NaN,NaN" path. Suppress until real.
        return !double.IsNaN(sx) && !double.IsNaN(sy) && !double.IsNaN(ex) && !double.IsNaN(ey);
    }

    // 取 [from, to] 这一段弧长上的折线。两端各自插值到精确位置，中间用现成采样点。
    // 返回 SVG 的 path d —— 一段拖尾就是一个独立的元素，才能各带各的颜色与不透明度
    private string SegmentPath(double from, double to)
    {
        var curve = _curve;
        if (curve is null) return "";

        to = Math.Min(to, curve.Length);
        from = Math.Clamp(from, 0, curve.Length);
        if (to <= from) return "";

        var sb = new StringBuilder();
        var start = curve.PointAtLength(from);
        sb.Append('M').Append(N(start.X)).Append(',').Append(N(start.Y));

        for (int i = 0; i < curve.Count; i++)
        {
            double l = curve.LengthAt(i);
            if (l <= from || l >= to) continue;
            sb.Append('L').Append(N(curve.XAt(i))).Append(',').Append(N(curve.YAt(i)));
        }

        var end = curve.PointAtLength(to);
        sb.Append('L').Append(N(end.X)).Append(',').Append(N(end.Y));
        return sb.ToString();
    }

    /// <summary>The full resting curve, as one path.</summary>
    private string BodyPath { get; set; } = "";

    /// <summary>
    /// The comet: the arc-length window [head − tail, head] cut into <see cref="TailSegments"/> pieces,
    /// drawn twice — the halo pass first (wider, fainter, same segments) and the body pass after it.
    /// </summary>
    private void BuildComet()
    {
        _comet.Clear();
        var curve = _curve;
        double length = curve?.Length ?? 0;
        BodyPath = length > 0 ? SegmentPath(0, length) : "";

        if (curve is null || length <= 0 || EffectiveIsVirtual)
        {
            return;
        }

        double intensity = Intensity;
        double head = Math.Clamp(Head, 0, 1) * length;
        double tail = TailFraction * length;

        // 复用同一批颜色，别每段都解析一次。选中时彗星跟线体一起换色，否则高亮的线上会套一层原色光
        var body = ParseColor(IsLit ? SelectedColor : (LineColorOverride ?? "#CC38BDF8"));

        // 光晕一遍在前
        for (int k = 0; k < TailSegments; k++)
        {
            double f0 = k / (double)TailSegments;      // 0 = 尾梢，1 = 头
            double f1 = (k + 1) / (double)TailSegments;

            double l0 = head - (tail * (1 - f0));
            double l1 = head - (tail * (1 - f1));

            // 平方衰减：让透明集中在尾段，读起来才像拖尾而不是一条均匀的带
            double a = intensity * f0 * f0;
            if (l1 <= 0 || l0 >= length || a <= 0.004)
            {
                _comet.Add(new CometStop("", LineColor, "0", N(HaloOuterWidth)));
                continue;
            }

            // 尾梢是本体的颜色，越靠近头越白 —— 白热只发生在头部
            _comet.Add(new CometStop(
                SegmentPath(l0, l1),
                MixToWhite(body, f0),
                N(a * 0.22),
                N(HaloOuterWidth)));
        }

        // 本体一遍在后
        for (int k = 0; k < TailSegments; k++)
        {
            double f0 = k / (double)TailSegments;
            double f1 = (k + 1) / (double)TailSegments;

            double l0 = head - (tail * (1 - f0));
            double l1 = head - (tail * (1 - f1));

            double a = intensity * f0 * f0;
            if (l1 <= 0 || l0 >= length || a <= 0.004)
            {
                _comet.Add(new CometStop("", LineColor, "0", ThicknessCss));
                continue;
            }

            _comet.Add(new CometStop(
                SegmentPath(l0, l1),
                MixToWhite(body, f0),
                N(a),
                N(Thickness * (0.45 + (0.95 * f0)))));
        }
    }

    // 把模板符号带的 XAML 色值解析成四通道
    // 读不了的（如 CSS 颜色名）退回模板自己的 #CC38BDF8：流程要通道值才能混色，混不了就画不出彗星
    private static (int A, int R, int G, int B) ParseColor(string value)
    {
        var text = value.Trim();
        if (text.StartsWith('#'))
        {
            var hex = text[1..];
            if (hex.Length == 8
                && int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var argb))
            {
                return ((argb >> 24) & 0xFF, (argb >> 16) & 0xFF, (argb >> 8) & 0xFF, argb & 0xFF);
            }

            if (hex.Length == 6
                && int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
            {
                return (0xFF, (rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
            }
        }

        return (0xDD, 0xFF, 0xFF, 0xFF);
    }

    // 尾梢（f0 = 0）是本体的颜色，头（f0 = 1）是白：白热只发生在头部
    private static string MixToWhite((int A, int R, int G, int B) from, double t)
    {
        byte L(int channel) => (byte)Math.Round(channel + ((255 - channel) * t));

        return string.Create(CultureInfo.InvariantCulture,
            $"rgb({L(from.R)},{L(from.G)},{L(from.B)})");
    }

    #endregion

    #region Interaction

    // 选中色与增量沿用另外六家：柔光（浅青）而不是刺目的红，线宽 +1.5，线体不透明度 0.55 → 0.85。
    // 写成 ARGB 是为了让 ToCss 与 ParseColor 都能读同一个常量 —— 管壁与彗星跟线体同源
    private const string SelectedColor = "#FFFFFFFF";
    private const double SelectedWidthBonus = 1.5;

    private bool _hover;

    /// <summary>
    /// Whether the pointer is on this link. The tree's <see cref="LinkInteraction"/> hub drives it
    /// through <see cref="ILinkHighlight"/> as it resolves the hovered link.
    /// </summary>
    public bool IsHighlighted
    {
        get => _hover;
        set
        {
            if (_hover == value) return;
            _hover = value;
            _ = InvokeAsync(StateHasChanged);
        }
    }

    // 亮起来的两个理由：指针在线上，或页面把这条线选住了（右键菜单开着时指针已经不在线上）
    private bool IsLit => _hover || IsSelected;

    private string LitColor => IsLit ? ToCss(SelectedColor) : LineColor;
    private double LitThickness => IsLit ? Thickness + SelectedWidthBonus : Thickness;
    private string LitThicknessCss => N(LitThickness);
    private string BodyOpacity => IsLit ? "0.85" : "0.55";

    // 命中交回描边：只有真正画出来的那圈参与命中（最宽的是外层管壁），距离判定由浏览器做。
    // 虚拟连线整层不参与 —— 它是指针下的橡皮筋，命中了就会抢掉正在拖它的那次手势
    private string HitTargetCss => EffectiveIsVirtual ? "none" : "stroke";

    // 悬停高亮：本视图进入即先亮（这张脸是逐元素的，浏览器进出即可，不必等枢纽走一圈），
    // 枢纽随后按命中把 IsHighlighted 落在真正在最上的那条上 —— 本视图转发的只是位置，
    // 哪条线在最上由枢纽裁决，它不替枢纽下结论。
    private async Task OnPointerEnter(MouseEventArgs e)
    {
        _hover = true;
        await InvokeAsync(StateHasChanged);
        if (Surface is not null)
        {
            await Surface.ForwardPointerAsync(PointerPhase.Entered, e.ClientX, e.ClientY);
        }
    }

    private async Task OnPointerExit(MouseEventArgs e)
    {
        _hover = false;
        await InvokeAsync(StateHasChanged);
        if (Surface is not null)
        {
            await Surface.ForwardPointerAsync(PointerPhase.Exited, e.ClientX, e.ClientY);
        }
    }

    #endregion

    /// <inheritdoc />
    protected override void OnInitialized()
    {
        Sync(Link);
        SyncFlow();
    }

    private void Sync(IWorkflowLinkViewModel? link)
    {
        if (link is null) return;

        CanRender = link.IsVisible;
        IsVirtual = IsVirtualLink(link);

        if (link is INotifyPropertyChanged n)
        {
            _notifier = n;
            n.PropertyChanged += OnLinkChanged;
        }

        if (link.Sender is INotifyPropertyChanged s)
        {
            _senderNotifier = s;
            s.PropertyChanged += OnEndpointChanged;
        }

        if (link.Receiver is INotifyPropertyChanged r)
        {
            _receiverNotifier = r;
            r.PropertyChanged += OnEndpointChanged;
        }
    }

    private void OnLinkChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IWorkflowLinkViewModel.IsVisible) or null or "")
        {
            CanRender = Link?.IsVisible == true;
        }

        if (e.PropertyName is nameof(IWorkflowLinkViewModel.Sender)
            or nameof(IWorkflowLinkViewModel.Receiver)
            or null or "")
        {
            IsVirtual = IsVirtualLink(Link);
        }

        SyncFlow();
        InvokeAsync(StateHasChanged);
    }

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        base.OnParametersSet();
        SyncFlow();
        if (IsVirtualOverride is not null || CanRenderOverride is not null)
        {
            InvokeAsync(StateHasChanged);
        }
    }

    private void OnEndpointChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IWorkflowSlotViewModel.Anchor) or null or "")
        {
            InvokeAsync(StateHasChanged);
        }
    }

    private bool IsVirtualLink(IWorkflowLinkViewModel? link)
        => link is null || (link.Sender?.Parent is null && link.Receiver?.Parent is null);

    /// <inheritdoc />
    public void Dispose()
    {
        // 视图走了就不再画这条线，撤掉发布出去的曲线，免得它继续替一条不存在的线回答命中
        Link?.PublishCurve(null);

        // 已释放的视图不能继续流动：那会一直向已经走掉的渲染器发 StateHasChanged
        StopFlow();

        if (_notifier is not null)
        {
            _notifier.PropertyChanged -= OnLinkChanged;
            _notifier = null;
        }

        if (_senderNotifier is not null)
        {
            _senderNotifier.PropertyChanged -= OnEndpointChanged;
            _senderNotifier = null;
        }

        if (_receiverNotifier is not null)
        {
            _receiverNotifier.PropertyChanged -= OnEndpointChanged;
            _receiverNotifier = null;
        }
    }
}
