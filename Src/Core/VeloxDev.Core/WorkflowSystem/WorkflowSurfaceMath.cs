using VeloxDev.AI;

namespace VeloxDev.WorkflowSystem;

/// <summary>
/// Pure, GUI-agnostic coordinate math shared by every workflow-surface adapter (WPF, Avalonia, WinUI,
/// MAUI, WinForms, Razor, Jalium). Each adapter previously inlined the same formulas into its surface
/// behavior / minimap overlay / grid decorator; these helpers own the single canonical copy.
///
/// All methods operate on <see cref="double"/> plus Core model types (<see cref="CanvasLayout"/>,
/// <see cref="Anchor"/>, <see cref="Offset"/>) — adapters map their native point types to doubles at the
/// call boundary, so Core carries no GUI-framework dependency.
/// </summary>
[AgentContext(AgentLanguages.Chinese, "工作流画布适配器共享的纯坐标数学：视口换算、平移越界、网格偏移、槽口锚点、小地图映射")]
[AgentContext(AgentLanguages.English, "Pure coordinate math shared by workflow-surface adapters: viewport, pan overscroll, grid, slot anchor, minimap")]
public static class WorkflowSurfaceMath
{
    // ── ① Viewport / screen↔world conversion ─────────────────────────────────

    /// <summary>
    /// Converts a screen/scroll coordinate to world space: <c>world = scroll − actualOffset</c>.
    /// The canvas is translated by <see cref="CanvasLayout.ActualOffset"/>, so world coordinates
    /// (node anchors, slot anchors) and screen coordinates differ by exactly that offset.
    /// </summary>
    public static double ToWorld(double scroll, double actualOffset)
        => scroll - actualOffset;

    /// <summary>
    /// Converts a screen point to a world-space <see cref="Anchor"/>: <c>world = screen − ActualOffset</c>.
    /// Used when writing pointer positions back to the model (e.g. <c>SetPointerCommand</c>, slot hit-testing).
    /// </summary>
    public static Anchor ToWorldAnchor(double screenX, double screenY, int layer, CanvasLayout layout)
        => new(screenX - layout.ActualOffset.Horizontal, screenY - layout.ActualOffset.Vertical, layer);

    /// <summary>
    /// Converts a world point to a screen <see cref="Offset"/>: <c>screen = world + ActualOffset</c>.
    /// </summary>
    public static Offset ToScreen(double worldX, double worldY, CanvasLayout layout)
        => new(worldX + layout.ActualOffset.Horizontal, worldY + layout.ActualOffset.Vertical);

    // ── ② Pan overscroll clamp ───────────────────────────────────────────────

    /// <summary>
    /// Clamps a desired scroll offset to <c>[0, max]</c> and, when it overshoots, expands the canvas by
    /// writing the overshoot into the layout's <see cref="CanvasLayout.NegativeOffset"/> (before origin)
    /// or <see cref="CanvasLayout.PositiveOffset"/> (past the content edge). Returns the clamped offset
    /// the caller should apply to the scroll viewer.
    ///
    /// <paramref name="max"/> is supplied by the caller because each adapter computes the scroll extent
    /// differently (WPF/WinUI/Avalonia: <c>Extent − Viewport</c>; MAUI: <c>ActualSize − ScrollViewer</c>
    /// with NaN guards; WinForms has no overscroll expansion at all and does not call this method).
    ///
    /// <paramref name="threshold"/> is a dead-band on the overshoot magnitude: the canvas only expands when
    /// the overshoot exceeds it. WPF/Avalonia/WinUI pass <c>0</c> (always expand); the MAUI minimap passes
    /// <c>0.5</c> to avoid expanding for sub-pixel jitter.
    /// </summary>
    public static double ClampScrollOffset(double desired, double max, CanvasLayout layout, bool horizontal, double threshold = 0d)
    {
        if (desired < 0)
        {
            var excess = -desired;
            if (excess > threshold)
                layout.NegativeOffset = horizontal
                    ? new Offset(layout.NegativeOffset.Horizontal + excess, layout.NegativeOffset.Vertical)
                    : new Offset(layout.NegativeOffset.Horizontal, layout.NegativeOffset.Vertical + excess);
            return 0;
        }

        if (desired > max)
        {
            var excess = desired - max;
            if (excess > threshold)
                layout.PositiveOffset = horizontal
                    ? new Offset(layout.PositiveOffset.Horizontal + excess, layout.PositiveOffset.Vertical)
                    : new Offset(layout.PositiveOffset.Horizontal, layout.PositiveOffset.Vertical + excess);
            return max;
        }

        return desired;
    }

    // ── ③ Grid offset math ───────────────────────────────────────────────────

    /// <summary>
    /// Left edge of the visible world region in scroll space: <c>worldLeft = ScrollOffsetX − ContentOffsetX</c>.
    /// The grid decorator starts drawing its vertical lines from this world x-coordinate.
    /// </summary>
    public static double GridWorldLeft(double scrollOffset, double contentOffset)
        => scrollOffset - contentOffset;

    /// <summary>
    /// Converts a world x-coordinate to a screen x-coordinate inside the content area:
    /// <c>x = contentRect.X + (worldValue − worldLeft)</c>.
    /// </summary>
    public static double GridX(double worldValue, double worldLeft, double contentRectX)
        => contentRectX + (worldValue - worldLeft);

    // ── ④ Slot anchor conversion ─────────────────────────────────────────────

    /// <summary>
    /// Computes the slot anchor from a measured visual center in screen space:
    /// <c>anchor = visualCenter − ActualOffset</c> (anchors live in world space). Layer is preserved from
    /// the slot.
    /// </summary>
    public static Anchor SlotAnchorFromVisualCenter(double centerX, double centerY, int layer, CanvasLayout layout)
        => new(centerX - layout.ActualOffset.Horizontal, centerY - layout.ActualOffset.Vertical, layer);

    /// <summary>
    /// Computes the slot anchor from a node anchor plus a local offset (used when no coordinate host is
    /// available): <c>anchor = nodeAnchor + local</c>.
    /// </summary>
    public static Anchor SlotAnchorFromNode(double nodeX, double nodeY, double localX, double localY, int layer)
        => new(nodeX + localX, nodeY + localY, layer);

    // ── ⑤ Minimap mapping ────────────────────────────────────────────────────

    /// <summary>
    /// Computes the uniform scale and top-left origin that fit the content bounds into the padded draw
    /// area, centered on the smaller axis: <c>scale = min(drawW/max(1,cw), drawH/max(1,ch))</c>,
    /// <c>origin = padding + (draw − content·scale)/2</c>. All seven adapters share this formula.
    /// </summary>
    public static (double OriginX, double OriginY, double Scale) MinimapFit(
        double contentWidth, double contentHeight,
        double drawWidth, double drawHeight, double padding)
    {
        var scale = Math.Min(drawWidth / Math.Max(1, contentWidth), drawHeight / Math.Max(1, contentHeight));
        var originX = padding + (drawWidth - contentWidth * scale) / 2;
        var originY = padding + (drawHeight - contentHeight * scale) / 2;
        return (originX, originY, scale);
    }

    /// <summary>
    /// Maps a world point onto the minimap draw area through a computed fit transform:
    /// <c>local = origin + (world − contentOrigin)·scale</c>. Used to position node thumbnails,
    /// link endpoints, and the viewport indicator in every minimap render loop.
    /// </summary>
    public static (double LocalX, double LocalY) MinimapLocal(
        double worldX, double worldY,
        double contentLeft, double contentTop,
        double originX, double originY, double scale)
        => (originX + (worldX - contentLeft) * scale, originY + (worldY - contentTop) * scale);

    /// <summary>
    /// Maps the world-space viewport rectangle onto the minimap draw area through the same fit transform
    /// the content uses (<paramref name="originX"/>/<paramref name="originY"/>/<paramref name="scale"/> —
    /// pass the values from <see cref="MinimapFit"/>), with the indicator size floored to
    /// <paramref name="minRectSize"/> pixels and the rectangle clamped to stay inside the minimap bounds
    /// (<c>mmWidth × mmHeight</c>). WPF/MAUI/Avalonia/WinUI use a minimum of 2, the WinForms demo 4.
    /// </summary>
    public static (double X, double Y, double Width, double Height) MinimapViewportRect(
        double originX, double originY, double scale,
        double vpLeft, double vpTop, double vpWidth, double vpHeight,
        double contentLeft, double contentTop,
        double mmWidth, double mmHeight, double minRectSize)
    {
        var (x, y) = MinimapLocal(vpLeft, vpTop, contentLeft, contentTop, originX, originY, scale);
        var w = Math.Max(minRectSize, vpWidth * scale);
        var h = Math.Max(minRectSize, vpHeight * scale);
        x = Math.Max(0, Math.Min(mmWidth - w, x));
        y = Math.Max(0, Math.Min(mmHeight - h, y));
        return (x, y, w, h);
    }

    /// <summary>
    /// Resolves a minimap point back to world space: <c>world = (mm − origin)/scale + contentLeft</c>.
    /// </summary>
    public static (double WorldX, double WorldY) MinimapToWorld(
        double mmX, double mmY, double originX, double originY, double scale,
        double contentLeft, double contentTop)
        => ((mmX - originX) / scale + contentLeft, (mmY - originY) / scale + contentTop);

    /// <summary>
    /// Converts a world target into a scroll offset that centers it on the viewport:
    /// <c>scroll = world − viewportSize/2 + contentOffset</c>. The caller then clamps/expands via
    /// <see cref="ClampScrollOffset"/>.
    /// </summary>
    public static (double ScrollX, double ScrollY) MinimapToScroll(
        double worldX, double worldY, double viewportWidth, double viewportHeight,
        double contentOffsetX, double contentOffsetY)
        => (worldX - viewportWidth / 2 + contentOffsetX, worldY - viewportHeight / 2 + contentOffsetY);
}
