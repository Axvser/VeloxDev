using VeloxDev.AOT;
using VeloxDev.MVVM;

namespace VeloxDev.WorkflowSystem;

public enum Alignments
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
public sealed partial class CanvasLayout : ICloneable, IEquatable<CanvasLayout>
{
    [VeloxProperty] private Size originSize = new(1920, 1080);
    [VeloxProperty] private Offset positiveOffset = new(0, 0);
    [VeloxProperty] private Offset negativeOffset = new(0, 0);
    [VeloxProperty] private Alignments originAlignment = Alignments.Center;

    [VeloxProperty] private Size actualSize = new(1920, 1080);
    [VeloxProperty] private Offset actualOffset = new(0, 0);

    public bool Equals(CanvasLayout? other)
        => other is not null &&
           OriginSize == other.OriginSize &&
           PositiveOffset == other.PositiveOffset &&
           NegativeOffset == other.NegativeOffset &&
           OriginAlignment == other.OriginAlignment;

    public object Clone() => new CanvasLayout()
    {
        OriginSize = new Size(this.OriginSize.Width, this.OriginSize.Height),
        PositiveOffset = new Offset(this.PositiveOffset.Horizontal, this.PositiveOffset.Vertical),
        NegativeOffset = new Offset(this.NegativeOffset.Horizontal, this.NegativeOffset.Vertical),
        OriginAlignment = this.OriginAlignment
    };

    public override bool Equals(object? obj)
    {
        if (obj is CanvasLayout layout)
        {
            return OriginSize == layout.OriginSize &&
                   PositiveOffset == layout.PositiveOffset &&
                   NegativeOffset == layout.NegativeOffset &&
                   OriginAlignment == layout.OriginAlignment;
        }
        return false;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(OriginSize, PositiveOffset, NegativeOffset, OriginAlignment);
    }

    public override string ToString()
        => $$"""
            CanvasLayout
            {
                OriginAlignment    > {{OriginAlignment}}
                OriginSize     > {{OriginSize}}
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
        var baseWidth = OriginSize.Width + PositiveOffset.Horizontal + NegativeOffset.Horizontal;
        var baseHeight = OriginSize.Height + PositiveOffset.Vertical + NegativeOffset.Vertical;

        ActualSize.Width = baseWidth;
        ActualSize.Height = baseHeight;
        OnPropertyChanged(nameof(ActualSize));

        var originWidth = OriginSize.Width;
        var originHeight = OriginSize.Height;

        var negativeLeft = NegativeOffset.Horizontal;
        var negativeTop = NegativeOffset.Vertical;

        ActualOffset = OriginAlignment switch
        {
            Alignments.TopLeft => new Offset(negativeLeft, negativeTop),
            Alignments.TopCenter => new Offset(
                originWidth / 2 + negativeLeft,
                negativeTop
            ),
            Alignments.TopRight => new Offset(
                originWidth + negativeLeft,
                negativeTop
            ),
            Alignments.CenterLeft => new Offset(
                negativeLeft,
                originHeight / 2 + negativeTop
            ),
            Alignments.Center => new Offset(
                originWidth / 2 + negativeLeft,
                originHeight / 2 + negativeTop
            ),
            Alignments.CenterRight => new Offset(
                originWidth + negativeLeft,
                originHeight / 2 + negativeTop
            ),
            Alignments.BottomLeft => new Offset(
                negativeLeft,
                originHeight + negativeTop
            ),
            Alignments.BottomCenter => new Offset(
                originWidth / 2 + negativeLeft,
                originHeight + negativeTop
            ),
            Alignments.BottomRight => new Offset(
                originWidth + negativeLeft,
                originHeight + negativeTop
            ),
            _ => new Offset(
                originWidth / 2 + negativeLeft,
                originHeight / 2 + negativeTop
            )
        };
    }
    partial void OnOriginSizeChanged(Size oldValue, Size newValue) => Update();
    partial void OnPositiveOffsetChanged(Offset oldValue, Offset newValue) => Update();
    partial void OnNegativeOffsetChanged(Offset oldValue, Offset newValue) => Update();
    partial void OnOriginAlignmentChanged(Alignments oldValue, Alignments newValue) => Update();
}
