// VeloxDev customization: A port's glyph. The adapter's WorkflowSlotView owns the binding, the state tinting and
// the repaint; this file says how big the glyph is and what colour an idle port is. The card places it — the ports'
// positions live in the SlotView layout below, which the surface hit-tests.
using Jalium.UI.Media;
using VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace TemplateNamespace;

/// <summary>
/// A port's glyph, and the layout the cards put their ports at.
/// </summary>
public sealed class TemplateClass : WorkflowSlotView
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

    public TemplateClass()
    {
        StandbyColor = Color.FromArgb(0xDD, 0x1E, 0x1E, 0x1E);
    }
}
