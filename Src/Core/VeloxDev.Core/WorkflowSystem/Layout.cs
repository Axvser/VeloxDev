using VeloxDev.Core.AOT;
using VeloxDev.Core.MVVM;

namespace VeloxDev.Core.WorkflowSystem;

public enum OriginAligns
{
    TopLeft,
    TopCenter,
    TopRight,
    CenterLeft,
    Center,
    CenterRight,
    BottomLeft,
    BottomCenter,
    BottomRight
}

[AOTReflection(Constructors: true, Methods: true, Properties: true, Fields: true)]
public sealed partial class Layout : ICloneable
{
    /* Configure those properties to change the layout calculation
     *  usage
     *  (1) OriginSize - The original size of the layout
     *  (2) OriginScale - The scale factor applied to the original size
     *  (3) PositiveOffset - Add Size At Right/Bottom Side
     *  (4) NegativeOffset - Add Size At Left/Top Side
     *  (5) OriginAlign - The alignment point of the layout
     *  (6) OriginViewport - The Viewport value from UI ( such as scrollview )，Only as a logic value
     */
    [VeloxProperty] private Size originSize = new(1920, 1080);
    [VeloxProperty] private Scale originScale = new(1, 1);
    [VeloxProperty] private Offset positiveOffset = new(0, 0);
    [VeloxProperty] private Offset negativeOffset = new(0, 0);
    [VeloxProperty] private OriginAligns originAlign = OriginAligns.Center;
    [VeloxProperty] private Viewport originViewport = new();

    /* Bind those properties directly to change the effect of UI
     *  usage
     *  (1) ActualSize - Canvas.Width/Height
     *  (2) ActualOffset - TranslateTransform.X/Y
     *  (3) ActualViewport - Use this value to correctly virtualize Nodes
     */
    [VeloxProperty] private Size actualSize = new(1920, 1080);
    [VeloxProperty] private Offset actualOffset = new(0, 0);
    [VeloxProperty] private Viewport actualViewport = new();

    public object Clone() => new Layout()
    {
        OriginSize = new Size(this.OriginSize.Width, this.OriginSize.Height),
        OriginScale = new Scale(this.OriginScale.X, this.OriginScale.Y),
        PositiveOffset = new Offset(this.PositiveOffset.Left, this.PositiveOffset.Top),
        NegativeOffset = new Offset(this.NegativeOffset.Left, this.NegativeOffset.Top),
    };

    public override bool Equals(object? obj)
    {
        if (obj is Layout layout)
        {
            return OriginSize == layout.OriginSize &&
                   OriginScale == layout.OriginScale &&
                   PositiveOffset == layout.PositiveOffset &&
                   NegativeOffset == layout.NegativeOffset;
        }
        return false;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(OriginSize, OriginScale, PositiveOffset, NegativeOffset);
    }

    public override string ToString()
        => $$"""
            Layout
            {
                OriginAlign    > {{OriginAlign}}
                OriginSize     > {{OriginSize}}
                OriginScale    > {{OriginScale}}
                PositiveOffset > {{PositiveOffset}}
                NegativeOffset > {{NegativeOffset}}
                ActualSize     > {{ActualSize}}
                ActualOffset   > {{ActualOffset}}
            }
            """;

    [VeloxCommand]
    private Task Update(object? parameter, CancellationToken ct)
    {
        Update();
        return Task.CompletedTask;
    }
    private void Update()
    {
        var baseWidth = OriginSize.Width + PositiveOffset.Left + NegativeOffset.Left;
        var baseHeight = OriginSize.Height + PositiveOffset.Top + NegativeOffset.Top;
        ActualSize = new Size(
            baseWidth + (baseWidth - (baseWidth * OriginScale.X)), 
            baseHeight + (baseHeight - (baseHeight * OriginScale.Y))
            );
        ActualOffset = (OriginAlign) switch
        {
            OriginAligns.TopLeft => new Offset(NegativeOffset.Left, NegativeOffset.Top),
            OriginAligns.TopCenter => new Offset(OriginSize.Width / 2 + NegativeOffset.Left, NegativeOffset.Top),
            OriginAligns.TopRight => new Offset(OriginSize.Width + NegativeOffset.Left, NegativeOffset.Top),
            OriginAligns.CenterLeft => new Offset(NegativeOffset.Left, OriginSize.Height / 2 + NegativeOffset.Top),
            OriginAligns.Center => new Offset(OriginSize.Width / 2 + NegativeOffset.Left, OriginSize.Height / 2 + NegativeOffset.Top),
            OriginAligns.CenterRight => new Offset(OriginSize.Width + NegativeOffset.Left, OriginSize.Height / 2 + NegativeOffset.Top),
            OriginAligns.BottomLeft => new Offset(NegativeOffset.Left, OriginSize.Height + NegativeOffset.Top),
            OriginAligns.BottomCenter => new Offset(OriginSize.Width / 2 + NegativeOffset.Left, OriginSize.Height + NegativeOffset.Top),
            OriginAligns.BottomRight => new Offset(OriginSize.Width + NegativeOffset.Left, OriginSize.Height + NegativeOffset.Top),
            _ => new Offset(OriginSize.Width / 2 + NegativeOffset.Left, OriginSize.Height / 2 + NegativeOffset.Top)
        };
    }
    partial void OnOriginSizeChanged(Size oldValue, Size newValue) => Update();
    partial void OnOriginScaleChanged(Scale oldValue, Scale newValue) => Update();
    partial void OnPositiveOffsetChanged(Offset oldValue, Offset newValue) => Update();
    partial void OnNegativeOffsetChanged(Offset oldValue, Offset newValue) => Update();
    partial void OnOriginAlignChanged(OriginAligns oldValue, OriginAligns newValue) => Update();
}
