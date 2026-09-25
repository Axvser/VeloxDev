using Microsoft.Maui.Graphics;
using VeloxDev.TransitionSystem;
using VeloxDev.WorkflowSystem;

namespace Demo.Controls;

/// <summary>
/// The port on a node: a fine ring with a small core, and ripples that say which way data goes.
/// <para>
/// The direction is carried by the <b>shape of the motion</b> rather than by a colour or a rotating token:
/// ripples spread <b>outward</b> when the slot can send, <b>inward</b> when it can receive, and both ways at
/// once when it can do either. That comes from <see cref="Channel"/> — the slot's declared capacity — and not
/// from what it happens to be connected to, so a port states what it is for even with nothing wired to it.
/// </para>
/// <para>
/// Self-drawn through the same <see cref="ICanvas"/> the connections use, rather than assembled out of
/// <c>Ellipse</c>s whose radius/opacity would have to be rewritten every frame. The glyph is a ring, a core
/// and a travelling ripple: on MAUI each of those would be a separate view with its own layout pass, and the
/// ripple's radius and alpha would become two more properties to animate — the drawing surface expresses the
/// whole thing as arithmetic over one number.
/// </para>
/// <para>
/// One clock drives everything. <c>Transition</c>'s exit is per-target, so two independent loops on one
/// control would stop each other; the aim-breath is therefore <i>derived</i> from the same <see cref="Sweep"/>
/// that moves the ripples rather than being a loop of its own.
/// </para>
/// </summary>
public partial class SlotView : ContentView
{
    // 字形按控件尺寸等比缩放。Avalonia 那边还会再取一个 32 设计单位的上限（端口的作者尺寸是 32 或 24，
    // 上限让它不至于跟着 Viewbox 无限放大）；MAUI 这边没有 Viewbox —— 端口的尺寸本身就是设计值、
    // 由 NodeMetricScaler 按 k 缩放，所以「上限」已经写在作者尺寸里，这里只需按短边取比例。
    private const double RingRadiusRatio = 0.205;
    private const double RingThicknessRatio = 0.045;
    private const double CoreRadiusRatio = 0.06;

    // 波纹的终点半径就是环本身（1.0×），外缘留 1 单位不碰边界。
    // 从前内圈取 1.25×，内聚波纹于是停在环外一截再凭空消失 —— 看着像没走到就被掐了
    private const double RippleInnerRatio = 1.0;
    private const double RippleThicknessRatio = 0.05;

    // 外散的出现段：用 12% 的路程升起来，不凭空冒出。内聚不走这条 —— 它从外缘的淡色开始，
    // 本来就不需要「出现」
    private const double RippleAttack = 0.12;

    // 内聚的两端透明度：外缘淡、环上实。它一路加深到底，不做收尾淡出
    private const double ReceiveOuterAlpha = 0.15;
    private const double ReceiveInnerAlpha = 0.90;

    // 一圈上只画一道波纹。两道虽然更「连续」，但同屏永远有两只环挂在固定的内外半径之间，
    // 读起来像一个静止的靶心 —— 眼睛抓不住单个环，方向也就读不出来。
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

    public static readonly BindableProperty SlotStateProperty = BindableProperty.Create(
        nameof(SlotState),
        typeof(SlotState),
        typeof(SlotView),
        SlotState.StandBy,
        propertyChanged: OnVisualPropertyChanged);

    /// <summary>The slot's declared capacity. Decides which way the ripples run, independently of connections.</summary>
    public static readonly BindableProperty ChannelProperty = BindableProperty.Create(
        nameof(Channel),
        typeof(SlotChannel),
        typeof(SlotView),
        SlotChannel.MultipleBoth,
        propertyChanged: OnChannelPropertyChanged);

    private double _sweep;
    private bool _pointerOver;
    private bool _running;
    private bool _loaded;

    public SlotView()
    {
        InitializeComponent();
        IconView.Drawable = new SlotDrawable(this);

        // 悬停要亮一档：端口小，没有反馈就不知道指针到底有没有搭上它。
        // ContentView 自己没有「指针在上面」这个属性，所以靠手势识别器自己记一个。
        var hover = new PointerGestureRecognizer();
        hover.PointerEntered += (_, _) => SetPointerOver(true);
        hover.PointerExited += (_, _) => SetPointerOver(false);
        GestureRecognizers.Add(hover);

        // 动画的起停跟着可视树，不跟构造：端口会被 ViewPool 复用，滚走的节点不能留着一条
        // 对无人可见的控件写 Sweep 的采样循环
        Loaded += (_, _) =>
        {
            _loaded = true;
            Sync();
        };
        Unloaded += (_, _) =>
        {
            _loaded = false;
            Stop();
        };
    }

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

    /// <summary>The clock's phase, 0 to 1, repeating. Animated, never set by a caller.</summary>
    public double Sweep
    {
        get => _sweep;
        set
        {
            _sweep = value;
            IconView?.Invalidate();
        }
    }

    #region State

    /// <summary>Whether the slot may connect outward — a target direction is declared.</summary>
    private bool CanSend => Channel.HasFlag(SlotChannel.OneTarget) || Channel.HasFlag(SlotChannel.MultipleTargets);

    /// <summary>Whether the slot may be connected inward — a source direction is declared.</summary>
    private bool CanReceive => Channel.HasFlag(SlotChannel.OneSource) || Channel.HasFlag(SlotChannel.MultipleSources);

    private static void OnVisualPropertyChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is SlotView slotView)
        {
            slotView.IconView?.Invalidate();
        }
    }

    private static void OnChannelPropertyChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is not SlotView slotView)
        {
            return;
        }

        slotView.IconView?.Invalidate();
        slotView.Sync();
    }

    private void SetPointerOver(bool value)
    {
        if (_pointerOver == value)
        {
            return;
        }

        _pointerOver = value;
        IconView?.Invalidate();
    }

    // 只要端口声明了任一方向，波纹就该跑；没有方向的端口是一条死线，挂上去只是白烧 CPU
    private void Sync()
    {
        bool ripples = _loaded && (CanSend || CanReceive);

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
        if (!_running)
        {
            return;
        }

        Transition.Exit(this, IncludeMutual: true, IncludeNoMutual: true);
        _running = false;
    }

    #endregion

    #region Render

    private sealed class SlotDrawable(SlotView owner) : IDrawable
    {
        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            float size = MathF.Min(dirtyRect.Width, dirtyRect.Height);
            if (size <= 2)
            {
                return;
            }

            var centerX = dirtyRect.Center.X;
            var centerY = dirtyRect.Center.Y;
            var color = ResolveSlotColor(owner.SlotState);

            var ringRadius = (float)(size * RingRadiusRatio);
            bool aiming = IsAiming(owner.SlotState);
            bool connected = IsConnected(owner.SlotState);

            // 呼吸从这个时钟推导：`sin` 的相位就是 Sweep，所以它和波纹天然同步，
            // 也不需要第二条动画（见类注释：Exit 是按目标停的，两条会互相打断）
            double breath = aiming ? 0.5 + (0.5 * Math.Sin(owner.Sweep * Math.PI * 2)) : 0;

            DrawRipples(canvas, centerX, centerY, size, ringRadius, color, aiming, connected);

            // 环与芯：静息时压暗，被瞄准或已连上时全亮。呼吸推的是同一个亮度
            double lit = (aiming || connected ? 0.75 : 0.45) + (0.25 * breath) + (owner._pointerOver ? 0.2 : 0);
            canvas.StrokeColor = color.WithAlpha((float)Math.Min(1, lit));
            canvas.StrokeSize = (float)(size * RingThicknessRatio);
            canvas.DrawCircle(centerX, centerY, ringRadius);

            double coreScale = (aiming ? 1.2 : 1.0) + (0.3 * breath);
            canvas.FillColor = color.WithAlpha((float)Math.Min(1, lit + 0.2));
            canvas.FillCircle(centerX, centerY, (float)(size * CoreRadiusRatio * coreScale));
        }

        /// <summary>
        /// The direction language: send ripples outward, receive ripples inward, both when the slot declares both.
        /// </summary>
        /// <remarks>
        /// Outward ripples fade as they leave — energy departing. Inward ones brighten as they arrive — energy
        /// landing — so the two read as opposite even in a still frame: one is darkest at its outermost, the other
        /// brightest at its innermost.
        /// </remarks>
        private void DrawRipples(ICanvas canvas, float centerX, float centerY, float size, float ringRadius,
            Color color, bool aiming, bool connected)
        {
            if (!owner.CanSend && !owner.CanReceive)
            {
                return;
            }

            double inner = ringRadius * RippleInnerRatio;
            double outer = (size / 2) - 1;
            if (outer <= inner)
            {
                return;
            }

            var thickness = (float)Math.Max(0.8, size * RippleThicknessRatio);
            double strength = aiming || connected ? 1.0 : 0.6;

            canvas.StrokeSize = thickness;

            for (var k = 0; k < RippleCount; k++)
            {
                double p = (owner.Sweep + (k / (double)RippleCount)) % 1.0;

                if (owner.CanSend)
                {
                    // 外散：在环上冒出来（12% 的路程升到全亮），越往外越淡
                    double appear = Math.Min(1, p / RippleAttack);
                    var radius = (float)(inner + ((outer - inner) * p));
                    canvas.StrokeColor = color.WithAlpha((float)(appear * (1 - p) * 0.7 * strength));
                    canvas.DrawCircle(centerX, centerY, radius);
                }

                if (owner.CanReceive)
                {
                    // 内聚：外缘淡、一路加深，收在环上。
                    // 不做收尾淡出是因为终点与环同半径（RippleInnerRatio = 1.0）：最后那一下是并进环里
                    // 而不是凭空消失，所以从「最深」跳回「外缘的淡」读起来是新的一波从外面过来
                    double arrive = ReceiveOuterAlpha + ((ReceiveInnerAlpha - ReceiveOuterAlpha) * p);
                    var radius = (float)(outer - ((outer - inner) * p));
                    canvas.StrokeColor = color.WithAlpha((float)(arrive * strength));
                    canvas.DrawCircle(centerX, centerY, radius);
                }
            }
        }

        private static bool IsAiming(SlotState state)
            => state.HasFlag(SlotState.PreviewSender) || state.HasFlag(SlotState.PreviewReceiver);

        private static bool IsConnected(SlotState state)
            => state.HasFlag(SlotState.Sender) || state.HasFlag(SlotState.Receiver);

        /// <summary>
        /// The colour the previous port already used: white at rest, one colour per connection role.
        /// Avalonia carries it on <c>Foreground</c> through style selectors; MAUI has no style selector that
        /// keys off an enum value, so the same three-way mapping lives here.
        /// </summary>
        private static Color ResolveSlotColor(SlotState state)
            => state switch
            {
                var value when value.HasFlag(SlotState.Sender) && value.HasFlag(SlotState.Receiver) => Colors.Violet,
                var value when value.HasFlag(SlotState.Sender) => Colors.Tomato,
                var value when value.HasFlag(SlotState.Receiver) => Colors.Lime,
                _ => Colors.White,
            };
    }

    #endregion
}
