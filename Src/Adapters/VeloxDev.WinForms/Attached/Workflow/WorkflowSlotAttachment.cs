using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.CompilerServices;
using System.Windows.Forms;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

/// <summary>
/// Everything a WinForms control needs to become one slot view, attached to it in a single call. The control stays
/// yours — what the port glyph looks like is your <c>OnPaint</c> — and this helper supplies the binding, the
/// state-to-colour reading, the SVG path parsing, the channel events, and the drag-to-connect hookup.
/// </summary>
/// <remarks>
/// <para>
/// Attach it in your view's constructor and give it the glyph and the palette; the control is then an ordinary one
/// that draws its own port:
/// <code>
/// public sealed class SlotView : Control
/// {
///     private readonly WorkflowSlotAttachment slot;
///
///     public SlotView()
///     {
///         slot = WorkflowSlotAttachment.Attach(this);
///         slot.SlotPath = "…";
///         slot.StandbyColor = …;
///     }
///
///     protected override void OnPaint(PaintEventArgs e)
///     {
///         base.OnPaint(e);
///         slot.Paint(e.Graphics);   // 最短的一版；不想这样画，就用 slot.IconPath / slot.GlyphColor 自己画
///     }
/// }
/// </code>
/// </para>
/// <para>
/// The glyph is a path rather than a bitmap so it stays in sync with the XAML adapters' slot views, which draw the
/// same artboard.
/// </para>
/// </remarks>
public sealed class WorkflowSlotAttachment
{
    // 一个控件一份：Attach 幂等，池与宿主都用 For 找它。
    private static readonly ConditionalWeakTable<Control, WorkflowSlotAttachment> Attachments = new();

    private readonly Control target;
    private readonly ModelChangeRelay relay;
    private readonly IWorkflowSlotEventSink eventSink;

    private IWorkflowSlotViewModel? slot;
    private IDisposable? modelEvents;
    private GraphicsPath? iconPath;
    private string slotPath = string.Empty;
    private float pathViewBox = 1024f;
    private Color slotBackground = Color.FromArgb(0x01, 0x00, 0x00, 0x00);
    private Color standbyColor = Color.FromArgb(0xDD, 0x1E, 0x1E, 0x1E);
    private Color borderColor = Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF);

    /// <summary>Attaches the slot machinery to <paramref name="target"/>, or returns the one already attached.</summary>
    /// <param name="target">The control that is going to draw one port.</param>
    /// <returns>The attachment, for chaining the glyph and the palette.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="target"/> is <see langword="null"/>.</exception>
    public static WorkflowSlotAttachment Attach(Control target)
    {
        if (target is null) throw new ArgumentNullException(nameof(target));
        return Attachments.GetValue(target, static control => new WorkflowSlotAttachment(control));
    }

    /// <summary>The attachment on <paramref name="target"/>, or <see langword="null"/> when it has none.</summary>
    /// <param name="target">The control to look up.</param>
    public static WorkflowSlotAttachment? For(Control target)
        => target is not null && Attachments.TryGetValue(target, out var attachment) ? attachment : null;

    private WorkflowSlotAttachment(Control target)
    {
        this.target = target;
        relay = new ModelChangeRelay(target, _ => target.Invalidate());
        eventSink = new SlotEventSink(this);

        // 全名限定：本文件的命名空间嵌在 VeloxDev.WorkflowSystem 里，裸写 `Size` 会解析成模型的那个 Size。
        target.Size = new System.Drawing.Size(20, 20);
        target.Margin = Padding.Empty;
        target.Cursor = Cursors.Hand;
        target.TabStop = false;
        // alpha 强制 255：共享的 slotBackground 默认是 #01000000（近乎透明），而 WinForms 的
        // Control.BackColor 在 A != 0xFF 且未声明 SupportsTransparentBackColor 时会抛。这个底色只作为
        // 「没有父控件」时的兜底（视图自己会把父控件的底色擦上去），所以不透明就够。
        target.BackColor = Opaque(slotBackground);

        // 拖拽连线那一层是适配器的：挂上它，控件才参与手势。SetStyle 是 protected，只能由视图自己设。
        WorkflowSlotConnectionBehavior.SetIsEnabled(target, true);

        target.HandleCreated += (_, _) =>
        {
            // 池可能只设了 Tag 而控件还没句柄：有句柄时补一次绑定。
            if (slot is null && target.Tag is IWorkflowSlotViewModel tagged) Slot = tagged;
        };
        target.Disposed += (_, _) => Detach();
    }

    /// <summary>Raised before the bound slot's channel changes.</summary>
    /// <remarks>Set <see cref="WorkflowEventHandle.PreventDefault"/> on the argument's handle to keep the current channel.</remarks>
    public event EventHandler<SlotChannelEventArgs>? ChannelChanging;

    /// <summary>Raised after the bound slot's channel changed.</summary>
    public event EventHandler<SlotChannelEventArgs>? ChannelChanged;

    /// <summary>Gets or sets the workflow slot bound to this control.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public IWorkflowSlotViewModel? Slot
    {
        get => slot;
        set
        {
            if (ReferenceEquals(slot, value)) return;

            slot = value;
            target.Tag = value;
            target.Visible = value is not null;
            relay.Set(value as INotifyPropertyChanged);

            // 学到所管的插槽这一处接上模型事件；先摘掉上一份订阅再按新模型接。
            modelEvents?.Dispose();
            modelEvents = value is null ? null : WorkflowEventRelay.Attach(value, eventSink);

            target.Invalidate();
        }
    }

    /// <summary>The glyph, as an SVG path in the <see cref="PathViewBox"/> artboard.</summary>
    /// <remarks>Changing it re-parses on the next paint.</remarks>
    public string SlotPath
    {
        get => slotPath;
        set
        {
            if (string.Equals(slotPath, value, StringComparison.Ordinal)) return;
            slotPath = value ?? string.Empty;
            DisposeIconPath();
            target.Invalidate();
        }
    }

    /// <summary>The artboard the <see cref="SlotPath"/> coordinates are expressed in — its SVG viewBox size.</summary>
    public float PathViewBox
    {
        get => pathViewBox;
        set
        {
            if (pathViewBox == value || value <= 0) return;
            pathViewBox = value;
            target.Invalidate();
        }
    }

    /// <summary>Background used when the control has no parent to erase to.</summary>
    public Color SlotBackground
    {
        get => slotBackground;
        set
        {
            if (slotBackground == value) return;
            slotBackground = value;
            target.BackColor = Opaque(value);
            target.Invalidate();
        }
    }

    /// <summary>The glyph's fill while the slot is idle.</summary>
    public Color StandbyColor
    {
        get => standbyColor;
        set
        {
            if (standbyColor == value) return;
            standbyColor = value;
            target.Invalidate();
        }
    }

    /// <summary>The glyph's outline.</summary>
    public Color BorderColor
    {
        get => borderColor;
        set
        {
            if (borderColor == value) return;
            borderColor = value;
            target.Invalidate();
        }
    }

    /// <summary>
    /// The parsed glyph, in the <see cref="PathViewBox"/> artboard — <see langword="null"/> until there is a path.
    /// </summary>
    public GraphicsPath? IconPath => EnsureIconPath();

    /// <summary>
    /// The fill the glyph carries right now: the three state colours are semantics (sender / receiver / both), not
    /// palette, so they are not configurable — <see cref="StandbyColor"/> is what an idle slot gets.
    /// </summary>
    public Color GlyphColor
    {
        get
        {
            if (slot is null) return standbyColor;
            return slot.State switch
            {
                var s when s.HasFlag(SlotState.Sender) && s.HasFlag(SlotState.Receiver) => Color.Violet,
                var s when s.HasFlag(SlotState.Sender) => Color.Tomato,
                var s when s.HasFlag(SlotState.Receiver) => Color.Lime,
                _ => standbyColor,
            };
        }
    }

    /// <summary>
    /// Draws the glyph for <see cref="IconPath"/>, scaled from the artboard into this control — the shortest version
    /// of a port. Call it from your <c>OnPaint</c>, or draw from <see cref="IconPath"/> and <see cref="GlyphColor"/>
    /// yourself.
    /// </summary>
    /// <param name="g">The surface to paint on.</param>
    public void Paint(Graphics g)
    {
        if (g is null) throw new ArgumentNullException(nameof(g));
        if (EnsureIconPath() is not { } icon) return;

        g.SmoothingMode = SmoothingMode.AntiAlias;

        // 把画板缩放进控件，等价于一个 WPF Viewbox。
        var saved = g.Save();
        g.ScaleTransform(target.Width / pathViewBox, target.Height / pathViewBox);

        using var fill = new SolidBrush(GlyphColor);
        using var pen = new Pen(borderColor, 1.5f);
        g.FillPath(fill, icon);
        g.DrawPath(pen, icon);

        g.Restore(saved);
    }

    /// <summary>Parses a <c>#RRGGBB</c>, <c>#AARRGGBB</c> or named colour.</summary>
    /// <param name="hex">The colour text.</param>
    /// <returns>The colour.</returns>
    public static Color ParseColor(string hex) => WorkflowSurfaceColors.Parse(hex);

    /// <summary>
    /// The opaque version of a colour — WinForms has no reliable transparent composition, so a view erases itself to
    /// its parent's colour instead of declaring <c>SupportsTransparentBackColor</c>.
    /// </summary>
    /// <param name="color">The colour to force opaque.</param>
    /// <returns>The same colour with alpha 255.</returns>
    public static Color Opaque(Color color) => Color.FromArgb(255, color);

    private GraphicsPath? EnsureIconPath()
    {
        if (iconPath is not null) return iconPath;
        if (slotPath.Length == 0) return null;

        iconPath = SvgPathParser.BuildPath(slotPath);
        return iconPath;
    }

    private void DisposeIconPath()
    {
        iconPath?.Dispose();
        iconPath = null;
    }

    private void Detach()
    {
        relay.Clear();
        modelEvents?.Dispose();
        modelEvents = null;
        DisposeIconPath();
    }

    // 助手自己接模型事件、自己摘订阅，把每条转成事件；视图只订事件，不碰 Helper。
    private sealed class SlotEventSink(WorkflowSlotAttachment owner) : IWorkflowSlotEventSink
    {
        public void OnChannelChanging(SlotChannelEventArgs e) => owner.ChannelChanging?.Invoke(owner, e);

        public void OnChannelChanged(SlotChannelEventArgs e) => owner.ChannelChanged?.Invoke(owner, e);
    }
}
