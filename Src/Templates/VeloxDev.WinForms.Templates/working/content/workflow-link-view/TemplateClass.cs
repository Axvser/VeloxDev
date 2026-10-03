// VeloxDev customization: The connection view. The adapter's WorkflowLinkView owns the window-region carving, the
// endpoint subscription and the geometry; this file supplies the palette. Keep SurfaceBackground equal to the
// tree's surface background, or the carved stroke band shows up as a seam.
using System.Drawing;
using System.Windows.Forms;
using VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace TemplateNamespace;

/// <summary>
/// The link view: the adapter's <see cref="WorkflowLinkView"/> with this project's palette.
/// </summary>
public sealed class TemplateClass : WorkflowLinkView
{
    public TemplateClass()
    {
        SurfaceBackground = ParseColor("TemplateSurfaceBackground");
        LineColor = ParseColor("TemplateLinkColor");
        Thickness = float.Parse("TemplateLinkThickness", System.Globalization.CultureInfo.InvariantCulture);
    }
}
