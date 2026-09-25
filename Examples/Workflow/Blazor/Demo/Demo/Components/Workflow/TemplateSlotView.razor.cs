using System.ComponentModel;
using System.Globalization;
using Microsoft.AspNetCore.Components;
using VeloxDev.TransitionSystem;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace Demo.Components.Workflow;

/// <summary>
/// The port on a node: a fine ring with a small core, and a ripple that says which way data goes.
/// <para>
/// The direction is carried by the <b>shape of the motion</b> rather than by a colour or a rotating
/// token: the ripple spreads <b>outward</b> when the slot can send, gathers <b>inward</b> when it can
/// receive, and does both at once when it can do either. That comes from <see cref="SlotChannel"/> —
/// the slot's declared capacity — and not from what it happens to be connected to, so a port states
/// what it is for even with nothing wired to it.
/// </para>
/// <para>
/// One clock drives everything. <c>Transition.Exit</c> stops by target, so two independent loops on one
/// component would interrupt each other; there is therefore exactly one transition here, on one
/// <see cref="Phase"/>, and every radius and every opacity the port draws is derived from it. The
/// aim-breath is derived from the same phase instead of being a loop of its own.
/// </para>
/// <para>
/// The glyph is SVG and the numbers are formatted through <see cref="N"/>: Razor writes a bare
/// <c>double</c> in the current culture, and a comma decimal separator produces an attribute the
/// browser silently drops. Every length here reaches the DOM as a string, so every one of them goes
/// through the invariant formatter.
/// </para>
/// </summary>
public partial class TemplateSlotView : ComponentBase, IDisposable
{
    // 字形按插槽尺寸等比缩放，但以 32 设计单位为上限 —— 端口在卡里只有二十几单位，
    // 再大就不成其为「点」了。0.205 / 0.045 / 0.06 都比原来那一整块填充字形细一半以上。
    private const double GlyphReference = 32;
    private const double RingRadiusRatio = 0.205;
    private const double RingThicknessRatio = 0.045;
    private const double CoreRadiusRatio = 0.06;

    // 波纹的终点半径就是环本身（1.0×），外缘留 1 单位不碰边界。
    // 从前内圈取 1.25×，内聚波纹于是停在环外一截再凭空消失 —— 看着像没走到就被掐了。
    private const double RippleInnerRatio = 1.0;
    private const double RippleThicknessRatio = 0.05;

    // 外散的出现段：用 12% 的路程升起来，不凭空冒出。内聚不走这条 —— 它从外缘的淡色开始，
    // 本来就不需要「出现」。
    private const double RippleAttack = 0.12;

    // 内聚的两端透明度：外缘淡、环上实。它一路加深到底，不做收尾淡出。
    private const double ReceiveOuterAlpha = 0.15;
    private const double ReceiveInnerAlpha = 0.90;

    // 一圈上只画一道波纹。两道虽然更「连续」，但同屏永远有两只环挂在固定的内外半径之间，
    // 读起来像一个静止的靶心 —— 眼睛抓不住单个环，方向也就读不出来了。一道走完再起一道，
    // 周期之间那点空档反而让「一波一波」这件事看得更清楚。
    private static readonly TimeSpan SweepPeriod = TimeSpan.FromMilliseconds(2300);

    /// <summary>Gets or sets the slot rendered by this view.</summary>
    [Parameter]
    public IWorkflowSlotViewModel? Slot { get; set; }

    /// <summary>Gets or sets the workflow tree that owns connection gestures.</summary>
    [Parameter]
    public IWorkflowTreeViewModel? Tree { get; set; }

    /// <summary>Gets or sets extra styles for the connection-behavior wrapper.</summary>
    [Parameter]
    public string? Style { get; set; }

    /// <summary>
    /// Gets or sets the rendered size in design pixels. The card is authored at its design size and
    /// scaled into the viewport, so this is a design length and not a screen length: 32 for a port on
    /// the card's edge, 24 for one inside a port list.
    /// </summary>
    [Parameter]
    public double SlotSize { get; set; } = 32;

    private INotifyPropertyChanged? _notifier;
    private bool _hover;

    #region The clock

    /// <summary>
    /// The clock's phase, 0 to 1, repeating. Written by the transition, read by everything else —
    /// never set by a caller.
    /// </summary>
    private double Phase { get; set; }

    private Transition<TemplateSlotView>? _spin;
    private bool _running;

    // 每组件构建：声明只写一个标量，几何与配色都不参与。
    // LoopTime = int.MaxValue 是这套系统里唯一的「永远」，而时长不能是零：零时长的趟不占时间，
    // 一个永远循环于是空转，Exit 也就再也打断不了它。
    private Transition<TemplateSlotView> BuildSpin()
    {
        var effect = new TransitionEffect
        {
            Duration = SweepPeriod,
            Ease = Eases.Default,
            LoopTime = int.MaxValue,
        };

        // 一个组件上只跑一条：LateUpdate 在当帧的写入落地后才触发，渲染的是刚写下的那帧。
        // 这里没有可达渲染对象的画刷，只能逐帧交给渲染器。
        effect.LateUpdate += (_, _) => InvokeAsync(StateHasChanged);

        return Transition<TemplateSlotView>.Create()
            .Property(v => v.Phase, 1d)
            .Effect(effect);
    }

    // 只要端口声明了任一方向，波纹就该跑；没有方向的端口是一条死线，挂上去只是白烧 CPU。
    private void SyncSpin()
    {
        bool ripples = HasDirection;

        if (ripples && !_running)
        {
            _spin ??= BuildSpin();

            // 转换从目标读起始值，所以 Execute 前组件必须已经在周期起点；
            // 循环在每个接缝重放它，后续每轮都从它开始。
            Phase = 0;
            _spin.Execute(this);
            _running = true;
        }
        else if (!ripples && _running)
        {
            StopSpin();
        }
    }

    // 停周期：不再声明方向的端口不能留着旧动画在跑
    private void StopSpin()
    {
        if (!_running) return;

        Transition.Exit(this, IncludeMutual: true, IncludeNoMutual: true);
        _running = false;
    }

    #endregion

    #region Slot state

    /// <summary>Whether the slot may connect outward — a target direction is declared.</summary>
    private bool CanSend => Slot is not null
        && (Slot.Channel.HasFlag(SlotChannel.OneTarget) || Slot.Channel.HasFlag(SlotChannel.MultipleTargets));

    /// <summary>Whether the slot may be connected inward — a source direction is declared.</summary>
    private bool CanReceive => Slot is not null
        && (Slot.Channel.HasFlag(SlotChannel.OneSource) || Slot.Channel.HasFlag(SlotChannel.MultipleSources));

    private bool HasDirection => CanSend || CanReceive;

    private SlotState State => Slot?.State ?? SlotState.StandBy;

    private bool IsAiming =>
        State.HasFlag(SlotState.PreviewSender) || State.HasFlag(SlotState.PreviewReceiver);

    private bool IsConnected =>
        State.HasFlag(SlotState.Sender) || State.HasFlag(SlotState.Receiver);

    #endregion

    #region Geometry

    // 字形按控件尺寸等比缩放，但以 32 设计单位为上限
    private double GlyphSize => Math.Min(SlotSize, GlyphReference);
    private double Centre => SlotSize / 2;

    private double RingRadius => GlyphSize * RingRadiusRatio;
    private double RingThickness => GlyphSize * RingThicknessRatio;

    // 波纹的终点半径就是环本身（1.0×），外缘留 1 单位不碰边界。
    private double RippleInner => RingRadius * RippleInnerRatio;
    private double RippleOuter => (GlyphSize / 2) - 1;
    private double RippleThickness => Math.Max(0.8, GlyphSize * RippleThicknessRatio);

    /// <summary>Draw a ripple at all: a direction is declared and there is room for the sweep.</summary>
    private bool HasRipple => HasDirection && RippleOuter > RippleInner;

    // 静息时压暗，被瞄准或已连上时全亮；指针搭在上面再亮一档 —— 端口小，没有反馈就不知道有没有搭上
    private double Lit =>
        (IsAiming || IsConnected ? 0.75 : 0.45)
        + (0.25 * Breath)
        + (_hover ? 0.2 : 0);

    // 呼吸从这个时钟推导：`sin` 的相位就是 Phase，所以它和波纹天然同步，也不需要第二条动画
    private double Breath => IsAiming ? 0.5 + (0.5 * Math.Sin(Phase * Math.PI * 2)) : 0;

    private double RingOpacity => Math.Clamp(Lit, 0, 1);
    private double CoreOpacity => Math.Clamp(Lit + 0.2, 0, 1);
    private double CoreRadius => GlyphSize * CoreRadiusRatio * ((IsAiming ? 1.2 : 1.0) + (0.3 * Breath));

    // 外散：在环上冒出来（12% 的路程升到全亮），越往外越淡 —— 能量在离开
    private double SendRadius => RippleInner + ((RippleOuter - RippleInner) * Phase);
    private double SendOpacity => Math.Clamp(RippleStrength * Math.Min(1, Phase / RippleAttack) * (1 - Phase) * 0.7, 0, 1);

    // 内聚：外缘淡、一路加深，收在环上 —— 能量在落地。不做收尾淡出，因为终点与环同半径：
    // 最后那一下是并进环里，从「最深」跳回「外缘的淡」读起来是新的一波从外面过来
    private double ReceiveRadius => RippleOuter - ((RippleOuter - RippleInner) * Phase);
    private double ReceiveOpacity => Math.Clamp(RippleStrength * (ReceiveOuterAlpha + ((ReceiveInnerAlpha - ReceiveOuterAlpha) * Phase)), 0, 1);

    private double RippleStrength => IsAiming || IsConnected ? 1.0 : 0.6;

    #endregion

    #region Css

    private string SizeCss => N(SlotSize);
    private string CentreCss => N(Centre);
    private string RingRadiusCss => N(RingRadius);
    private string RingThicknessCss => N(RingThickness);
    private string CoreRadiusCss => N(CoreRadius);
    private string RippleThicknessCss => N(RippleThickness);
    private string RingOpacityCss => N(RingOpacity);
    private string CoreOpacityCss => N(CoreOpacity);
    private string SendRadiusCss => N(SendRadius);
    private string SendOpacityCss => N(SendOpacity);
    private string ReceiveRadiusCss => N(ReceiveRadius);
    private string ReceiveOpacityCss => N(ReceiveOpacity);

    /// <summary>The ring's colour: the slot's own state, opaque. Alpha is a separate attribute.</summary>
    private string ColorCss => StateColor(State);

    /// <summary>
    /// The WPF template contract, kept: Sender + Receiver → Violet, Sender → Tomato, Receiver → Lime.
    /// Standby is white rather than the template's near-black <c>#DD1E1E1E</c>, because the port now
    /// sits on a dark card where a near-black glyph is invisible — the Avalonia card made the same
    /// call. The alpha that the old value carried lives in the opacity attributes now.
    /// </summary>
    private static string StateColor(SlotState state)
    {
        if (state.HasFlag(SlotState.Sender) && state.HasFlag(SlotState.Receiver))
        {
            return "#EE82EE";
        }

        if (state.HasFlag(SlotState.Sender))
        {
            return "#FF6347";
        }

        if (state.HasFlag(SlotState.Receiver))
        {
            return "#32CD32";
        }

        return "#FFFFFF";
    }

    // Razor 用当前区域写裸 double，逗号小数点会写出浏览器读不了的 SVG 属性
    private static string N(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    #endregion

    private async Task OnPointerEnter()
    {
        _hover = true;
        await InvokeAsync(StateHasChanged);
    }

    private async Task OnPointerExit()
    {
        _hover = false;
        await InvokeAsync(StateHasChanged);
    }

    /// <inheritdoc />
    protected override void OnInitialized()
    {
        Sync(Slot);
        SyncSpin();
    }

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        base.OnParametersSet();

        // 视图池会把一个端口复用到另一个插槽上；换了插槽，波纹的方向与颜色都得跟着换
        if (!ReferenceEquals(_subscribed, Slot))
        {
            Sync(Slot);
            SyncSpin();
        }
    }

    private IWorkflowSlotViewModel? _subscribed;

    private void Sync(IWorkflowSlotViewModel? slot)
    {
        if (_notifier is not null)
        {
            _notifier.PropertyChanged -= OnSlotChanged;
            _notifier = null;
        }

        _subscribed = slot;

        if (slot is INotifyPropertyChanged n)
        {
            _notifier = n;
            n.PropertyChanged += OnSlotChanged;
        }
    }

    private void OnSlotChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Channel 决定波纹的方向，State 决定颜色与亮度；两者都是重绘的理由。
        if (e.PropertyName is nameof(IWorkflowSlotViewModel.Channel))
        {
            SyncSpin();
        }

        if (e.PropertyName is nameof(IWorkflowSlotViewModel.State)
            or nameof(IWorkflowSlotViewModel.Channel) or null or "")
        {
            InvokeAsync(StateHasChanged);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        // 已释放的组件不能继续流动：那会一直向已经走掉的渲染器发 StateHasChanged
        StopSpin();

        if (_notifier is not null)
        {
            _notifier.PropertyChanged -= OnSlotChanged;
            _notifier = null;
        }

        _subscribed = null;
    }
}
