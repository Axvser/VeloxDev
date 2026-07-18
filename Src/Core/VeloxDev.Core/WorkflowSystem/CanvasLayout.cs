using VeloxDev.MVVM;

namespace VeloxDev.WorkflowSystem;

public sealed partial class CanvasLayout : ICloneable, IEquatable<CanvasLayout>
{
    [VeloxProperty] private Size originSize = new(1920, 1080);
    [VeloxProperty] private Offset positiveOffset = new(0, 0);
    [VeloxProperty] private Offset negativeOffset = new(0, 0);

    [VeloxProperty] private Size actualSize = new(1920, 1080);
    [VeloxProperty] private Offset actualOffset = new(0, 0);

    /// <summary>
    /// Last known viewport top-left position relative to the origin content area.
    /// Updated by the adapter whenever the visible region changes (scroll, pan, or resize).
    /// Used on deserialization to restore the user's previous viewing position.
    /// </summary>
    [VeloxProperty] private Offset viewportOffset = new(0, 0);

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
            ViewportOffset = new Offset(ViewportOffset.Horizontal, ViewportOffset.Vertical),
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
           NegativeOffset == other.NegativeOffset;

    public object Clone() => new CanvasLayout()
    {
        OriginSize = new Size(this.OriginSize.Width, this.OriginSize.Height),
        PositiveOffset = new Offset(this.PositiveOffset.Horizontal, this.PositiveOffset.Vertical),
        NegativeOffset = new Offset(this.NegativeOffset.Horizontal, this.NegativeOffset.Vertical),
        ViewportOffset = new Offset(this.ViewportOffset.Horizontal, this.ViewportOffset.Vertical),
    };

    public override bool Equals(object? obj)
    {
        if (obj is CanvasLayout layout)
        {
            return OriginSize == layout.OriginSize &&
                   PositiveOffset == layout.PositiveOffset &&
                   NegativeOffset == layout.NegativeOffset;
        }
        return false;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(OriginSize, PositiveOffset, NegativeOffset);
    }

    [VeloxCommand]
    private Task Update(object? parameter, CancellationToken ct)
    {
        Update();
        return Task.CompletedTask;
    }
    private void Update()
    {
        var baseWidth = OriginSize.Width + PositiveOffset.Horizontal + NegativeOffset.Horizontal;
        var baseHeight = OriginSize.Height + PositiveOffset.Vertical + NegativeOffset.Vertical;

        ActualSize.Width = baseWidth;
        ActualSize.Height = baseHeight;
        OnPropertyChanged(nameof(ActualSize));

        var negativeLeft = NegativeOffset.Horizontal;
        var negativeTop = NegativeOffset.Vertical;

        ActualOffset = new Offset(negativeLeft, negativeTop);
    }
    partial void OnOriginSizeChanged(Size oldValue, Size newValue) => Update();
    partial void OnPositiveOffsetChanged(Offset oldValue, Offset newValue) => Update();
    partial void OnNegativeOffsetChanged(Offset oldValue, Offset newValue) => Update();
}
