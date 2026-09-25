using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using VeloxDev.TransitionSystem;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace Demo.Views.Workflow;

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
/// This file is the WPF reading of Avalonia's <c>Views/Workflow/SlotView.cs</c>; the glyph, its proportions,
/// its clock and its direction language are the same numbers. What differs is the platform:
/// </para>
/// <list type="bullet">
/// <item>
/// Avalonia's <c>AffectsRender</c> has no WPF spelling, so each styled property that the glyph reads carries a
/// <see cref="PropertyMetadata"/> callback into <see cref="UIElement.InvalidateVisual"/> — that is the whole of
/// the "affects render" contract, and it must be there: WPF keeps a control's <c>OnRender</c> output as retained
/// drawing, so <b>writing a value never repaints on its own</b>. The same reason is why a resize invalidates too.
/// </item>
/// <item>
/// Avalonia's control carried no template, so its <c>Background</c> plus a real child element were what made the
/// port a pointer target. WPF's <c>UserControl</c> is not reliable in that role either, so the constructor plants
/// an opaque-to-hit-testing child as well as the background. Both, not either: the port is a 32-unit square whose
/// drawn glyph is a ring a few units thick, and a hit surface the size of the ring would be unusable.
/// </item>
/// <item>
/// The connection behaviour is switched on here rather than in each card. Every card used to write
/// <c>behaviors:WorkflowSlotConnectionBehavior.IsEnabled="True"</c> on its slot, and — along with the
/// <c>Background</c> that gave the glyph a hit-test surface — dropping that attribute silently cost the ports
/// their pointer input. Neither can be forgotten from a card now, because neither is in a card.
/// </item>
/// <item>
/// Avalonia declared the clock as one <c>static readonly</c> transition shared by every port. WPF's transition
/// is built per control in this repo (<c>PolylineCurveView</c>) so that the values a run captures belong to the
/// view that runs them; a port is cheap and pooled views are long-lived, so the same shape is kept here.
/// </item>
/// </list>
/// <para>
/// It is a <see cref="UserControl"/> and not a bare <see cref="System.Windows.Controls.Control"/> because the
/// cards name it as <c>&lt;local:SlotView&gt;</c> in XAML and the layout behaviour resolves it by name; a control
/// with no template of its own would render neither the glyph nor a hit surface.
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

    private Transition<SlotView>? _spin;
    private bool _running;

    public SlotView()
    {
        // 命中测试面。原来这个 Background 与 behaviors: 前缀都写在 SlotView.xaml 的根上，
        // 两者一起消失之后端口就再也收不到指针事件了。
        // 这里放一个实在的子元素而不是只依赖控件自己的 Background：PointerPressed 是冒泡事件，
        // 落在子元素上一样会走到挂在 SlotView 上的行为，但「有没有一个能画出来的面」不再是推断。
        Background = Brushes.Transparent;
        Content = new Border { Background = Brushes.Transparent };

        // 每个卡过去都在自己的 SlotView 上写这句；现在写在控件里，卡片忘了也不会再丢掉交互
        WorkflowSlotConnectionBehavior.SetIsEnabled(this, true);

        // 先落一次颜色：Control.Foreground 的默认是系统文字色（近黑），而依赖属性「没变过」
        // 就不会有回调 —— 卡片不绑 SlotState 时端口会是一枚黑环
        UpdateForeground();

        // WPF 的 UserControl 没有可重写的虚拟挂载方法，用 Loaded/Unloaded 这对事件（WPF 适配器也用它挂载画布）；
        // 构造时不起周期：还不在树上的控件不该有一条对无人可见的画面写 Sweep 的循环
        Loaded += (_, _) => Sync();
        Unloaded += (_, _) => Stop();

        // 字形按 Bounds 缩放，尺寸一变就必须重画：见类注释（写值本身不会重绘）
        SizeChanged += (_, _) => InvalidateVisual();

        Sync();
    }

    #region Dependency properties

    public static readonly DependencyProperty SlotStateProperty =
        DependencyProperty.Register(nameof(SlotState), typeof(SlotState), typeof(SlotView),
            new PropertyMetadata(SlotState.StandBy, OnGlyphChanged));

    /// <summary>The slot's declared capacity. Decides which way the ripples run, independently of connections.</summary>
    public static readonly DependencyProperty ChannelProperty =
        DependencyProperty.Register(nameof(Channel), typeof(SlotChannel), typeof(SlotView),
            new PropertyMetadata(SlotChannel.MultipleBoth, OnGlyphChanged));

    /// <summary>The clock's phase, 0 to 1, repeating. Animated, never set by a caller.</summary>
    public static readonly DependencyProperty SweepProperty =
        DependencyProperty.Register(nameof(Sweep), typeof(double), typeof(SlotView),
            new PropertyMetadata(0d, OnGlyphChanged));

    public SlotState SlotState
    {
        get => (SlotState)GetValue(SlotStateProperty);
        set => SetValue(SlotStateProperty, value);
    }

    public SlotChannel Channel
    {
        get => (SlotChannel)GetValue(ChannelProperty);
        set => SetValue(ChannelProperty, value);
    }

    public double Sweep
    {
        get => (double)GetValue(SweepProperty);
        set => SetValue(SweepProperty, value);
    }

    /// <summary>
    /// The WPF stand-in for Avalonia's <c>AffectsRender</c>: repaint, re-derive the state colour, and start or
    /// stop the clock when the direction a slot declares changes.
    /// </summary>
    private static void OnGlyphChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var slot = (SlotView)d;

        // Foreground 来自卡片，它不是本控件的依赖属性，换了状态颜色也不会自己重绘
        if (e.Property == SlotStateProperty)
        {
            slot.UpdateForeground();
        }

        if (e.Property == ChannelProperty)
        {
            slot.Sync();
        }

        slot.InvalidateVisual();
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

    /// <summary>
    /// The port's colour by state, kept from the cards that used to declare it one by one (input slots point
    /// left, output slots point right, and their state tints the glyph). The new design says direction with the
    /// ripple and leaves the ring symmetric, so this is the only thing the old pin direction was carrying.
    /// </summary>
    private void UpdateForeground()
    {
        Foreground = SlotState switch
        {
            var state when state.HasFlag(SlotState.Sender) && state.HasFlag(SlotState.Receiver) => Brushes.Violet,
            var state when state.HasFlag(SlotState.Sender) => Brushes.Tomato,
            var state when state.HasFlag(SlotState.Receiver) => Brushes.Lime,
            _ => Brushes.White,
        };
    }

    // 每视图构建：周期只写一个标量，几何与配色都不参与
    // 匀速（Eases.Default 就是恒等）—— 波纹不该有缓动，速度一变化就不像在流了
    private Transition<SlotView> BuildSpin() => Transition<SlotView>.Create()
        .Property(v => v.Sweep, 1d)
        .Effect(new TransitionEffect
        {
            Duration = SweepPeriod,
            // int.MaxValue 是这套系统唯一的「永远」，且时长不能为零：零时长的趟不占时间，
            // 对它的永久循环是空转而不是循环，Transition.Exit 也就再打断不了它
            LoopTime = int.MaxValue,
            Ease = Eases.Default,
        });

    // 只要端口声明了任一方向，波纹就该跑；没有方向的端口是一条死线，挂上去只是白烧 CPU
    private void Sync()
    {
        bool ripples = CanSend || CanReceive;

        if (ripples && !_running && IsLoaded)
        {
            // 声明从目标读起值，所以执行前必须把标量摆到周期起点；永久循环在每个接缝重放这一份起始状态
            Sweep = 0;
            (_spin ??= BuildSpin()).Execute(this);
            _running = true;
        }
        else if (!ripples && _running)
        {
            Stop();
        }
    }

    private void Stop()
    {
        if (!_running)
        {
            return;
        }

        Transition.Exit(this, IncludeMutual: true, IncludeNoMutual: true);
        _running = false;
    }

    // 悬停要亮一档：端口小，没有反馈就不知道指针到底有没有搭上它。
    // IsMouseOver 不是本控件的依赖属性，进出各重绘一次。
    protected override void OnMouseEnter(System.Windows.Input.MouseEventArgs e)
    {
        base.OnMouseEnter(e);
        InvalidateVisual();
    }

    protected override void OnMouseLeave(System.Windows.Input.MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        InvalidateVisual();
    }

    #endregion

    #region Render

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        double size = Math.Min(Math.Min(ActualWidth, ActualHeight), GlyphReference);
        if (size <= 2) return;

        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        var color = (Foreground as SolidColorBrush)?.Color ?? Colors.White;

        double ringRadius = size * RingRadiusRatio;
        bool aiming = IsAiming;
        bool connected = IsConnected;

        // 呼吸从这个时钟推导：`sin` 的相位就是 Sweep，所以它和波纹天然同步，
        // 也不需要第二条动画（见类注释：Exit 是按目标停的，两条会互相打断）
        double breath = aiming ? (0.5 + (0.5 * Math.Sin(Sweep * Math.PI * 2))) : 0;

        DrawRipples(dc, center, size, ringRadius, color, aiming, connected);

        // 环与芯：静息时压暗，被瞄准或已连上时全亮。呼吸推的是同一个亮度，所以它一定看得见
        double lit = (aiming || connected ? 0.75 : 0.45) + (0.25 * breath) + (IsMouseOver ? 0.2 : 0);
        var ringPen = new Pen(AtAlpha(color, lit), size * RingThicknessRatio);
        dc.DrawEllipse(null, ringPen, center, ringRadius, ringRadius);

        double coreScale = (aiming ? 1.2 : 1.0) + (0.3 * breath);
        dc.DrawEllipse(
            AtAlpha(color, lit + 0.2), null,
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
    private void DrawRipples(DrawingContext dc, Point center, double size, double ringRadius,
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
                var pen = new Pen(AtAlpha(color, appear * (1 - p) * 0.7 * strength), thickness);
                dc.DrawEllipse(null, pen, center, r, r);
            }

            if (CanReceive)
            {
                // 内聚：外缘淡、一路加深，收在环上。
                // 不做收尾淡出是因为终点与环同半径（RippleInnerRatio = 1.0）：最后那一下是并进环里
                // 而不是凭空消失，所以从「最深」跳回「外缘的淡」读起来是新的一波从外面过来，
                // 而不是刚才那只环炸掉
                double arrive = ReceiveOuterAlpha + ((ReceiveInnerAlpha - ReceiveOuterAlpha) * p);
                double r = outer - ((outer - inner) * p);
                var pen = new Pen(AtAlpha(color, arrive * strength), thickness);
                dc.DrawEllipse(null, pen, center, r, r);
            }
        }
    }

    /// <summary>Carries the glyph's colour and an independent alpha, frozen — one of these is built per frame.</summary>
    private static SolidColorBrush AtAlpha(Color color, double alpha)
    {
        var brush = new SolidColorBrush(Color.FromArgb(
            (byte)Math.Round(Math.Clamp(alpha, 0, 1) * 255), color.R, color.G, color.B));
        brush.Freeze();
        return brush;
    }

    #endregion
}
