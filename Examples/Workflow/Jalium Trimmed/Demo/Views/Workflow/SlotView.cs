// VeloxDev customization: A port's glyph. This element is yours — how a port looks is your OnRender. The adapter's
// WorkflowSlotAttachment owns the binding, the state tinting, the size the card asks for, and the channel events.
// The card places it; the ports' positions live in the layout below, which the surface hit-tests.
using Jalium.UI;
using Jalium.UI.Media;
using VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace Demo.Views.Workflow;

/// <summary>
/// A port's glyph, and the layout the cards put their ports at.
/// </summary>
public sealed class SlotView : FrameworkElement
{
    /// <summary>Where this project's cards put their ports, in design (scale-1) coordinates.</summary>
    /// <remarks>
    /// The surface, the cards and the link views all read this one instance — assign it to each of them, or they
    /// will disagree about where the ports are.
    /// </remarks>
    public static readonly WorkflowPortLayout Layout = new()
    {
        DesignWidth = 260,
        DesignHeight = 180,
        TitleBarH = 36,
        RowH = 26,
        InputPortX = 10,
        OutputInset = 15,
        InputPortRadius = 9,
        OutputPortRadius = 7,
    };

    private readonly WorkflowSlotAttachment slot;

    public SlotView()
    {
        // One call attaches the rest: binding (from the DataContext the card sets), state tinting, sizing, events.
        slot = WorkflowSlotAttachment.Attach(this);
        slot.StandbyColor = Color.FromArgb(0xDD, 0x1E, 0x1E, 0x1E);
    }

    /// <summary>Gets the attachment, for a view that wants the brush or the channel events.</summary>
    public WorkflowSlotAttachment Attachment => slot;

    // VeloxDev customization: the drawing. slot.Paint is a circle tinted by the slot's state; replace it with
    // whatever this project's ports look like. slot.Brush and slot.Radius are there if you would rather draw it
    // yourself.
    /// <inheritdoc />
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        slot.Paint(dc);
    }
}
