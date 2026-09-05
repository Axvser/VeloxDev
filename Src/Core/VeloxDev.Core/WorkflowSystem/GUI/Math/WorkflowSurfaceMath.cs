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
    ///
    /// The canvas geometry is identical in both zoom modes (nodes always collapse toward the world origin;
    /// <see cref="CanvasLayout.ActualOffset"/> == NegativeOffset, extent = world + auto-extend), so the
    /// overshoot is written 1:1 in both. Viewport-center zoom never translates the canvas — it keeps the
    /// pivot under the viewport center purely by scrolling, and growing the offset by <c>excess</c> makes
    /// the exact centering scroll reachable.
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
    /// Scroll-space coordinates of the center of the currently visible viewport:
    /// <c>center = scrollOffset + viewportSize/2</c>. This is the point whose world position a
    /// viewport-center zoom keeps fixed (<see cref="LayoutPivot"/>). Clamped to the scrollable
    /// range so it stays meaningful even before the surface lays out.
    /// </summary>
    public static (double X, double Y) ScrollCenter(double scrollX, double scrollY, double viewportWidth, double viewportHeight)
        => (scrollX + viewportWidth / 2, scrollY + viewportHeight / 2);

    /// <summary>
    /// The world point currently under the center of the visible viewport, through the layout's coordinate
    /// chain. Nodes always render at canvas-local origin-collapsed <c>world/scale</c>; the surface
    /// translates by <see cref="CanvasLayout.ActualOffset"/> and the scroll offset subtracts, so the
    /// collapsed coordinate at the viewport center is <c>screenCenter − ActualOffset</c>, and inverting
    /// the collapse gives <c>world = (screenCenter − ActualOffset)·scale</c>. In viewport-center mode the
    /// adapter captures this world point, writes it as the new <see cref="CanvasLayout.CollapsePivot"/>,
    /// and scrolls to <see cref="PivotCenterScroll"/> so it stays at the viewport center. The canvas
    /// geometry is untouched by the zoom — only the scroll moves.
    /// </summary>
    public static (double WorldX, double WorldY) WorldAtViewportCenter(
        double scrollX, double scrollY, double viewportWidth, double viewportHeight,
        CanvasLayout layout)
    {
        var (cx, cy) = ScrollCenter(scrollX, scrollY, viewportWidth, viewportHeight);
        var sx = layout.Scale.Horizontal == 0 ? 1 : layout.Scale.Horizontal;
        var sy = layout.Scale.Vertical == 0 ? 1 : layout.Scale.Vertical;
        return (
            (cx - layout.ActualOffset.Horizontal) * sx,
            (cy - layout.ActualOffset.Vertical) * sy);
    }

    /// <summary>
    /// Scroll-space offset that puts the world <paramref name="pivotX"/>/<paramref name="pivotY"/> (the
    /// <see cref="CanvasLayout.CollapsePivot"/>) at the center of a viewport of the given size. Nodes
    /// appear at <c>world/scale + ActualOffset − scroll</c>, so <c>scroll = pivot/scale + ActualOffset − viewportSize/2</c>.
    /// The canvas is not translated or resized by the zoom — <see cref="CanvasLayout.ActualOffset"/>
    /// stays == NegativeOffset, so this scroll target is the only thing that moves.
    /// </summary>
    public static (double X, double Y) PivotCenterScroll(
        double pivotX, double pivotY, CanvasLayout layout,
        double viewportWidth, double viewportHeight)
    {
        var sx = layout.Scale.Horizontal == 0 ? 1 : layout.Scale.Horizontal;
        var sy = layout.Scale.Vertical == 0 ? 1 : layout.Scale.Vertical;
        return (pivotX / sx + layout.ActualOffset.Horizontal - viewportWidth / 2,
                pivotY / sy + layout.ActualOffset.Vertical - viewportHeight / 2);
    }

    /// <summary>
    /// The collapse pivot the layout currently anchors: <see cref="CanvasLayout.CollapsePivot"/> in
    /// ViewportCenter mode, otherwise the world origin. This is the world point whose screen position a
    /// viewport-center zoom keeps fixed.
    /// </summary>
    public static Anchor LayoutPivot(CanvasLayout layout)
        => layout.ZoomCenter == ZoomCenter.ViewportCenter
            ? new Anchor(layout.CollapsePivot.Horizontal, layout.CollapsePivot.Vertical, 0)
            : new Anchor(0, 0, 0);

    /// <summary>
    /// Floors a minimap thumbnail size to a minimum pixel count: <c>max(min, size · scale)</c>.
    /// Every minimap inlined this to keep tiny nodes visible (WinForms/MAUI/Avalonia/WPF/WinUI
    /// used 2, Jalium/Razor used 1).
    /// </summary>
    public static double MinThumbSize(double size, double scale, double min)
        => Math.Max(min, size * scale);

    // ── ⑥ Scale (collapse toward the world origin / viewport-center pivot) ──────

    /// <summary>
    /// Per-node scale factor used to collapse the node toward the world origin <c>(0,0)</c>: a node whose
    /// anchor is <c>(ax, ay)</c> sees the origin at local <c>(-ax, -ay)</c>, so the whole node (position and
    /// content) is scaled about that point by <c>1/scale</c>. The larger the workspace zoom, the tighter the
    /// graph clusters around the origin — the node's model <see cref="Anchor"/> and <see cref="Size"/> stay
    /// unchanged, the canvas stays the same size, and only the render transform moves.
    /// </summary>
    public static (double ScaleX, double ScaleY, double CenterX, double CenterY) ScaleCollapse(
        double anchorX, double anchorY, double scaleX, double scaleY)
        => (SafeInverse(scaleX), SafeInverse(scaleY), -anchorX, -anchorY);

    /// <summary>
    /// World-space bounds a node occupies after collapsing toward the origin:
    /// <c>(anchorX/scaleX, anchorY/scaleY, width/scaleX, height/scaleY)</c>. Mirrors the per-node
    /// <see cref="ScaleCollapse"/> render transform so minimaps (and any world-space overlay) can draw the
    /// collapsed thumbnails without touching the model.
    /// </summary>
    public static (double Left, double Top, double Width, double Height) ScaleVisualBounds(
        double anchorX, double anchorY, double width, double height, double scaleX, double scaleY)
    {
        var invX = SafeInverse(scaleX);
        var invY = SafeInverse(scaleY);
        return (anchorX * invX, anchorY * invY, width * invX, height * invY);
    }

    private static double SafeInverse(double value) => value == 0 ? 1 : 1 / value;

    // ── ⑦ Negative cover (deep-zoom reachability) ─────────────────────────────

    /// <summary>
    /// Grows the layout's negative cover so content with negative world anchors stays reachable after a
    /// zoom-in. This is the single canonical copy of a rule that WPF/WinUI/MAUI/Razor surface behaviors
    /// and the Jalium demo previously inlined as <c>EnsureNegativeCover</c>.
    ///
    /// Why it is needed: zoom-in makes <see cref="CanvasLayout.Scale"/> smaller, and every node's
    /// <see cref="IWorkflowNodeViewModel.Anchor"/> getter collapses toward the world origin (world ÷ Scale),
    /// so negative-world content folds further negative on every notch. Meanwhile the canvas translate stays
    /// pinned at <see cref="CanvasLayout.NegativeOffset"/> (ActualOffset == NegativeOffset) and the scroll
    /// floor is 0 — once <c>collapsed &lt; −NegativeOffset</c>, that content (and the link polylines drawn in the
    /// same canvas-local collapsed frame) escapes the scrollable region on the left/top and truncates with no
    /// way to scroll it back. Reachability is exactly <c>ActualOffset ≥ −min(collapsed anchor)</c>.
    ///
    /// Callers must invoke this AFTER writing the new <see cref="CanvasLayout.Scale"/> (the collapsed anchors
    /// it reads are derived from that scale) and BEFORE any later read of <see cref="CanvasLayout.ActualOffset"/>
    /// / <see cref="CanvasLayout.ActualSize"/> (extent/translate, PivotCenterScroll, clamp) so the grown cover
    /// is absorbed by the same layout pass. Growth is monotonic (never shrinks an offset the user already
    /// expanded), and the B2 guard (min ≥ 0 → no-op) keeps positive-only surfaces bit-for-bit unchanged.
    /// </summary>
    /// <returns>True if the cover grew (the caller should re-apply layout where its branch would otherwise
    /// skip a layout pass; viewport-center branches re-apply unconditionally and can ignore the value).</returns>
    public static bool EnsureNegativeCover(IWorkflowTreeViewModel? tree)
    {
        if (tree is null || tree.Nodes is null)
        {
            return false;
        }

        var layout = tree.Layout;
        double minX = double.MaxValue;
        double minY = double.MaxValue;
        foreach (var node in tree.Nodes)
        {
            // Anchor already reflects the scale just applied (collapsed = world / Scale).
            var a = node.Anchor;
            if (a.Horizontal < minX)
            {
                minX = a.Horizontal;
            }

            if (a.Vertical < minY)
            {
                minY = a.Vertical;
            }
        }

        // No content, or positive-only content: nothing to cover.
        if (minX >= 0d && minY >= 0d)
        {
            return false;
        }

        var current = layout.NegativeOffset;
        var newX = Math.Max(current.Horizontal, minX < 0d ? -minX : current.Horizontal);
        var newY = Math.Max(current.Vertical, minY < 0d ? -minY : current.Vertical);
        if (Math.Abs(newX - current.Horizontal) < 0.01d && Math.Abs(newY - current.Vertical) < 0.01d)
        {
            return false;
        }

        // The setter fires CanvasLayout.Update(), which re-raises ActualSize and ActualOffset (== NegativeOffset)
        // in the same tick — the fix never touches the canvas translate directly.
        layout.NegativeOffset = new Offset(newX, newY);
        return true;
    }
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
