using System.ComponentModel;
using System.Globalization;
using Microsoft.AspNetCore.Components;
using VeloxDev.TransitionSystem;
using VeloxDev.WorkflowSystem;

namespace Demo.Components.Workflow;

/// <summary>
/// A Blazor workflow link view rendered as an orthogonal polyline with golden-ratio
/// stubs, mirroring the WPF template's geometry. Points derive from the endpoint slot
/// anchors; the polyline spans the whole canvas so links are absolutely positioned
/// (overflow visible) and redraw whenever the endpoints move.
/// <para>
/// The view is also the object the flow animates — <c>Transition&lt;TemplateLinkView&gt;</c>. A Razor component
/// is a class, so the band's three stop offsets and its colour are this component's own members and the markup
/// reads the cycle's position straight off it, with nothing in between to map back into stops.
/// </para>
/// </summary>
public partial class TemplateLinkView : ComponentBase, IDisposable
{
    private const double Phi = 0.6180339887;

    /// <summary>Gets or sets the link rendered by this view.</summary>
    [Parameter]
    public IWorkflowLinkViewModel? Link { get; set; }

    /// <summary>Gets or sets the canvas size the link spans.</summary>
    [Parameter]
    public double CanvasWidth { get; set; } = 1920;

    /// <summary>Gets or sets the canvas height the link spans.</summary>
    [Parameter]
    public double CanvasHeight { get; set; } = 1080;

    /// <summary>Gets or sets an optional line-color override (defaults to <c>#DDFFFFFF</c>).</summary>
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

    private INotifyPropertyChanged? _notifier;
    private INotifyPropertyChanged? _senderNotifier;
    private INotifyPropertyChanged? _receiverNotifier;

    private string LineColor => LineColorOverride ?? ToCss("#DDFFFFFF");
    private double Thickness
    {
        get
        {
            if (ThicknessOverride is not null
                && double.TryParse(ThicknessOverride, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var t))
            {
                return t;
            }

            return double.Parse("2", System.Globalization.CultureInfo.InvariantCulture);
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
            if (byte.TryParse(alpha, System.Globalization.NumberStyles.HexNumber,
                    System.Globalization.CultureInfo.InvariantCulture, out var a))
            {
                return $"rgba({HexByte(rgb, 0)},{HexByte(rgb, 2)},{HexByte(rgb, 4)},{a / 255d:0.###})";
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

    private string CanvasWidthCss => CanvasWidth.ToString("0.#");
    private string CanvasHeightCss => CanvasHeight.ToString("0.#");
    private string ThicknessCss => Thickness.ToString("0.#");
    private string MarkerSuffix => Link?.GetHashCode().ToString("X8") ?? "virtual";

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

    // 光带的三个停靠点偏移（沿链接的渐变单位）与颜色：全部动画状态；本组件即动画对象，路径直接读视图，中间没有标量要映射回停靠点
    // 偏移由 N 不变文化格式化：Razor 用当前区域写裸 double，逗号小数点会写出浏览器读不了的 SVG 属性
    private double BandTail { get; set; }
    private double BandCentre { get; set; }
    private double BandLead { get; set; }

    // 光带颜色（CSS 串）：周期写它，中间那个 stop 读它
    private string BandColor { get; set; } = "rgba(0,0,0,0)";

    private Transition<TemplateLinkView>? _flow;
    private bool _flowRunning;
    private IWorkflowLinkViewModel? _flowLink;

    // 流动两色按通道存而非标记串（要能混色）：只在参数变化时解析，不是每帧
    private (int A, int R, int G, int B) _dimColor = (0x9E, 0xFF, 0xFF, 0xFF);
    private (int A, int R, int G, int B) _litColor = (0xFF, 0xFF, 0xFF, 0xFF);

    // 满亮度的光带色（CSS）：箭头也用它
    private string LitCss => Css(_litColor);

    // 线体静息色（CSS）：光带两侧的肩，周期从不写它
    private string DimCss => Css(_dimColor);

    // 本链接的渐变 id：一条链接一份定义，描边按 id 引用
    private string FlowId => $"veloxdev-flow-{MarkerSuffix}";

    // 已连接的链接描边就是这段渐变；虚拟链接保留原来的虚线平色
    private string FlowStroke => EffectiveIsVirtual ? LineColor : $"url(#{FlowId})";

    // 每组件构建：两个端点取自该链接自己的颜色，静态声明会把读到的那份值共享给之后每次执行
    // 路径直达组件自身：三个偏移与颜色是具名写入，相位结构看得见而非算出来；匀速所以不用缓动
    private Transition<TemplateLinkView> BuildFlow() => Transition<TemplateLinkView>.Create()
        // 相位一：一边成形一边进入（走三分之一路程，同时由静息色变亮）
        .Property(v => v.BandTail, BandFormed - BandHalfWidth)
        .Property(v => v.BandCentre, BandFormed)
        .Property(v => v.BandLead, BandFormed + BandHalfWidth)
        .Property(v => v.BandColor, LitCss)
        .Effect(Repainting(EnterDuration))
        .Then()
        // 相位二：保持全亮只移动——这一段读起来才是流动而非脉冲
        .Property(v => v.BandTail, BandLeaving - BandHalfWidth)
        .Property(v => v.BandCentre, BandLeaving)
        .Property(v => v.BandLead, BandLeaving + BandHalfWidth)
        .Effect(Repainting(TravelDuration))
        .Then()
        // 相位三：一边退回静息色一边离开；周期两端都是均匀暗色，循环接缝才看不出来
        .Property(v => v.BandTail, BandExit - BandHalfWidth)
        .Property(v => v.BandCentre, BandExit)
        .Property(v => v.BandLead, BandExit + BandHalfWidth)
        .Property(v => v.BandColor, DimCss)
        .Effect(Repainting(ExitDuration))
        .Repeat(int.MaxValue);

    // 一相的效果：直线时长 + 本组件的重绘；这里没有可达渲染对象的画刷，只能逐帧交给渲染器
    // 用 LateUpdate 而非 Update：它在当帧的写入落地后才触发，渲染的是刚写下的那帧（重放时每周期都触发）
    private TransitionEffect Repainting(TimeSpan duration)
    {
        var effect = new TransitionEffect()
        {
            Duration = duration,
            Ease = Eases.Default,
        };

        effect.LateUpdate += (_, _) => InvokeAsync(StateHasChanged);
        return effect;
    }

    // 渐变轴（userSpaceOnUse）：取链接自身两端而非包围盒，光带才沿链接走而不是横扫盒子的对角线
    // 每次渲染从锚点重算，也不反过来写：偏移归周期所有（见 AimFlow）
    private string[] FlowAxis => Link?.Sender is { } sender && Link.Receiver is { } receiver
        ?
        [
            N(sender.Anchor.Horizontal), N(sender.Anchor.Vertical),
            N(receiver.Anchor.Horizontal), N(receiver.Anchor.Vertical),
        ]
        : ["0", "0", "0", "0"];

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

        AimFlow();

        // 转换从目标读起始值，Execute 前组件必须已在周期起点；循环在每个接缝重放它，后续每轮都从它开始
        BandTail = BandStart - BandHalfWidth;
        BandCentre = BandStart;
        BandLead = BandStart + BandHalfWidth;
        BandColor = DimCss;

        _flow!.Execute(this);
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

    // 把模板符号带的 XAML 色值解析成四通道
    // 读不了的（如 CSS 颜色名）退回模板自己的 #DDFFFFFF：流程要通道值才能混色，混不了就画不出光带
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

    // 按链接自己的颜色算出流动两色，并重建以它们为端点的声明：换链接、换参数、换颜色时调用
    // 这里不写光带位置：那些停靠点归周期所有，拖拽中每帧都改锚点，抢写会让光带抖动
    private void AimFlow()
    {
        var lit = LitOf(ParseColor(LineColorOverride ?? "#DDFFFFFF"));
        if (_flow is not null && lit == _litColor)
        {
            return;
        }

        _litColor = lit;
        _dimColor = DimOf(lit);
        _flow = BuildFlow();

        // 视图被复用到另一种颜色的链接上时，按自己的颜色重新起周期
        if (_flowRunning)
        {
            StartFlow();
        }
    }

    // 亮色：各通道向白抬 45%（白链接也留出更亮处）
    private static (int A, int R, int G, int B) LitOf((int A, int R, int G, int B) color)
    {
        const double lift = 0.45;

        int Up(int channel) => (int)Math.Round(channel + (255 - channel) * lift);

        return (0xFF, Up(color.R), Up(color.G), Up(color.B));
    }

    // 靠 alpha 变暗取反差，色相不变；往白里提在青线（本 demo）和白线上都几乎看不出（实测过）
    private static (int A, int R, int G, int B) DimOf((int A, int R, int G, int B) color)
        => ((int)Math.Round(color.A * 0.62), color.R, color.G, color.B);

    // 颜色写成 CSS 串；alpha 与偏移同理必须不变文化——它是周期的一个端点，逗号小数点会让混色的采样器读不出它
    private static string Css((int A, int R, int G, int B) color) => color.A >= 0xFF
        ? $"rgb({color.R},{color.G},{color.B})"
        : string.Create(CultureInfo.InvariantCulture,
            $"rgba({color.R},{color.G},{color.B},{color.A / 255d:0.###})");

    private static string N(double value) => value.ToString("0.####", CultureInfo.InvariantCulture);

    #endregion

    /// <inheritdoc />
    protected override void OnInitialized()
    {
        Sync(Link);

        // 声明的端点是链接自己的颜色，所以颜色在前：周期由它构建，不起周期的虚拟链接也用它画箭头
        AimFlow();
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
        AimFlow();
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

    private string BuildPoints()
    {
        var link = Link;
        if (link is null) return "";

        var sender = link.Sender;
        var receiver = link.Receiver;
        if (sender is null || receiver is null) return "";

        // NaN gate: slot anchors default to NaN (unmeasured placeholder). Rendering before
        // the GUI measures the endpoints would serialize NaN coordinates and paint a stale
        // frame that jumps back once measurement lands — the first-entry flicker the XAML
        // adapters guard against via WorkflowLinkRenderEx.IsRenderReady(). Skip until both
        // non-virtual endpoints are measured. Placeholder endpoints (Parent is null, e.g. the
        // VirtualLink gesture) are exempt and render immediately.
        if (!WorkflowSlotUpdateGate.IsLinkRenderReady(link)) return "";

        double sx = sender.Anchor.Horizontal;
        double sy = sender.Anchor.Vertical;
        double ex = receiver.Anchor.Horizontal;
        double ey = receiver.Anchor.Vertical;

        // Placeholder endpoints (VirtualLink gesture) can still carry NaN anchors on the
        // reset intermediate frames (Reset nulls the anchors before clearing IsVisible), which
        // would serialize a "NaN,NaN" polyline. Suppress until the coordinates are real.
        if (double.IsNaN(sx) || double.IsNaN(sy) || double.IsNaN(ex) || double.IsNaN(ey)) return "";

        double dx = ex - sx;
        // Signed stub keeps the orthogonal bend on the correct side when dragging leftward.
        double stub = dx / 2.0 * (1.0 - Phi);
        double p1x = sx + stub;
        double p4x = ex - stub;

        return $"{sx:F1},{sy:F1} {p1x:F1},{sy:F1} {p4x:F1},{ey:F1} {ex:F1},{ey:F1}";
    }

    /// <inheritdoc />
    public void Dispose()
    {
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
