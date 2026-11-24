using System;

namespace VeloxDev.WorkflowSystem;

/// <summary>
/// Core implementation of <see cref="IWorkflowSurfaceScroller"/> and <see cref="IWorkflowSurfaceNavigator"/>:
/// it converts wheel deltas into steps, writes only the axis that moved, clamps against the native limits
/// while growing the canvas on overscroll, and centers a canvas-local point with the same edge-expansion
/// semantics.
/// </summary>
/// <remarks>
/// <para>
/// This is the counterpart of the view-system composition: the state machine lives here and an adapter
/// supplies a narrow <see cref="ISurfaceScrollHost"/> over its own scroll container. The seven GUI frameworks
/// scroll differently, but the arithmetic — the sign of each axis, the zero-delta rule, the overscroll growth
/// ratio, the re-read of the maximum after a grown canvas — is the same and is written once.
/// </para>
/// <para>
/// A scroller instance reads its host on every call, so it survives the platform replacing the scroll
/// container under it. It performs no work when the host reports no offsets and no layout.
/// </para>
/// </remarks>
/// <seealso cref="ISurfaceScrollHost"/>
/// <seealso cref="WorkflowInput.Scroller"/>
/// <seealso cref="WorkflowInput.Navigator"/>
public sealed class SurfaceScrollerCore : IWorkflowSurfaceScroller, IWorkflowSurfaceNavigator
{
    /// <summary>Wheel delta a Win32-based platform reports for one physical notch.</summary>
    public const double DefaultWheelDelta = 120d;

    /// <summary>Line height a Win32-based platform uses when it scrolls by lines.</summary>
    public const double DefaultLineHeight = 16d;

    private readonly ISurfaceScrollHost host;

    /// <summary>Creates the scroller over one adapter-supplied host.</summary>
    /// <param name="host">The platform layer that supplies offsets and applies the result.</param>
    /// <exception cref="ArgumentNullException"><paramref name="host"/> is <see langword="null"/>.</exception>
    public SurfaceScrollerCore(ISurfaceScrollHost host)
        => this.host = host ?? throw new ArgumentNullException(nameof(host));

    /// <inheritdoc />
    /// <remarks>
    /// A wheel turn moves the surface by the host's <see cref="SurfaceWheelProfile"/> step. A zero delta on one
    /// axis leaves that axis alone; an overshoot past either content edge grows the canvas through
    /// <see cref="WorkflowSurfaceMath.ClampScrollOffset"/> (with
    /// <see cref="WorkflowSurfaceMath.DefaultPanExtendRatio"/>) instead of stopping at the edge.
    /// </remarks>
    public void ScrollBy(double wheelDeltaX, double wheelDeltaY)
    {
        if (wheelDeltaX == 0d && wheelDeltaY == 0d)
        {
            return;
        }

        var profile = host.WheelProfile;
        var notch = profile.SafeDeltaPerNotch;
        var lineHeight = profile.LineHeight > 0d ? profile.LineHeight : DefaultLineHeight;

        // 像素步长的平台（Avalonia / Jalium / WinUI / MAUI / Razor）按一格 = 固定像素；
        // 其余按系统行数，行数为负/零（「按页滚」）时用整幅视口。
        var stepX = profile.PixelsPerNotch > 0d
            ? profile.PixelsPerNotch
            : profile.ScrollLines > 0d ? profile.ScrollLines * lineHeight : host.ViewportWidth;
        var stepY = profile.PixelsPerNotch > 0d
            ? profile.PixelsPerNotch
            : profile.ScrollLines > 0d ? profile.ScrollLines * lineHeight : host.ViewportHeight;

        // 横轴正 = 向右，偏移同向取加；纵轴正 = 远离用户 = 视口上移，偏移取减 —— 两轴的「正」不是同一个物理方向。
        var dx = wheelDeltaX != 0d ? (wheelDeltaX / notch) * stepX : 0d;
        var dy = wheelDeltaY != 0d ? -(wheelDeltaY / notch) * stepY : 0d;

        var offsetX = host.HorizontalOffset;
        var offsetY = host.VerticalOffset;
        var desiredX = dx != 0d ? offsetX + dx : offsetX;
        var desiredY = dy != 0d ? offsetY + dy : offsetY;

        var maxX = host.HorizontalMaximum;
        var maxY = host.VerticalMaximum;
        var layout = host.Layout;

        if (layout is null)
        {
            // 没有布局就不知道画布几何，只能到边即停 —— 至少不越界。
            host.ApplyOffset(
                dx != 0d ? WorkflowSurfaceMath.ClampValue(desiredX, 0d, maxX) : null,
                dy != 0d ? WorkflowSurfaceMath.ClampValue(desiredY, 0d, maxY) : null);
            return;
        }

        // 与拖拽平移走同一条边界语义：越界经 ClampScrollOffset 撑大画布，而不是到边即停。
        var newX = dx != 0d
            ? WorkflowSurfaceMath.ClampScrollOffset(
                desiredX, maxX, layout, horizontal: true,
                extendRatio: WorkflowSurfaceMath.DefaultPanExtendRatio)
            : desiredX;
        var newY = dy != 0d
            ? WorkflowSurfaceMath.ClampScrollOffset(
                desiredY, maxY, layout, horizontal: false,
                extendRatio: WorkflowSurfaceMath.DefaultPanExtendRatio)
            : desiredY;

        if (newX != desiredX || newY != desiredY)
        {
            // 撑大过画布：extent 是懒的，先让平台采纳新范围，再按新上限夹取。
            host.ExpandCanvas();
            maxX = host.HorizontalMaximum;
            maxY = host.VerticalMaximum;
            newX = Math.Min(newX, maxX);
            newY = Math.Min(newY, maxY);
        }

        host.ApplyOffset(
            dx != 0d ? WorkflowSurfaceMath.ClampValue(newX, 0d, maxX) : null,
            dy != 0d ? WorkflowSurfaceMath.ClampValue(newY, 0d, maxY) : null);
    }

    /// <inheritdoc />
    /// <remarks>
    /// The target comes from <see cref="WorkflowSurfaceMath.CenterScroll"/>. When the canvas is too small to
    /// center the point it grows by the exact excess (ratio zero), then the target is recomputed against the
    /// grown layout so the point really lands at the center instead of at an edge.
    /// </remarks>
    public void CenterOn(double localX, double localY)
    {
        var layout = host.Layout;
        if (layout is null)
        {
            return;
        }

        var viewportWidth = host.ViewportWidth;
        var viewportHeight = host.ViewportHeight;
        var maxX = host.HorizontalMaximum;
        var maxY = host.VerticalMaximum;

        var (desiredX, desiredY) = WorkflowSurfaceMath.CenterScroll(
            localX, localY, viewportWidth, viewportHeight, layout);

        var clampedX = WorkflowSurfaceMath.ClampScrollOffset(desiredX, maxX, layout, horizontal: true);
        var clampedY = WorkflowSurfaceMath.ClampScrollOffset(desiredY, maxY, layout, horizontal: false);

        if (clampedX != desiredX || clampedY != desiredY)
        {
            // 负向增长改 ActualOffset、正向增长改 ActualSize；先让平台采纳新范围，再按新偏移重算目标。
            host.ExpandCanvas();
            maxX = host.HorizontalMaximum;
            maxY = host.VerticalMaximum;
            (desiredX, desiredY) = WorkflowSurfaceMath.CenterScroll(
                localX, localY, viewportWidth, viewportHeight, layout);
        }

        host.ApplyOffset(
            WorkflowSurfaceMath.ClampValue(desiredX, 0d, maxX),
            WorkflowSurfaceMath.ClampValue(desiredY, 0d, maxY));
    }
}
