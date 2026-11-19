using System.Drawing;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

/// <summary>
/// A node card that <see cref="WorkflowTreeView"/> re-places on every pan.
/// </summary>
/// <remarks>
/// The surface cannot name the card type — node views are generated into the host's own assembly and
/// namespace — so it hands the card the projection and lets the card place itself. A link view does not
/// implement this: its geometry is rebuilt from its endpoints, and those move when the slot-layout behaviour
/// re-measures them after the cards have been placed.
/// </remarks>
public interface IWorkflowSurfaceNodeView
{
    /// <summary>Puts the card where the surface's current world origin places it.</summary>
    /// <param name="panOffset">The surface's signed pan translation, in pixels.</param>
    /// <param name="contentOffset">
    /// The world origin the surface is drawing at: the layout's offset plus the surface's ruler reserve, so the
    /// card needs no inset of its own.
    /// </param>
    void ApplySurfacePosition(Point panOffset, Offset contentOffset);
}
