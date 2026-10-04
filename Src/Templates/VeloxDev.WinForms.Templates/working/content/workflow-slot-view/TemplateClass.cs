// VeloxDev customization: The slot glyph. This control is yours — how a port looks is your OnPaint. The adapter's
// WorkflowSlotAttachment owns the binding, the state tinting, the SVG parsing, the channel events and the
// drag-to-connect hookup. The path is an SVG path in the 1024x1024 artboard the XAML adapters use, so all seven
// platforms draw the same icon.
using System.Drawing;
using System.Windows.Forms;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace TemplateNamespace;

/// <summary>The slot view: one control that draws one port and participates in drag-to-connect.</summary>
public sealed class TemplateClass : Control
{
    private readonly WorkflowSlotAttachment slot;

    public TemplateClass()
    {
        // Control styles are protected on Control, so they belong in the control — not in the attachment.
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint,
            true);

        // One call attaches the rest. Channel changes reach this control's own events too:
        // slot.ChannelChanging / ChannelChanged.
        slot = WorkflowSlotAttachment.Attach(this);
        slot.SlotPath = "TemplateSlotPath";
        slot.SlotBackground = WorkflowSlotAttachment.ParseColor("TemplateSlotBackground");
        slot.StandbyColor = WorkflowSlotAttachment.ParseColor("TemplateSlotColor");
        slot.BorderColor = WorkflowSlotAttachment.ParseColor("TemplateSlotBorderColor");
    }

    /// <summary>Gets the attachment, for a view that wants the glyph or the channel events.</summary>
    public WorkflowSlotAttachment Attachment => slot;

    /// <summary>Gets or sets the slot this view shows — the pool and the card both bind through it.</summary>
    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public IWorkflowSlotViewModel? ViewModel
    {
        get => slot.Slot;
        set => slot.Slot = value;
    }

    // WinForms has no reliable transparent composition, so the slot erases to its parent's opaque colour instead of
    // declaring SupportsTransparentBackColor and going down the semi-transparent path. Every host panel is opaque,
    // so Clear never sees Color.Transparent (GDI would paint that black).
    /// <inheritdoc />
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        if (e is null) throw new System.ArgumentNullException(nameof(e));

        e.Graphics.Clear(Parent?.BackColor ?? BackColor);
    }

    // VeloxDev customization: the drawing. slot.Paint is the glyph scaled from the artboard; replace it with
    // whatever this project's ports look like. slot.IconPath and slot.GlyphColor are there if you would rather
    // draw it yourself.
    /// <inheritdoc />
    protected override void OnPaint(PaintEventArgs e)
    {
        if (e is null) throw new System.ArgumentNullException(nameof(e));

        base.OnPaint(e);
        slot.Paint(e.Graphics);
    }
}
