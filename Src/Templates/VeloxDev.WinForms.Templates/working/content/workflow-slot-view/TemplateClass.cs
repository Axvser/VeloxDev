// VeloxDev customization: The slot glyph. The adapter's WorkflowSlotView owns the binding, the paint, the state
// tinting and the SVG parsing; this file supplies the glyph and the colours. The path is an SVG path in the
// 1024x1024 artboard the XAML adapters use, so all seven platforms draw the same icon.
using System.Drawing;
using System.Windows.Forms;
using VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace TemplateNamespace;

/// <summary>
/// The slot view: the adapter's <see cref="WorkflowSlotView"/> with this project's glyph and palette.
/// </summary>
public sealed class TemplateClass : WorkflowSlotView
{
    public TemplateClass()
    {
        SlotPath = "TemplateSlotPath";
        SlotBackground = ParseColor("TemplateSlotBackground");
        StandbyColor = ParseColor("TemplateSlotColor");
        BorderColor = ParseColor("TemplateSlotBorderColor");
    }
}
