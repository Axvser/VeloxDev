using System;

namespace VeloxDev.WorkflowSystem;

/// <summary>
/// Owns the viewport-restore state machine: it captures a newly attached tree's saved viewport once, schedules
/// one deferred restore, and applies it once the platform can accept the offsets.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Capture"/> must run <b>before</b> the surface writes its current viewport back to
/// <see cref="CanvasLayout.ViewportOffset"/>; otherwise that write overwrites the saved position with the
/// origin and there is nothing left to restore.
/// </para>
/// <para>
/// The capture is keyed on the tree instance, so repeated refreshes of the same tree never move the user
/// back; only a different tree starts a new capture. The deferred callback reads the platform limits at
/// execution time and leaves the pending position in place when the adapter is not ready, which lets the next
/// surface pass retry.
/// </para>
/// </remarks>
/// <seealso cref="ISurfaceViewportRestoreHost"/>
/// <seealso cref="WorkflowSurfaceMath.HasViewportRestore"/>
public sealed class SurfaceViewportRestoreCore
{
    private ITreeComponent? lastTree;
    private bool hasPending;
    private bool isQueued;
    private double pendingX;
    private double pendingY;

    /// <summary>Whether a captured position is still waiting to be restored.</summary>
    public bool HasPendingRestore => hasPending;

    /// <summary>
    /// Captures <paramref name="tree"/>'s saved viewport when it differs from the last captured tree.
    /// </summary>
    /// <param name="tree">The tree the surface is showing, or <see langword="null"/> when none is bound.</param>
    public void Capture(ITreeComponent? tree)
    {
        if (tree is null || ReferenceEquals(tree, lastTree))
        {
            return;
        }

        lastTree = tree;
        hasPending = false;

        if (!WorkflowSurfaceMath.HasViewportRestore(tree.Layout))
        {
            return;
        }

        var scroll = WorkflowSurfaceMath.ViewportRestoreScroll(tree.Layout);
        pendingX = scroll.Horizontal;
        pendingY = scroll.Vertical;
        hasPending = true;
    }

    /// <summary>
    /// Schedules one restore through <paramref name="host"/> when a position is pending. Calling it again
    /// while a restore is queued does nothing.
    /// </summary>
    /// <param name="host">The adapter layer that defers and applies the restore.</param>
    /// <param name="afterRestore">Runs after a successful restore, typically to write the new viewport back.</param>
    /// <exception cref="ArgumentNullException"><paramref name="host"/> is <see langword="null"/>.</exception>
    public void Schedule(ISurfaceViewportRestoreHost host, Action? afterRestore = null)
    {
        if (host is null) throw new ArgumentNullException(nameof(host));
        if (!hasPending || isQueued)
        {
            return;
        }

        isQueued = true;
        host.DeferRestore(() =>
        {
            isQueued = false;

            if (!hasPending || !host.IsRestoreEnabled)
            {
                // 平台还没解析出滚动容器：保留待恢复位置，下一次表面刷新会再排一次。
                return;
            }

            hasPending = false;
            var maxX = host.HorizontalMaximum;
            var maxY = host.VerticalMaximum;
            host.ApplyOffset(
                WorkflowSurfaceMath.ClampValue(pendingX, 0d, maxX),
                WorkflowSurfaceMath.ClampValue(pendingY, 0d, maxY));

            afterRestore?.Invoke();
        });
    }
}
