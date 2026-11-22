using System.Drawing;
using System.Drawing.Drawing2D;
using VeloxDev.WinForms.Xaml.Visuals;
using VeloxDev.WorkflowSystem;
using DrawingSize = System.Drawing.Size;

namespace VeloxDev.WorkflowSystem.VisualHosting;

/// <summary>
/// The adapter's fallback slot glyph, used when a surface has no slot template. The slot itself is bound by
/// <see cref="VisualWorkflowSlotBehavior"/>; this class only paints.
/// </summary>
internal sealed class DefaultVisualWorkflowSlotView : VisualElement
{
    /// <summary>The glyph's diameter in design units.</summary>
    internal const float DesignGlyphSize = 18f;

    /// <summary>Creates an unbound slot glyph.</summary>
    internal DefaultVisualWorkflowSlotView()
    {
        IsHitTestVisible = true;
        Width = DesignGlyphSize;
        Height = DesignGlyphSize;
    }

    /// <inheritdoc />
    protected override DrawingSize MeasureOverride(DrawingSize availableSize)
        => new(
            (int)Math.Ceiling(Width),
            (int)Math.Ceiling(Height));

    /// <inheritdoc />
    protected override void OnRender(Graphics graphics)
    {
        if (VisualWorkflowSlotBehavior.GetSlot(this) is not { } slot)
        {
            return;
        }

        var size = Math.Min(Bounds.Width, Bounds.Height);
        if (size <= 1f)
        {
            return;
        }

        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var color = StateColor(slot.State);
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
