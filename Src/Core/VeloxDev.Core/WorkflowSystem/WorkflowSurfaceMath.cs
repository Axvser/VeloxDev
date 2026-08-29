using System.Collections.Generic;
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

    /// <summary>
    /// Maximum scroll offset for a ScrollViewer: <c>max = max(0, extent − viewport)</c>.
    /// Every adapter inlined this (WPF/WinUI/Avalonia <c>Extent − Viewport</c>, MAUI with NaN
    /// guards, WinForms via AutoScrollPosition, Jalium via ScrollableWidth) with subtle
    /// platform differences — the caller still resolves its native extent/viewport.
    /// </summary>
    public static double ScrollMax(double extent, double viewport)
        => Math.Max(0, extent - viewport);

    /// <summary>
    /// Clamps a value to <c>[min, max]</c>. Replaces the repeated <c>Math.Max(min, Math.Min(v, max))</c>
    /// written by hand in every surface behavior and minimap after overscroll clamping.
    /// </summary>
    public static double ClampValue(double value, double min, double max)
        => Math.Max(min, Math.Min(value, max));

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

    /// <summary>
    /// Top edge of the visible world region in scroll space: <c>worldTop = ScrollOffsetY − ContentOffsetY</c>.
    /// Y-axis mirror of <see cref="GridWorldLeft"/> (previously every grid decorator inlined this).
    /// </summary>
    public static double GridWorldTop(double scrollOffset, double contentOffset)
        => scrollOffset - contentOffset;

    /// <summary>
    /// Converts a world y-coordinate to a screen y-coordinate inside the content area:
    /// <c>y = contentRect.Y + (worldValue − worldTop)</c>. Y-axis mirror of <see cref="GridX"/>.
    /// </summary>
    public static double GridY(double worldValue, double worldTop, double contentRectY)
        => contentRectY + (worldValue - worldTop);

    /// <summary>
    /// Grid-line snapping: the first world value aligned to the spacing grid at or below
    /// <paramref name="worldLeft"/>: <c>first = ⌊worldLeft / spacing⌋ · spacing</c>. Every grid
    /// decorator previously inlined this loop-start computation.
    /// </summary>
    public static double GridFirstLine(double worldLeft, double spacing)
        => Math.Floor(worldLeft / spacing) * spacing;

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

    /// <summary>
    /// Computes the slot anchor from a measured visual center that is already in canvas-local
    /// (world) space: <c>anchor = center</c>. Adapters whose canvas-level translation (WPF's
    /// per-node offset / WinUI &amp; MAUI's canvas <c>Translation</c>) is already inverted by the
    /// coordinate-host transform land here; <see cref="SlotAnchorFromVisualCenter"/> is the
    /// screen-space variant that still subtracts <c>ActualOffset</c>. Choosing the wrong one is
    /// a silent offset bug, so the two contracts are named explicitly.
    /// </summary>
    public static Anchor SlotAnchorFromCanvasLocal(double centerX, double centerY, int layer)
        => new(centerX, centerY, layer);

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
    /// <paramref name="minRectSize"/> pixels, capped to the minimap bounds, and the position clamped to
    /// stay inside the minimap (<c>mmWidth × mmHeight</c>). The size cap keeps the block from overflowing
    /// when the viewport is larger than the fitted content (small workflows). WPF/MAUI/Avalonia/WinUI use
    /// a minimum of 2, the WinForms demo 4.
    /// </summary>
    public static (double X, double Y, double Width, double Height) MinimapViewportRect(
        double originX, double originY, double scale,
        double vpLeft, double vpTop, double vpWidth, double vpHeight,
        double contentLeft, double contentTop,
        double mmWidth, double mmHeight, double minRectSize)
    {
        var (x, y) = MinimapLocal(vpLeft, vpTop, contentLeft, contentTop, originX, originY, scale);
        var w = Math.Min(mmWidth, Math.Max(minRectSize, vpWidth * scale));
        var h = Math.Min(mmHeight, Math.Max(minRectSize, vpHeight * scale));
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

    /// <summary>
    /// Floors a minimap thumbnail size to a minimum pixel count: <c>max(min, size · scale)</c>.
    /// Every minimap inlined this to keep tiny nodes visible (WinForms/MAUI/Avalonia/WPF/WinUI
    /// used 2, Jalium/Razor used 1).
    /// </summary>
    public static double MinThumbSize(double size, double scale, double min)
        => Math.Max(min, size * scale);
}

/// <summary>
/// Axis-aligned bounds over workflow content in world space — the union/empty semantics every
/// minimap previously carried as its own private <c>BoundsRect</c> struct (WPF/WinUI/Avalonia/MAUI).
/// Coordinates live in world space (node anchors + sizes); the minimap fits these into its draw area.
/// </summary>
public readonly struct WorkflowBounds
{
    public double Left { get; init; }
    public double Top { get; init; }
    public double Width { get; init; }
    public double Height { get; init; }

    public readonly double Right => Left + Width;
    public readonly double Bottom => Top + Height;
    public readonly bool IsEmpty => Width <= 0 || Height <= 0;

    public static WorkflowBounds FromNode(double x, double y, double w, double h)
        => new() { Left = x, Top = y, Width = w, Height = h };

    public static WorkflowBounds FromNodes(IEnumerable<(double X, double Y, double W, double H)> rects)
    {
        var bounds = default(WorkflowBounds);
        var first = true;
        foreach (var (x, y, w, h) in rects)
        {
            var nr = FromNode(x, y, w, h);
            bounds = first ? nr : Union(bounds, nr);
            first = false;
        }
        return bounds;
    }

    public static WorkflowBounds Union(WorkflowBounds a, WorkflowBounds b)
    {
        if (a.IsEmpty) return b;
        if (b.IsEmpty) return a;
        var l = Math.Min(a.Left, b.Left);
        var t = Math.Min(a.Top, b.Top);
        var r = Math.Max(a.Right, b.Right);
        var btm = Math.Max(a.Bottom, b.Bottom);
        return new WorkflowBounds { Left = l, Top = t, Width = r - l, Height = btm - t };
    }
}
