using PlatformInput = Microsoft.AspNetCore.Components.Web;
using Wf = VeloxDev.WorkflowSystem;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

/// <summary>
/// What the browser said a pointer did, before the surface turns it into the standard input Core routes.
/// </summary>
/// <remarks>
/// A DOM-level description, not a Core one: the surface receives <c>mouseenter</c>, <c>mousemove</c>,
/// <c>wheel</c> and their siblings per element, and this is the smallest thing a caller has to name to hand one of
/// them over. <see cref="WorkflowSurfaceBehavior.RoutePointerAsync"/> maps it to the matching
/// <see cref="Wf.PointerEventArgs"/>. It is never the same thing as the old pointer phase enum: that one was
/// part of Core's input surface, this one belongs to the browser boundary.
/// </remarks>
public enum SurfacePointerKind
{
    /// <summary>The pointer arrived over the element.</summary>
    Entered = 0,

    /// <summary>The pointer moved.</summary>
    Moved = 1,

    /// <summary>The pointer left the element.</summary>
    Exited = 2,

    /// <summary>A button went down.</summary>
    Pressed = 3,

    /// <summary>A button came up.</summary>
    Released = 4,

    /// <summary>The wheel turned.</summary>
    Wheel = 5,
}
