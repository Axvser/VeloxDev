namespace VeloxDev.WorkflowSystem;

/// <summary>The coordinate space an <see cref="ISlotPart"/> reports a measured center in.</summary>
/// <remarks>
/// Each adapter reaches a slot center through a different platform transform, and that transform decides which
/// offset <see cref="WorkflowSurfaceMath"/> has to apply afterwards. Naming the space keeps a portable adapter from
/// silently landing every port a uniform offset away from where it belongs.
/// </remarks>
public enum SlotCoordinateSpace
{
    /// <summary>
    /// The center is relative to the node view. The anchor is the node's anchor plus that center
    /// (<see cref="WorkflowSurfaceMath.SlotAnchorFromNode"/>).
    /// </summary>
    NodeLocal,

    /// <summary>
    /// The center is already canvas/world space, because the platform transform inverted the canvas translation.
    /// The anchor is the center unchanged (<see cref="WorkflowSurfaceMath.SlotAnchorFromCanvasLocal"/>).
    /// </summary>
    CanvasLocal,

    /// <summary>
    /// The center is in scroll space and still includes <see cref="CanvasLayout.ActualOffset"/>. The anchor subtracts
    /// it (<see cref="WorkflowSurfaceMath.SlotAnchorFromVisualCenter"/>).
    /// </summary>
    ScrollSpace,
}
