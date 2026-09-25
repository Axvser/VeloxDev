using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using System;
using VeloxDev.TransitionSystem;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace Demo;

/// <summary>
/// The port on a node: a fine ring with a small core, and ripples that say which way data goes.
/// <para>
/// The direction is carried by the <b>shape of the motion</b> rather than by a colour or a rotating token:
/// ripples spread <b>outward</b> when the slot can send, <b>inward</b> when it can receive, and both ways at
/// once when it can do either. That comes from <see cref="Channel"/> — the slot's declared capacity — and not
/// from what it happens to be connected to, so a port states what it is for even with nothing wired to it.
/// </para>
/// <para>
/// One clock drives everything. <see cref="Transition"/>'s exit is per-target, so two independent loops on one
/// control would stop each other; the aim-breath is therefore <i>derived</i> from the same <see cref="Sweep"/>
/// that moves the ripples rather than being a loop of its own.
/// </para>
/// <para>
/// The type keeps the name <c>SlotView</c> because the node views bind and style it by that name. It is also
/// why the connection behaviour is switched on here rather than in each card: every card used to write
/// <c>behaviors:WorkflowSlotConnectionBehavior.IsEnabled="True"</c> on its slot, and dropping that attribute —
/// along with the near-transparent <c>Background</c> that gave the glyph a hit-test surface — silently cost
/// the ports their pointer input. Both live here now so a card cannot forget them.
/// </para>
/// </summary>
public class SlotView : UserControl
{
    // 字形按控件尺寸等比缩放，但以 32 设计单位为上限 —— 端口在卡里只有二十几单位，
    // 再大就不成其为「点」了。这三个比例都比原来细一半以上：0.205 / 0.045 / 0.06
    private const double GlyphReference = 32;
    private const double RingRadiusRatio = 0.205;
    private const double RingThicknessRatio = 0.045;
    private const double CoreRadiusRatio = 0.06;

    // 波纹的终点半径就是环本身（1.0×），外缘留 1 单位不碰边界。
    // 从前内圈取 1.25×，内聚波纹于是停在环外一截再凭空消失 —— 看着像没走到就被掐了
    private const double RippleInnerRatio = 1.0;
    private const double RippleThicknessRatio = 0.05;

    // 外散的出现段：用 12% 的路程升起来，不凭空冒出。内聚不走这条 —— 它从外缘的淡色开始，
    // 本来就不需要「出现」（见 DrawRipples）
    private const double RippleAttack = 0.12;

    // 内聚的两端透明度：外缘淡、环上实。它一路加深到底，不做收尾淡出
    private const double ReceiveOuterAlpha = 0.15;
    private const double ReceiveInnerAlpha = 0.90;

    // 一圈上只画一道波纹。两道虽然更「连续」，但同屏永远有两只环挂在固定的内外半径之间，
    // 读起来像一个静止的靶心 —— 眼睛抓不住单个环，方向也就读不出来。一道走完再起一道，
    // 周期之间那点空档反而让「一波一波」这件事看得更清楚。
    private const int RippleCount = 1;

    // 慢到眼睛能跟住一只环走完全程
    private static readonly TimeSpan SweepPeriod = TimeSpan.FromMilliseconds(2300);

    /// <summary>
    /// The clock. One repeating chain on one property — everything the port draws is a function of it.
    /// <para>
    /// <c>LoopTime = int.MaxValue</c> is the only "forever" this system has, and the duration must not be zero:
    /// a zero-length pass consumes no time, so a forever loop over it spins instead of looping and
    /// <c>Transition.Exit</c> can no longer interrupt it.
    /// </para>
    /// </summary>
    private static readonly Transition<SlotView> Spin =
        Transition<SlotView>.Create()
            .Property(v => v.Sweep, 1d)
            .Effect(new TransitionEffect
            {
                Duration = SweepPeriod,
                LoopTime = int.MaxValue,
                Ease = Eases.Default,
            });

    private bool _running;

    public SlotView()
    {
        // 命中测试面。原来这个 Background 写在被删掉的 axaml 根上，前缀的 behaviors: 也在那里，
        // 两者一起消失之后端口就再也收不到指针事件了。
        // 这里放一个实在的子元素而不是只依赖控件自己的 Background：PointerPressed 是冒泡事件，
        // 落在子元素上一样会走到挂在 SlotView 上的行为，但「有没有一个能画出来的面」不再是推断。
        Background = new ImmutableSolidColorBrush(Color.FromArgb(1, 0, 0, 0));
        Content = new Border { Background = new ImmutableSolidColorBrush(Color.FromArgb(1, 0, 0, 0)) };

        // 每个卡过去都在自己的 SlotView 上写这句；现在写在控件里，卡片忘了也不会再丢掉交互
        WorkflowSlotConnectionBehavior.SetIsEnabled(this, true);

        Sync();
    }

    #region Styled properties

    public static readonly StyledProperty<SlotState> SlotStateProperty =
        AvaloniaProperty.Register<SlotView, SlotState>(nameof(SlotState), SlotState.StandBy);

    /// <summary>The slot's declared capacity. Decides which way the ripples run, independently of connections.</summary>
    public static readonly StyledProperty<SlotChannel> ChannelProperty =
        AvaloniaProperty.Register<SlotView, SlotChannel>(nameof(Channel), SlotChannel.MultipleBoth);

    /// <summary>The clock's phase, 0 to 1, repeating. Animated, never set by a caller.</summary>
    public static readonly StyledProperty<double> SweepProperty =
        AvaloniaProperty.Register<SlotView, double>(nameof(Sweep));

    public SlotState SlotState
    {
        get => GetValue(SlotStateProperty);
        set => SetValue(SlotStateProperty, value);
    }

    public SlotChannel Channel
    {
        get => GetValue(ChannelProperty);
        set => SetValue(ChannelProperty, value);
    }

    public double Sweep
    {
        get => GetValue(SweepProperty);
        set => SetValue(SweepProperty, value);
    }

    static SlotView()
    {
        AffectsRender<SlotView>(SlotStateProperty, ChannelProperty, SweepProperty);
    }

    #endregion

    #region State

    /// <summary>Whether the slot may connect outward — a target direction is declared.</summary>
    private bool CanSend => Channel.HasFlag(SlotChannel.OneTarget) || Channel.HasFlag(SlotChannel.MultipleTargets);

    /// <summary>Whether the slot may be connected inward — a source direction is declared.</summary>
    private bool CanReceive => Channel.HasFlag(SlotChannel.OneSource) || Channel.HasFlag(SlotChannel.MultipleSources);

    private bool IsAiming =>
        SlotState.HasFlag(SlotState.PreviewSender) || SlotState.HasFlag(SlotState.PreviewReceiver);

    private bool IsConnected =>
        SlotState.HasFlag(SlotState.Sender) || SlotState.HasFlag(SlotState.Receiver);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == SlotStateProperty || change.Property == ForegroundProperty)
        {
            // Foreground 来自节点视图的样式选择器，它不是 AffectsRender 的属性，
            // 不主动重绘的话换了状态颜色也不会跟着变
            InvalidateVisual();
        }
    }

    // 只要端口声明了任一方向，波纹就该跑；没有方向的端口是一条死线，挂上去只是白烧 CPU
    private void Sync()
    {
        bool ripples = CanSend || CanReceive;

        if (ripples && !_running)
        {
            Sweep = 0;
            Spin.Execute(this);
            _running = true;
        }
        else if (!ripples && _running)
        {
            Stop();
        }
    }

    private void Stop()
    {
        if (!_running) return;
        Transition.Exit(this, IncludeMutual: true, IncludeNoMutual: true);
        _running = false;
    }

    // 悬停要亮一档：端口小，没有反馈就不知道指针到底有没有搭上它。
    // IsPointerOver 不是 AffectsRender 的属性，进出各重绘一次。
    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        InvalidateVisual();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Sync();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);

        // 永久循环不会因为控件离开可视树而停：滚走的节点会留着一条对无人可见的控件写 Sweep 的采样循环。
        // 端口是被池化复用的，所以这里必须停，下一次挂载 Sync 会重新起。
        Stop();
    }

    #endregion

    #region Render

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        double size = Math.Min(Math.Min(Bounds.Width, Bounds.Height), GlyphReference);
        if (size <= 2) return;

        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
        var color = (Foreground as ISolidColorBrush)?.Color ?? Colors.White;

        double ringRadius = size * RingRadiusRatio;
        bool aiming = IsAiming;
        bool connected = IsConnected;

        // 呼吸从这个时钟推导：`sin` 的相位就是 Sweep，所以它和波纹天然同步，
        // 也不需要第二条动画（见类注释：Exit 是按目标停的，两条会互相打断）
        double breath = aiming ? (0.5 + (0.5 * Math.Sin(Sweep * Math.PI * 2))) : 0;

        DrawRipples(context, center, size, ringRadius, color, aiming, connected);

        // 环与芯：静息时压暗，被瞄准或已连上时全亮。呼吸推的是同一个亮度，所以它一定看得见
        double lit = (aiming || connected ? 0.75 : 0.45) + (0.25 * breath) + (IsPointerOver ? 0.2 : 0);
        var ringPen = new Pen(new ImmutableSolidColorBrush(color, Math.Min(1, lit)), size * RingThicknessRatio);
        context.DrawEllipse(null, ringPen, center, ringRadius, ringRadius);

        double coreScale = (aiming ? 1.2 : 1.0) + (0.3 * breath);
        context.DrawEllipse(
            new ImmutableSolidColorBrush(color, Math.Min(1, lit + 0.2)), null,
            center, size * CoreRadiusRatio * coreScale, size * CoreRadiusRatio * coreScale);
    }

    /// <summary>
    /// The direction language: send ripples outward, receive ripples inward, both when the slot declares both.
    /// </summary>
    /// <remarks>
    /// Outward ripples fade as they leave — energy departing. Inward ones brighten as they arrive — energy
    /// landing — so the two read as opposite even in a still frame: one is darkest at its outermost, the other
    /// brightest at its innermost.
    /// </remarks>
    private void DrawRipples(DrawingContext context, Point center, double size, double ringRadius,
        Color color, bool aiming, bool connected)
    {
        if (!CanSend && !CanReceive) return;

        double inner = ringRadius * RippleInnerRatio;
        double outer = (size / 2) - 1;
        if (outer <= inner) return;

        double thickness = Math.Max(0.8, size * RippleThicknessRatio);
        double strength = aiming || connected ? 1.0 : 0.6;

        for (int k = 0; k < RippleCount; k++)
        {
            double p = (Sweep + (k / (double)RippleCount)) % 1.0;

            if (CanSend)
            {
                // 外散：在环上冒出来（12% 的路程升到全亮），越往外越淡
                double appear = Math.Min(1, p / RippleAttack);
                double r = inner + ((outer - inner) * p);
                var pen = new Pen(new ImmutableSolidColorBrush(color, appear * (1 - p) * 0.7 * strength), thickness);
                context.DrawEllipse(null, pen, center, r, r);
            }

            if (CanReceive)
            {
                // 内聚：外缘淡、一路加深，收在环上。
                // 不做收尾淡出是因为终点与环同半径（RippleInnerRatio = 1.0）：最后那一下是并进环里
                // 而不是凭空消失，所以从「最深」跳回「外缘的淡」读起来是新的一波从外面过来，
                // 而不是刚才那只环炸掉
                double arrive = ReceiveOuterAlpha + ((ReceiveInnerAlpha - ReceiveOuterAlpha) * p);
                double r = outer - ((outer - inner) * p);
                var pen = new Pen(new ImmutableSolidColorBrush(color, arrive * strength), thickness);
                context.DrawEllipse(null, pen, center, r, r);
            }
        }
    }

    #endregion
}
