// VeloxDev customization: The slot glyph. This control is yours — how a port looks is your OnPaint. The adapter's
// WorkflowSlotAttachment owns the binding, the state tinting, the SVG parsing, the channel events and the
// drag-to-connect hookup. The path is an SVG path in the 1024x1024 artboard the XAML adapters use, so all seven
// platforms draw the same icon.
using System.Drawing;
using System.Windows.Forms;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace Demo.Views.Workflow;

/// <summary>The slot view: one control that draws one port and participates in drag-to-connect.</summary>
public sealed class SlotView : Control
{
    private readonly WorkflowSlotAttachment slot;

    public SlotView()
    {
        // Control styles are protected on Control, so they belong in the control — not in the attachment.
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint,
            true);

        // One call attaches the rest: binding, state tinting, SVG parsing, drag-to-connect, channel events.
        slot = WorkflowSlotAttachment.Attach(this);
        slot.SlotPath = "M517.3248,511.488 m-123.6992,0 a123.6992,123.6992 0 1 0 247.3984,0 a123.6992,123.6992 0 1 0 -247.3984,0 Z M366.848,991.5904 a47.2064,47.2064 0 0 1 -15.36,-2.5088 A506.368,506.368 0 0 1 32.8704,655.36 a46.08,46.08 0 1 1 88.32,-26.2144 A414.0544,414.0544 0 0 0 383.9616,928.2048 a46.08,46.08 0 0 1 -15.104,89.6 Z M648.2944,997.888 a46.08,46.08 0 0 1 -13.1072,-90.2656 A413.952,413.952 0 0 0 920.9344,646.8608 a46.08,46.08 0 1 1 87.04,30.208 A506.3168,506.3168 0 0 1 674.5088,996.9408 a45.2608,45.2608 0 0 1 -13.1072,1.9456 Z M957.44,426.5984 a46.08,46.08 0 0 1 -44.1344,-32.9728 A414.0544,414.0544 0 0 0 652.544,120.9728 a46.08,46.08 0 1 1 30.1568,-87.04 A506.368,506.368 0 0 1 991.6416,467.3984 a46.08,46.08 0 0 1 -31.0272,57.2928 a45.2608,45.2608 0 0 1 -13.1584,1.8944 Z M83.3024,407.0912 a46.08,46.08 0 0 1 -43.5712,-61.44 A506.4704,506.4704 0 0 1 373.248,26.9824 a46.08,46.08 0 1 1 26.112,88.3712 A413.952,413.952 0 0 0 100.7104,367.0528 a46.08,46.08 0 0 1 -43.52,31.0272 Z";
        slot.SlotBackground = WorkflowSlotAttachment.ParseColor("#01000000");
        slot.StandbyColor = WorkflowSlotAttachment.ParseColor("#DD1E1E1E");
        slot.BorderColor = WorkflowSlotAttachment.ParseColor("#FFFFFFFF");
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
    // declaring SupportsTransparentBackColor and going down the semi-transparent path.
    /// <inheritdoc />
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        if (e is null) throw new System.ArgumentNullException(nameof(e));

        e.Graphics.Clear(Parent?.BackColor ?? BackColor);
    }

    /// <inheritdoc />
    protected override void OnPaint(PaintEventArgs e)
    {
        if (e is null) throw new System.ArgumentNullException(nameof(e));

        base.OnPaint(e);
        slot.Paint(e.Graphics);
    }
}
