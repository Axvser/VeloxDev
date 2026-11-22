using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using VeloxDev.WinForms.Xaml.Visuals;
using VeloxDev.WorkflowSystem;
using DrawingSize = System.Drawing.Size;
using Wf = VeloxDev.WorkflowSystem;

namespace VeloxDev.WorkflowSystem.VisualHosting;

/// <summary>
/// A small visual glyph for one workflow slot: a ring and a core whose colour reflects the slot's connection state.
/// </summary>
/// <remarks>
/// The glyph is hit-testable, so a press that lands on it resolves to the slot rather than to the owning node. The
/// ripple language of the demo card is deliberately not reproduced here; this is the adapter's neutral default.
/// Derive and override <see cref="OnRender"/> to supply a host's own glyph.
/// </remarks>
public class VisualWorkflowSlotView : VisualElement
{
    /// <summary>The glyph's diameter at scale one, in pixels.</summary>
    public const float DefaultGlyphSize = 12f;

    private ISlotComponent? _slot;
    private INotifyPropertyChanged? _notifier;

    /// <summary>Creates an unbound slot glyph.</summary>
    public VisualWorkflowSlotView()
    {
        IsHitTestVisible = true;
        Width = DefaultGlyphSize;
        Height = DefaultGlyphSize;
    }

    /// <summary>The slot drawn by this glyph, or <see langword="null"/>.</summary>
    public ISlotComponent? Slot
    {
        get => _slot;
        set
        {
            if (ReferenceEquals(_slot, value))
            {
                return;
            }

            if (_notifier is not null)
            {
                _notifier.PropertyChanged -= OnSlotPropertyChanged;
                _notifier = null;
            }

            _slot = value;
            if (_slot is INotifyPropertyChanged notifier)
            {
                _notifier = notifier;
                _notifier.PropertyChanged += OnSlotPropertyChanged;
            }

            InvalidateVisual();
        }
    }

    /// <inheritdoc />
    protected override DrawingSize MeasureOverride(DrawingSize availableSize)
        => new(
            (int)Math.Ceiling(Width),
            (int)Math.Ceiling(Height));

    /// <inheritdoc />
    protected override void OnRender(Graphics graphics)
    {
        if (_slot is null)
        {
            return;
        }

        var size = Math.Min(Bounds.Width, Bounds.Height);
        if (size <= 1f)
        {
            return;
        }

        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var color = StateColor(_slot.State);
        var centerX = Bounds.Width / 2f;
        var centerY = Bounds.Height / 2f;
        var ringRadius = size * 0.30f;
        var coreRadius = size * 0.13f;

        using var ring = new Pen(color, Math.Max(1f, size * 0.08f));
        graphics.DrawEllipse(
            ring,
            centerX - ringRadius,
            centerY - ringRadius,
            ringRadius * 2f,
            ringRadius * 2f);

        using var core = new SolidBrush(color);
        graphics.FillEllipse(
            core,
            centerX - coreRadius,
            centerY - coreRadius,
            coreRadius * 2f,
            coreRadius * 2f);
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing && _notifier is not null)
        {
            _notifier.PropertyChanged -= OnSlotPropertyChanged;
            _notifier = null;
        }

        base.Dispose(disposing);
    }

    private void OnSlotPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ISlotComponent.State)
            or nameof(ISlotComponent.Channel)
            or nameof(ISlotComponent.Anchor))
        {
            InvalidateVisual();
        }
    }

    private static Color StateColor(SlotState state)
    {
        if (state.HasFlag(SlotState.Sender) && state.HasFlag(SlotState.Receiver))
        {
            return Color.Violet;
        }

        if (state.HasFlag(SlotState.Sender) || state.HasFlag(SlotState.PreviewSender))
        {
            return Color.Tomato;
        }

        if (state.HasFlag(SlotState.Receiver) || state.HasFlag(SlotState.PreviewReceiver))
        {
            return Color.Lime;
        }

        return Color.FromArgb(0xCC, 0xD0, 0xD0, 0xD0);
    }
}
