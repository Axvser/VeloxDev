using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Jalium.UI;
using Jalium.UI.Media;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

/// <summary>
/// Everything a Jalium element needs to become one slot view, attached to it in a single call. The element stays
/// yours — what the port glyph looks like is your <c>OnRender</c> — and this helper supplies the binding, the
/// state-to-brush reading, the size it occupies, and the channel events.
/// </summary>
/// <remarks>
/// <para>
/// Attach it in your view's constructor and give it the size and the palette; the element is then an ordinary one
/// that draws its own port:
/// <code>
/// public sealed class SlotView : FrameworkElement
/// {
///     private readonly WorkflowSlotAttachment slot;
///
///     public SlotView()
///     {
///         slot = WorkflowSlotAttachment.Attach(this);
///         slot.Radius = 7;
///     }
///
///     protected override void OnRender(DrawingContext dc)
///     {
///         base.OnRender(dc);
///         slot.Paint(dc);   // 最短的一版；不想这样画，就用 slot.Brush / slot.StandbyColor 自己画
///     }
/// }
/// </code>
/// </para>
/// <para>
/// The host positions the element at the port centre, so the glyph needs no geometry of its own. The three state
/// colours are semantic — a port that can send, one that can receive, and one that can do both — so they are fixed
/// rather than part of a palette. <see cref="StandbyColor"/> covers the idle slot, which is a style choice.
/// </para>
/// </remarks>
public sealed class WorkflowSlotAttachment
{
    // 一个元素一份：Attach 幂等，池与宿主都用 For 找它。
    private static readonly ConditionalWeakTable<FrameworkElement, WorkflowSlotAttachment> Attachments = new();

    private static readonly Brush SenderBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x63, 0x47));
    private static readonly Brush ReceiverBrush = new SolidColorBrush(Color.FromRgb(0x32, 0xCD, 0x32));
    private static readonly Brush BothBrush = new SolidColorBrush(Color.FromRgb(0xEE, 0x82, 0xEE));

    private readonly FrameworkElement target;

    private IWorkflowSlotViewModel? slot;
    private IDisposable? slotEvents;
    private SlotEventSink? eventSink;
    private Color standbyColor = Color.FromArgb(0xDD, 0x1E, 0x1E, 0x1E);
    private Color borderColor = Color.FromArgb(0xFF, 0x00, 0x00, 0x00);
    private double radius = 7;
    private double borderThickness;

    /// <summary>Attaches the slot machinery to <paramref name="target"/>, or returns the one already attached.</summary>
    /// <param name="target">The element that is going to draw one port.</param>
    /// <returns>The attachment, for chaining the size and the palette.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="target"/> is <see langword="null"/>.</exception>
    public static WorkflowSlotAttachment Attach(FrameworkElement target)
    {
        if (target is null) throw new ArgumentNullException(nameof(target));
        return Attachments.GetValue(target, static element => new WorkflowSlotAttachment(element));
    }

    /// <summary>The attachment on <paramref name="target"/>, or <see langword="null"/> when it has none.</summary>
    /// <param name="target">The element to look up.</param>
    public static WorkflowSlotAttachment? For(FrameworkElement target)
        => target is not null && Attachments.TryGetValue(target, out var attachment) ? attachment : null;

    private WorkflowSlotAttachment(FrameworkElement target)
    {
        this.target = target;
        target.DataContextChanged += OnDataContextChanged;
    }

    /// <summary>Raised before the slot's channel changes; refuse that change via the argument's handle.</summary>
    /// <param name="e">The change that is about to happen.</param>
    public event EventHandler<SlotChannelEventArgs>? ChannelChanging;

    /// <summary>Raised after the slot's channel changed.</summary>
    /// <param name="e">The change that happened.</param>
    public event EventHandler<SlotChannelEventArgs>? ChannelChanged;

    /// <summary>The glyph's radius, in the card's design coordinates.</summary>
    public double Radius
    {
        get => radius;
        set
        {
            if (radius == value) return;
            radius = value;
            ResizeToGlyph();
            target.InvalidateVisual();
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
            target.InvalidateVisual();
        }
    }

    /// <summary>The glyph's outline colour. Only drawn when <see cref="BorderThickness"/> is above zero.</summary>
    public Color BorderColor
    {
        get => borderColor;
        set
        {
            if (borderColor == value) return;
            borderColor = value;
            target.InvalidateVisual();
        }
    }

    /// <summary>The glyph's outline width. Zero (the default) draws no outline.</summary>
    public double BorderThickness
    {
        get => borderThickness;
        set
        {
            if (borderThickness == value) return;
            borderThickness = value;
            target.InvalidateVisual();
        }
    }

    /// <summary>The slot this glyph is showing, taken from the <c>DataContext</c>.</summary>
    public IWorkflowSlotViewModel? Slot => slot;

    /// <summary>The fill the glyph carries right now: the three state colours are semantics, so they are fixed.</summary>
    public Brush Brush
    {
        get
        {
            if (slot is null) return new SolidColorBrush(standbyColor);

            var state = slot.State;
            var sender = state.HasFlag(SlotState.Sender);
            var receiver = state.HasFlag(SlotState.Receiver);
            if (sender && receiver) return BothBrush;
            if (sender) return SenderBrush;
            if (receiver) return ReceiverBrush;
            return new SolidColorBrush(standbyColor);
        }
    }

    /// <summary>
    /// Draws the glyph — a filled circle tinted by the slot's state — centred on the element. Call it from your
    /// <c>OnRender</c>, or draw from <see cref="Brush"/> and <see cref="Radius"/> yourself.
    /// </summary>
    /// <param name="dc">The drawing context.</param>
    public void Paint(DrawingContext dc)
    {
        if (dc is null) throw new ArgumentNullException(nameof(dc));

        var pen = borderThickness > 0 ? new Pen(new SolidColorBrush(borderColor), borderThickness) : null;
        dc.DrawEllipse(Brush, pen, new Point(radius, radius), radius, radius);
    }

    // 视图换宿主时复用同一个 sink；sink 只把调用转给事件，不持有额外状态。
    private SlotEventSink EventSink => eventSink ??= new SlotEventSink(this);

    private void OnDataContextChanged(object? sender, DependencyPropertyChangedEventArgs e)
    {
        if (slot is INotifyPropertyChanged old) old.PropertyChanged -= OnSlotChanged;
        slotEvents?.Dispose();
        slotEvents = null;

        slot = target.DataContext as IWorkflowSlotViewModel;

        if (slot is INotifyPropertyChanged notify) notify.PropertyChanged += OnSlotChanged;

        // 模型事件由 Core 的 relay 接一次，转发到事件；Helper 不提供事件时 Attach 返回 null。
        slotEvents = slot is null ? null : WorkflowEventRelay.Attach(slot, EventSink);

        ResizeToGlyph();
        target.InvalidateVisual();
    }

    // 端口的状态变了（Core 的 UpdateState 在连线建立/删除时改它）就要重画，颜色才跟得上。
    private void OnSlotChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IWorkflowSlotViewModel.State))
        {
            target.InvalidateVisual();
        }
    }

    private void ResizeToGlyph()
    {
        target.Width = radius * 2;
        target.Height = radius * 2;
    }

    // relay 的宿主：把每个 sink 方法原样转给对应的事件。
    private sealed class SlotEventSink(WorkflowSlotAttachment owner) : IWorkflowSlotEventSink
    {
        public void OnChannelChanging(SlotChannelEventArgs e) => owner.ChannelChanging?.Invoke(owner, e);

        public void OnChannelChanged(SlotChannelEventArgs e) => owner.ChannelChanged?.Invoke(owner, e);
    }
}
