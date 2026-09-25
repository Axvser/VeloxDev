using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using System;
using VeloxDev.TransitionSystem;
using VeloxDev.WorkflowSystem;

namespace Demo.Views
{
    /// <summary>
    /// The port on a node: a fine ring with a small core, and ripples that say which way data goes.
    /// <para>
    /// The direction is carried by the <b>shape of the motion</b> rather than by a colour or a rotating token:
    /// ripples spread <b>outward</b> when the slot can send, <b>inward</b> when it can receive, and both ways at
    /// once when it can do either. That comes from <see cref="Channel"/> — the slot's declared capacity — and not
    /// from what it happens to be connected to, so a port states what it is for even with nothing wired to it.
    /// </para>
    /// <para>
    /// The platform difference from the Avalonia original (Views/Workflow/SlotView.cs) is the whole reason this
    /// file looks different: Avalonia overrides <c>Render(DrawingContext)</c> and draws the glyph fresh every
    /// frame, WinUI is retained mode and has no such hook. The same picture is therefore carried by four
    /// <see cref="Ellipse"/> elements whose diameter and opacity are rewritten each frame — the two properties
    /// named, nothing else. The ratios, the period and the direction language are copied verbatim so the two
    /// platforms draw the same port.
    /// </para>
    /// <para>
    /// One clock drives everything. <see cref="Transition{T}"/>'s exit is per-target, so two independent loops on
    /// one control would stop each other; the aim-breath is therefore <i>derived</i> from the same
    /// <see cref="Sweep"/> that moves the ripples rather than being a loop of its own.
    /// </para>
    /// </summary>
    public sealed partial class SlotView : UserControl
    {
        // 字形按控件尺寸等比缩放，但以 32 设计单位为上限 —— 端口在卡里只有二十几单位，
        // 再大就不成其为「点」了。这三个比例都比从前细一半以上：0.205 / 0.045 / 0.06
        private const double GlyphReference = 32;
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
        // 读起来像一个静止的靶心 —— 眼睛抓不住单个环，方向也就读不出来。一道走完再起一道，
        // 周期之间那点空档反而让「一波一波」这件事看得更清楚。
        private const int RippleCount = 1;

        // 慢到眼睛能跟住一只环走完全程
        private static readonly TimeSpan SweepPeriod = TimeSpan.FromMilliseconds(2300);

        public static readonly DependencyProperty SlotStateProperty = DependencyProperty.Register(
            nameof(SlotState),
            typeof(SlotState),
            typeof(SlotView),
            new PropertyMetadata(SlotState.StandBy, OnSlotStateChanged));

        /// <summary>The slot's declared capacity. Decides which way the ripples run, independently of connections.</summary>
        public static readonly DependencyProperty ChannelProperty = DependencyProperty.Register(
            nameof(Channel),
            typeof(SlotChannel),
            typeof(SlotView),
            new PropertyMetadata(SlotChannel.MultipleBoth, OnChannelChanged));

        // 四个圆共用一支画刷：字形只有一个颜色，改它比改四个 Fill/Stroke 便宜
        private readonly SolidColorBrush _glyphBrush = new(Microsoft.UI.Colors.White);

        // 每视图构建（不是 static readonly）：Transition<T> 首次被触碰时会把它的调度器建在当时的线程上，
        // 静态字段的初始化线程取决于谁的构造先跑到那里；挂在实例上就一定是 UI 线程。同一控件上也只跑这一条链
        // —— Transition.Exit 按目标停，两条会互相打断。
        private Transition<SlotView>? _spin;
        private bool _running;

        // 时钟相位，0 到 1，循环。动画写入，调用方从不设它
        private double _sweep;

        // 指针是否搭在这个端口上。WinUI 的 UIElement 没有 IsPointerOver（Avalonia 有），
        // 所以这一位由进出事件自己维护
        private bool _pointerOver;

        public SlotView()
        {
            InitializeComponent();

            Ring.Stroke = _glyphBrush;
            Core.Fill = _glyphBrush;
            RippleOut.Stroke = _glyphBrush;
            RippleIn.Stroke = _glyphBrush;

            SizeChanged += (_, _) => Render();
            PointerEntered += (_, _) => { _pointerOver = true; Render(); };
            PointerExited += (_, _) => { _pointerOver = false; Render(); };
            Loaded += (_, _) => Sync();
            Unloaded += (_, _) => Stop();

            Sync();
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

        /// <summary>
        /// The clock's phase, 0 to 1, repeating. Animated, never set by a caller.
        /// </summary>
        /// <remarks>
        /// A plain property rather than a dependency property, deliberately: the transition system writes animatable
        /// members through compiled reflection paths and does not need the dependency-property store. Keeping it plain
        /// also means the one write per frame lands straight in <see cref="Render"/> instead of going through
        /// <c>SetValue</c> first.
        /// </remarks>
        public double Sweep
        {
            get => _sweep;
            set
            {
                _sweep = value;
                Render();
            }
        }

        private static void OnSlotStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
            => ((SlotView)d).Render();

        private static void OnChannelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
            => ((SlotView)d).Sync();

        #region State

        /// <summary>Whether the slot may connect outward — a target direction is declared.</summary>
        private bool CanSend => Channel.HasFlag(SlotChannel.OneTarget) || Channel.HasFlag(SlotChannel.MultipleTargets);

        /// <summary>Whether the slot may be connected inward — a source direction is declared.</summary>
        private bool CanReceive => Channel.HasFlag(SlotChannel.OneSource) || Channel.HasFlag(SlotChannel.MultipleSources);

        private bool IsAiming =>
            SlotState.HasFlag(SlotState.PreviewSender) || SlotState.HasFlag(SlotState.PreviewReceiver);

        private bool IsConnected =>
            SlotState.HasFlag(SlotState.Sender) || SlotState.HasFlag(SlotState.Receiver);

        // 只要端口声明了任一方向，波纹就该跑；没有方向的端口是一条死线，挂上去只是白烧 CPU
        private void Sync()
        {
            bool ripples = CanSend || CanReceive;

            if (ripples && !_running)
            {
                // 链从目标读起始值，执行前先把相位摆到周期起点（直接写字段，不经过 setter，
                // 免得在 Execute 之前先画一帧假的）
                _sweep = 0;
                _spin ??= BuildSpin();
                _spin.Execute(this);
                _running = true;
            }
            else if (!ripples && _running)
            {
                Stop();
            }

            Render();
        }

        /// <summary>
        /// One repeating chain on one property — everything the port draws is a function of it.
        /// </summary>
        /// <remarks>
        /// <c>LoopTime = int.MaxValue</c> is the only "forever" this system has, and the duration must not be zero:
        /// a zero-length pass consumes no time, so a forever loop over it spins instead of looping and
        /// <c>Transition.Exit</c> can no longer interrupt it.
        /// </remarks>
        private static Transition<SlotView> BuildSpin() => Transition<SlotView>.Create()
            .Property(v => v.Sweep, 1d)
            .Effect(new TransitionEffect
            {
                Duration = SweepPeriod,
                LoopTime = int.MaxValue,
                Ease = Eases.Default,
            });

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

        // 这里是 Avalonia 那版 Render(DrawingContext) 的对应物：同样的算式，结果写到元素属性上而不是画笔上
        private void Render()
        {
            // DP 回调与动画帧都可能早于 InitializeComponent 的命名元素就绪（理论上），守一道
            if (Ring is null || Core is null || RippleOut is null || RippleIn is null)
            {
                return;
            }

            double size = Math.Min(Math.Min(ActualWidth, ActualHeight), GlyphReference);
            if (size <= 2)
            {
                return;
            }

            UpdateGlyphColor();

            bool aiming = IsAiming;
            bool connected = IsConnected;

            // 呼吸从这个时钟推导：`sin` 的相位就是 Sweep，所以它和波纹天然同步，
            // 也不需要第二条动画（见类注释：Exit 是按目标停的，两条会互相打断）
            double breath = aiming ? (0.5 + (0.5 * Math.Sin(_sweep * Math.PI * 2))) : 0;

            double ringRadius = size * RingRadiusRatio;

            // 环与芯：静息时压暗，被瞄准或已连上时全亮。呼吸推的是同一个亮度，所以它一定看得见。
            // Avalonia 那边把这个 alpha 写进画刷，这里写进元素自身的 Opacity —— 底是全透明的，
            // 两者等价，而 Opacity 不进画刷重建。
            // 悬停要亮一档：端口小，没有反馈就不知道指针到底有没有搭上它。Avalonia 有 IsPointerOver
            // 可以直接读，WinUI 的 UIElement 上没有这个属性（会编译不过），所以自己跟一对进出事件。
            double lit = (aiming || connected ? 0.75 : 0.45) + (0.25 * breath) + (_pointerOver ? 0.2 : 0);

            Ring.StrokeThickness = Math.Max(0.6, size * RingThicknessRatio);
            SetDiameter(Ring, ringRadius);
            Ring.Opacity = Math.Min(1, lit);

            double coreScale = (aiming ? 1.2 : 1.0) + (0.3 * breath);
            SetDiameter(Core, size * CoreRadiusRatio * coreScale);
            Core.Opacity = Math.Min(1, lit + 0.2);

            DrawRipples(size, ringRadius, aiming, connected);
        }

        /// <summary>
        /// The direction language: send ripples outward, receive ripples inward, both when the slot declares both.
        /// </summary>
        /// <remarks>
        /// Outward ripples fade as they leave — energy departing. Inward ones brighten as they arrive — energy
        /// landing — so the two read as opposite even in a still frame: one is darkest at its outermost, the other
        /// brightest at its innermost.
        /// </remarks>
        private void DrawRipples(double size, double ringRadius, bool aiming, bool connected)
        {
            bool send = CanSend;
            bool receive = CanReceive;

            RippleOut.Visibility = send ? Visibility.Visible : Visibility.Collapsed;
            RippleIn.Visibility = receive ? Visibility.Visible : Visibility.Collapsed;

            if (!send && !receive)
            {
                return;
            }

            double inner = ringRadius * RippleInnerRatio;
            double outer = (size / 2) - 1;
            if (outer <= inner)
            {
                RippleOut.Visibility = Visibility.Collapsed;
                RippleIn.Visibility = Visibility.Collapsed;
                return;
            }

            double thickness = Math.Max(0.8, size * RippleThicknessRatio);
            double strength = aiming || connected ? 1.0 : 0.6;

            for (int k = 0; k < RippleCount; k++)
            {
                double p = (_sweep + (k / (double)RippleCount)) % 1.0;

                if (send)
                {
                    // 外散：在环上冒出来（12% 的路程升到全亮），越往外越淡
                    double appear = Math.Min(1, p / RippleAttack);
                    RippleOut.StrokeThickness = thickness;
                    SetDiameter(RippleOut, inner + ((outer - inner) * p));
                    RippleOut.Opacity = appear * (1 - p) * 0.7 * strength;
                }

                if (receive)
                {
                    // 内聚：外缘淡、一路加深，收在环上。不做收尾淡出是因为终点与环同半径
                    // （RippleInnerRatio = 1.0）：最后那一下是并进环里而不是凭空消失，所以从
                    // 「最深」跳回「外缘的淡」读起来是新的一波从外面过来，而不是刚才那只环炸掉
                    double arrive = ReceiveOuterAlpha + ((ReceiveInnerAlpha - ReceiveOuterAlpha) * p);
                    RippleIn.StrokeThickness = thickness;
                    SetDiameter(RippleIn, outer - ((outer - inner) * p));
                    RippleIn.Opacity = arrive * strength;
                }
            }
        }

        // Ellipse 在 Grid 里居中，所以半径就是全部位置信息：给直径，别的一概不用管
        private static void SetDiameter(Ellipse ellipse, double radius)
        {
            double diameter = radius * 2;
            ellipse.Width = diameter;
            ellipse.Height = diameter;
        }

        // 端口颜色仍按 SlotState 给：默认白、发送端 Tomato、接收端 Lime、两头都通 Violet。
        // Avalonia 那边是每张卡在自己的 Grid.Styles 里写这四条选择器；WinUI 没有选择器，
        // 而四个圆又共用一支画刷，所以直接算在这里 —— 五张卡的映射就一直是一样的了。
        // （Enum 卡在 Avalonia 只覆盖了 Receiver 一条，其余落到默认白；这里沿用完整映射。）
        private void UpdateGlyphColor()
        {
            var color = SlotState switch
            {
                var state when state.HasFlag(SlotState.Sender) && state.HasFlag(SlotState.Receiver) => Microsoft.UI.Colors.Violet,
                var state when state.HasFlag(SlotState.Sender) => Microsoft.UI.Colors.Tomato,
                var state when state.HasFlag(SlotState.Receiver) => Microsoft.UI.Colors.Lime,
                _ => Microsoft.UI.Colors.White,
            };

            if (_glyphBrush.Color != color)
            {
                _glyphBrush.Color = color;
            }
        }

        #endregion
    }
}
