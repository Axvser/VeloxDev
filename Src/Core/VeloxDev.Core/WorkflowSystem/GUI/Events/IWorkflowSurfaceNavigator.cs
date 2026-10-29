namespace VeloxDev.WorkflowSystem;

/// <summary>
/// Scrolls the surface a tree is drawn on so a world point sits at the viewport center.
/// </summary>
/// <remarks>
/// <para>
/// The absolute companion of <see cref="IWorkflowSurfaceScroller"/>: that one moves the surface by a wheel
/// delta, this one takes a target. An adapter registers it while its surface is attached, so a host can locate
/// a node, slot or link without knowing the platform's scroll API.
/// </para>
/// <para>
/// Coordinates use the same frame as <see cref="IWorkflowNodeViewModel.Anchor"/> and
/// <see cref="IWorkflowTreeViewModelHelper.Viewport"/>; <see cref="StandardEx.WorkflowNavigationEx"/> produces
/// them from an element. A target outside the current scrollable extent may grow the surface (the adapter
/// decides — six of the seven surfaces extend it, the same way their wheel overscroll does; WinForms has no
/// extent ceiling because its pan engine accepts signed targets). An implementation that cannot grow clamps
/// instead, so the point lands as close to the center as it allows.
/// </para>
/// </remarks>
/// <seealso cref="WorkflowInput.Navigator"/>
/// <seealso cref="StandardEx.WorkflowNavigationEx"/>
public interface IWorkflowSurfaceNavigator
{
    /// <summary>
    /// Scrolls so the world point (<paramref name="worldX"/>, <paramref name="worldY"/>) is at the viewport center.
    /// </summary>
    /// <param name="worldX">Horizontal world coordinate.</param>
    /// <param name="worldY">Vertical world coordinate.</param>
    void CenterOn(double worldX, double worldY);
}
