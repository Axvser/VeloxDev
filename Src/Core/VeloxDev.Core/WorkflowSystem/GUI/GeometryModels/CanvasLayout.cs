using VeloxDev.MVVM;

namespace VeloxDev.WorkflowSystem;

public sealed partial class CanvasLayout : ICloneable, IEquatable<CanvasLayout>
{
    [VeloxProperty] private Size originSize = new(1920, 1080);
    [VeloxProperty] private Offset positiveOffset = new(0, 0);
    [VeloxProperty] private Offset negativeOffset = new(0, 0);

    [VeloxProperty] private Scale scale = new(1, 1);

    [VeloxProperty] private Size actualSize = new(1920, 1080);
    [VeloxProperty] private Offset actualOffset = new(0, 0);

    [VeloxProperty] private Offset viewportOffset = new(0, 0);

    [VeloxProperty] private ZoomCenter zoomCenter = ZoomCenter.ViewportCenter;

    [VeloxProperty] private Anchor collapsePivot = new(0, 0, 0);

    public CanvasLayout AdaptTo(
        Size targetOriginSize,
        out double suggestedViewportX,
        out double suggestedViewportY)
    {
        var adapted = new CanvasLayout
        {
            OriginSize     = new Size(targetOriginSize.Width, targetOriginSize.Height),
            PositiveOffset = new Offset(PositiveOffset.Horizontal, PositiveOffset.Vertical),
            NegativeOffset = new Offset(NegativeOffset.Horizontal, NegativeOffset.Vertical),
            Scale          = new Scale(Scale.Horizontal, Scale.Vertical),
            ViewportOffset = new Offset(ViewportOffset.Horizontal, ViewportOffset.Vertical),
            ZoomCenter     = ZoomCenter,
            CollapsePivot  = new Anchor(CollapsePivot.Horizontal, CollapsePivot.Vertical, CollapsePivot.Layer),
        };

        var newActualWidth  = targetOriginSize.Width  + PositiveOffset.Horizontal + NegativeOffset.Horizontal;
        var newActualHeight = targetOriginSize.Height + PositiveOffset.Vertical   + NegativeOffset.Vertical;

        suggestedViewportX = newActualWidth  / 2.0 - NegativeOffset.Horizontal;
        suggestedViewportY = newActualHeight / 2.0 - NegativeOffset.Vertical;

        return adapted;
    }

    public CanvasLayout AdaptTo(Size targetOriginSize)
        => AdaptTo(targetOriginSize, out _, out _);

    public bool Equals(CanvasLayout? other)
        => other is not null &&
           OriginSize == other.OriginSize &&
           PositiveOffset == other.PositiveOffset &&
           NegativeOffset == other.NegativeOffset &&
           Scale == other.Scale &&
           ZoomCenter == other.ZoomCenter;

    public object Clone() => new CanvasLayout()
    {
        OriginSize = new Size(this.OriginSize.Width, this.OriginSize.Height),
        PositiveOffset = new Offset(this.PositiveOffset.Horizontal, this.PositiveOffset.Vertical),
        NegativeOffset = new Offset(this.NegativeOffset.Horizontal, this.NegativeOffset.Vertical),
        Scale = new Scale(this.Scale.Horizontal, this.Scale.Vertical),
        ViewportOffset = new Offset(this.ViewportOffset.Horizontal, this.ViewportOffset.Vertical),
        ZoomCenter = this.ZoomCenter,
        CollapsePivot = new Anchor(this.CollapsePivot.Horizontal, this.CollapsePivot.Vertical, this.CollapsePivot.Layer),
    };

    public override bool Equals(object? obj)
    {
        if (obj is CanvasLayout layout)
        {
            return OriginSize == layout.OriginSize &&
                   PositiveOffset == layout.PositiveOffset &&
                   NegativeOffset == layout.NegativeOffset &&
                   Scale == layout.Scale &&
                   ZoomCenter == layout.ZoomCenter;
        }
        return false;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(OriginSize, PositiveOffset, NegativeOffset, Scale, ZoomCenter);
    }

    [VeloxCommand]
    private Task Update(object? parameter, CancellationToken ct)
    {
        Update();
        return Task.CompletedTask;
    }
    private void Update()
    {
        // The canvas geometry is the same in both zoom modes: nodes always collapse toward the world
        // origin (Anchor/Size getters divide by Scale), and the canvas sits at scroll −NegativeOffset
        // with the world extent plus a zoom-in auto-extend. Viewport-center zoom keeps that geometry
        // and moves the viewport instead — the collapse pivot's world point is held under the viewport
        // center purely by scrolling (WorkflowSurfaceMath.PivotCenterScroll), never by translating or
        // resizing the canvas. Zoom therefore never makes the canvas itself move.
        var baseWidth = OriginSize.Width + PositiveOffset.Horizontal + NegativeOffset.Horizontal;
        var baseHeight = OriginSize.Height + PositiveOffset.Vertical + NegativeOffset.Vertical;

        // Auto-extend on zoom-in: the node Anchor/Size getters divide by Scale, so when Scale < 1 the
        // collapsed content grows by 1/Scale beyond the world extent and would overflow the canvas.
        // Grow the scrollable size to fit (Scale > 1 collapses content toward the origin, which already
        // fits). The viewport/surface recompute from ActualSize, so the scroll range follows.
        var sx = Scale.Horizontal > 0 && Scale.Horizontal < 1 ? 1d / Scale.Horizontal : 1d;
        var sy = Scale.Vertical > 0 && Scale.Vertical < 1 ? 1d / Scale.Vertical : 1d;

        ActualSize.Width = baseWidth * sx;
        ActualSize.Height = baseHeight * sy;
        ActualOffset = new Offset(NegativeOffset.Horizontal, NegativeOffset.Vertical);

        OnPropertyChanged(nameof(ActualSize));
    }
    partial void OnOriginSizeChanged(Size oldValue, Size newValue) => Update();
    partial void OnPositiveOffsetChanged(Offset oldValue, Offset newValue) => Update();
    partial void OnNegativeOffsetChanged(Offset oldValue, Offset newValue) => Update();

    /// <summary>Scale only affects per-node view transforms, not world-space layout; re-raise ActualSize/Offset so views refresh.</summary>
    partial void OnScaleChanged(Scale oldValue, Scale newValue) => Update();

    /// <summary>CollapsePivot is written by the adapter immediately before Scale in one zoom gesture; the scale change that
    /// follows recomputes the extent, so a pivot-only change must not also resize the canvas.</summary>
    partial void OnCollapsePivotChanged(Anchor oldValue, Anchor newValue) { }
    partial void OnZoomCenterChanged(ZoomCenter oldValue, ZoomCenter newValue) { }
}
