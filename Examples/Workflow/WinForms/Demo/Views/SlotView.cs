using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using VeloxDev.TransitionSystem;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.AttachedBehaviors;
// `Size` collides between System.Drawing and VeloxDev.WorkflowSystem; a drawing
// alias keeps `new Size(24, 24)` unambiguous in generated code.
using Size = System.Drawing.Size;

namespace Demo.Views;

/// <summary>
/// The port on a node: a fine ring with a small core, and ripples that say which way data goes.
/// <para>
/// The direction is carried by the <b>shape of the motion</b> rather than by a colour or a rotating token:
/// ripples spread <b>outward</b> when the slot can send, <b>inward</b> when it can receive, and both ways at
/// once when it can do either. That comes from <see cref="IWorkflowSlotViewModel.Channel"/> — the slot's
/// declared capacity — and not from what it happens to be connected to, so a port states what it is for even
/// with nothing wired to it.
/// </para>
/// <para>
/// One clock drives everything. <see cref="Transition.Exit(object, bool, bool)"/> is per-target, so two
/// independent loops on one control would stop each other; the aim-breath is therefore <i>derived</i> from the
/// same <see cref="Sweep"/> that moves the ripples rather than being a loop of its own.
/// </para>
/// <para>
/// <b>Clipping.</b> WinForms clips a child window to its parent's client rectangle, so a port that rides the
/// card's edge — the design asks for its centre to sit exactly on that edge — would lose the half that spills
/// outside. That half is not drawn here at all: <see cref="RenderGlyph"/> is public to the assembly so the
/// canvas, which owns the area outside the card, draws the same glyph at the same centre and the two halves
/// meet at the card's edge. Only the visible result has to be a whole port; which window paints which half
/// does not.
/// </para>
/// </summary>
public sealed class SlotView : Control
{
    // 字形按控件尺寸等比缩放。这三个比例都比原来细一半以上：0.205 / 0.045 / 0.06
    private const double RingRadiusRatio = 0.205;
    private const double RingThicknessRatio = 0.045;
    private const double CoreRadiusRatio = 0.06;

    // 波纹的终点半径就是环本身（1.0×）。从前内圈取 1.25×，内聚波纹于是停在环外一截再凭空消失 ——
    // 看着像没走到就被掐了
    private const double RippleInnerRatio = 1.0;
    private const double RippleThicknessRatio = 0.05;

    // 外散的出现段：用 12% 的路程升起来，不凭空冒出。内聚不走这条 —— 它从外缘的淡色开始，
    // 本来就不需要「出现」（见 DrawRipples）
    private const double RippleAttack = 0.12;

    // 内聚的两端透明度：外缘淡、环上实。它一路加深到底，不做收尾淡出
    private const double ReceiveOuterAlpha = 0.15;
    private const double ReceiveInnerAlpha = 0.90;

    // 慢到眼睛能跟住一只环走完全程
    private static readonly TimeSpan SweepPeriod = TimeSpan.FromMilliseconds(2300);

    /// <summary>
    /// The clock. One repeating chain on one property — everything the port draws is a function of it.
    /// <para>
    /// <c>LoopTime = int.MaxValue</c> is the only "forever" this system has, and the duration must not be zero:
    /// a zero-length pass consumes no time, so a forever loop over it spins instead of looping and
    /// <see cref="Transition.Exit(object, bool, bool)"/> can no longer interrupt it.
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

    private IWorkflowSlotViewModel? _slot;
    private INotifyPropertyChanged? _notifier;
    private bool _running;
    private bool _hover;

    public SlotView()
    {
        Size = new Size(24, 24);
        Margin = Padding.Empty;
        Cursor = Cursors.Hand;
        TabStop = false;
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint,
            true);
        BackColor = CardTheme.Surface;
        WorkflowSlotConnectionBehavior.SetIsEnabled(this, true);
    }

    /// <summary>
    /// The port's size at scale 1. The node card keeps this so a zoom step can re-size the port from its design
    /// size (32 for a port on the card's edge, 24 for one inside a port strip) instead of from the last size it
    /// happened to have — a round trip to a tiny zoom must not drift.
    /// </summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal int DesignSize { get; set; } = 24;

    /// <summary>
    /// Called when something this port draws has changed and its own window is not the only thing that shows
    /// it. A port on the card's edge has half its glyph drawn by the canvas (see the class remarks), so its
    /// frames and its state changes have to reach the canvas too — the port cannot invalidate it on its own:
    /// it is a renderer-less child, and the surface that can answer a repaint is the canvas.
    /// </summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Action? ExternalInvalidate { get; set; }

    /// <summary>Gets or sets the workflow slot bound to this view.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public IWorkflowSlotViewModel? ViewModel
    {
        get => _slot;
        set
        {
            if (ReferenceEquals(_slot, value)) return;

            if (_notifier is not null)
            {
                _notifier.PropertyChanged -= OnSlotChanged;
                _notifier = null;
            }

            _slot = value;
            Tag = value;
            Visible = value is not null;

            if (value is INotifyPropertyChanged n)
            {
                _notifier = n;
                n.PropertyChanged += OnSlotChanged;
            }

            Sync();
            Repaint();
        }
    }

    /// <summary>The clock's phase, 0 to 1, repeating. Animated, never set by a caller.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double Sweep
    {
        get => _sweep;
        set
        {
            _sweep = value;
            Repaint();
        }
    }

    private double _sweep;

    #region State

    /// <summary>Whether the slot may connect outward — a target direction is declared.</summary>
    private bool CanSend =>
        _slot is not null &&
        (((_slot.Channel & SlotChannel.OneTarget) != 0) || ((_slot.Channel & SlotChannel.MultipleTargets) != 0));

    /// <summary>Whether the slot may be connected inward — a source direction is declared.</summary>
    private bool CanReceive =>
        _slot is not null &&
        (((_slot.Channel & SlotChannel.OneSource) != 0) || ((_slot.Channel & SlotChannel.MultipleSources) != 0));

    private SlotState State => _slot?.State ?? SlotState.StandBy;

    private bool IsAiming =>
        State.HasFlag(SlotState.PreviewSender) || State.HasFlag(SlotState.PreviewReceiver);

    private bool IsConnected =>
        State.HasFlag(SlotState.Sender) || State.HasFlag(SlotState.Receiver);

    /// <summary>
    /// The port's colour, from the slot's connection state. The reference states this as style selectors on the
    /// slot's <c>Foreground</c>; the states are the same three plus the un-lit stand-by.
    /// </summary>
    private Color StateColor()
    {
        var state = State;
        if (state.HasFlag(SlotState.Sender) && state.HasFlag(SlotState.Receiver)) return Color.Violet;
        if (state.HasFlag(SlotState.Sender)) return Color.Tomato;
        if (state.HasFlag(SlotState.Receiver)) return Color.Lime;
        return Color.White;
    }

    // 只要端口声明了任一方向，波纹就该跑；没有方向的端口是一条死线，挂上去只是白烧 CPU
    private void Sync()
    {
        bool ripples = CanSend || CanReceive;

        if (ripples && !_running && IsHandleCreated)
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

    // 句柄之前起动画会被宿主静默拒绝（Awake 被拒即结束），所以等到这块端口成为窗口再起；
    // 句柄销毁时停，否则池化复用的端口会留着一条对无人可见的控件写 Sweep 的采样循环
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (_slot is null && Tag is IWorkflowSlotViewModel tagged)
        {
            ViewModel = tagged;
        }

        Sync();
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        Stop();
        base.OnHandleDestroyed(e);
    }

    // 悬停要亮一档：端口小，没有反馈就不知道指针到底有没有搭上它
    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        _hover = true;
        Repaint();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hover = false;
        Repaint();
    }

    private void OnSlotChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new PropertyChangedEventHandler(OnSlotChanged), sender, e);
            return;
        }

        Sync();
        Repaint();
    }

    private void Repaint()
    {
        Invalidate();
        ExternalInvalidate?.Invoke();
    }

    #endregion

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        // The card is a single dark surface, so the port can simply clear to it instead of reading the parent's
        // BackColor: the port strips inside the Python and Enum cards sit on panels that are the same colour,
        // and a port on the card's edge sits on the body of the card.
        e.Graphics.Clear(CardTheme.Surface);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var size = Math.Min(Width, Height);
        RenderGlyph(e.Graphics, Width / 2f, Height / 2f, size);
    }

    /// <summary>
    /// Draws the whole port glyph centred on the given point. Called by <see cref="OnPaint"/> for the half that
    /// falls inside the port's own window, and by the canvas for the half that spills past the card's edge.
    /// </summary>
    internal void RenderGlyph(Graphics g, float centerX, float centerY, float size)
    {
        if (size <= 2 || _slot is null) return;

        var color = StateColor();
        double ringRadius = size * RingRadiusRatio;
        bool aiming = IsAiming;
        bool connected = IsConnected;

        // 呼吸从这个时钟推导：`sin` 的相位就是 Sweep，所以它和波纹天然同步，
        // 也不需要第二条动画（见类注释：Exit 是按目标停的，两条会互相打断）
        double breath = aiming ? (0.5 + (0.5 * Math.Sin(Sweep * Math.PI * 2))) : 0;

        DrawRipples(g, centerX, centerY, size, ringRadius, color, aiming, connected);

        // 环与芯：静息时压暗，被瞄准或已连上时全亮。呼吸推的是同一个亮度，所以它一定看得见
        double lit = (aiming || connected ? 0.75 : 0.45) + (0.25 * breath) + (_hover ? 0.2 : 0);
        var ringPen = new Pen(Fade(color, Math.Min(1, lit)), (float)(size * RingThicknessRatio));
        g.DrawEllipse(ringPen, centerX - (float)ringRadius, centerY - (float)ringRadius,
            (float)(ringRadius * 2), (float)(ringRadius * 2));
        ringPen.Dispose();

        double coreScale = (aiming ? 1.2 : 1.0) + (0.3 * breath);
        double core = size * CoreRadiusRatio * coreScale;
        using var coreBrush = new SolidBrush(Fade(color, Math.Min(1, lit + 0.2)));
        g.FillEllipse(coreBrush, centerX - (float)core, centerY - (float)core, (float)(core * 2), (float)(core * 2));
    }

    /// <summary>
    /// The direction language: send ripples outward, receive ripples inward, both when the slot declares both.
    /// </summary>
    /// <remarks>
    /// Outward ripples fade as they leave — energy departing. Inward ones brighten as they arrive — energy
    /// landing — so the two read as opposite even in a still frame: one is darkest at its outermost, the other
    /// brightest at its innermost.
    /// </remarks>
    private void DrawRipples(Graphics g, float centerX, float centerY, float size, double ringRadius, Color color,
        bool aiming, bool connected)
    {
        if (!CanSend && !CanReceive) return;

        double inner = ringRadius * RippleInnerRatio;
        double outer = (size / 2) - 1;
        if (outer <= inner) return;

        double thickness = Math.Max(0.8, size * RippleThicknessRatio);
        double strength = aiming || connected ? 1.0 : 0.6;

        // 一圈上只画一道波纹。两道虽然更「连续」，但同屏永远有两只环挂在固定的内外半径之间，
        // 读起来像一个静止的靶心 —— 眼睛抓不住单个环，方向也就读不出来。一道走完再起一道，
        // 周期之间那点空档反而让「一波一波」这件事看得更清楚。
        double p = Sweep % 1.0;
        if (p < 0) p += 1.0;

        if (CanSend)
        {
            // 外散：在环上冒出来（12% 的路程升到全亮），越往外越淡
            double appear = Math.Min(1, p / RippleAttack);
            double r = inner + ((outer - inner) * p);
            using var pen = new Pen(Fade(color, appear * (1 - p) * 0.7 * strength), (float)thickness);
            g.DrawEllipse(pen, centerX - (float)r, centerY - (float)r, (float)(r * 2), (float)(r * 2));
        }

        if (CanReceive)
        {
            // 内聚：外缘淡、一路加深，收在环上。
            // 不做收尾淡出是因为终点与环同半径（RippleInnerRatio = 1.0）：最后那一下是并进环里
            // 而不是凭空消失，所以从「最深」跳回「外缘的淡」读起来是新的一波从外面过来，
            // 而不是刚才那只环炸掉
            double arrive = ReceiveOuterAlpha + ((ReceiveInnerAlpha - ReceiveOuterAlpha) * p);
            double r = outer - ((outer - inner) * p);
            using var pen = new Pen(Fade(color, arrive * strength), (float)thickness);
            g.DrawEllipse(pen, centerX - (float)r, centerY - (float)r, (float)(r * 2), (float)(r * 2));
        }
    }

    /// <summary>Same hue, given opacity. GDI+ colours carry alpha as a byte, the design's values are 0..1.</summary>
    private static Color Fade(Color color, double opacity)
        => Color.FromArgb((int)Math.Round(Math.Clamp(opacity, 0, 1) * 255), color.R, color.G, color.B);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Stop();
            if (_notifier is not null)
            {
                _notifier.PropertyChanged -= OnSlotChanged;
                _notifier = null;
            }
        }

        base.Dispose(disposing);
    }
}
