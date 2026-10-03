// VeloxDev customization: The connection view. The adapter's WorkflowLinkView owns the window-region carving, the
// endpoint subscription and the geometry; this file supplies the palette. Keep SurfaceBackground equal to the
// tree's surface background, or the carved stroke band shows up as a seam.
using System.Drawing;
using System.Windows.Forms;
using VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace Demo.Views.Workflow;

/// <summary>
/// The link view: the adapter's <see cref="WorkflowLinkView"/> with this project's palette.
/// </summary>
public sealed class LinkView : WorkflowLinkView
{
    public LinkView()
    {
        SurfaceBackground = ParseColor("#1E1E1E");
        LineColor = ParseColor("#DDFFFFFF");
        Thickness = float.Parse("2", System.Globalization.CultureInfo.InvariantCulture);
    }
}
