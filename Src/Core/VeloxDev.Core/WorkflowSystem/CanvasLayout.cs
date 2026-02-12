using VeloxDev.Core.AOT;
using VeloxDev.Core.MVVM;

namespace VeloxDev.Core.WorkflowSystem;

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
    [VeloxProperty] private Scale originScale = new(1, 1);
    [VeloxProperty] private Offset positiveOffset = new(0, 0);
    [VeloxProperty] private Offset negativeOffset = new(0, 0);
    [VeloxProperty] private Alignments originAlignment = Alignments.Center;

    [VeloxProperty] private Size actualSize = new(1920, 1080);
    [VeloxProperty] private Offset actualOffset = new(0, 0);

    public bool Equals(CanvasLayout? other)
        => other is not null &&
           OriginSize == other.OriginSize &&
           OriginScale == other.OriginScale &&
           PositiveOffset == other.PositiveOffset &&
           NegativeOffset == other.NegativeOffset &&
           OriginAlignment == other.OriginAlignment;

    public object Clone() => new CanvasLayout()
    {
        OriginSize = new Size(this.OriginSize.Width, this.OriginSize.Height),
        OriginScale = new Scale(this.OriginScale.X, this.OriginScale.Y),
        PositiveOffset = new Offset(this.PositiveOffset.Left, this.PositiveOffset.Top),
        NegativeOffset = new Offset(this.NegativeOffset.Left, this.NegativeOffset.Top),
        OriginAlignment = this.OriginAlignment
    };

    public override bool Equals(object? obj)
    {
        if (obj is CanvasLayout layout)
        {
            return OriginSize == layout.OriginSize &&
                   OriginScale == layout.OriginScale &&
                   PositiveOffset == layout.PositiveOffset &&
                   NegativeOffset == layout.NegativeOffset &&
                   OriginAlignment == layout.OriginAlignment;
        }
        return false;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(OriginSize, OriginScale, PositiveOffset, NegativeOffset, OriginAlignment);
    }

    public override string ToString()
        => $$"""
            CanvasLayout
            {
                OriginAlignment    > {{OriginAlignment}}
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

        ActualSize.Width = baseWidth / OriginScale.X;
        ActualSize.Height = baseHeight / OriginScale.Y;
        OnPropertyChanged(nameof(ActualSize));

        var scaledOriginWidth = OriginSize.Width * OriginScale.X;
        var scaledOriginHeight = OriginSize.Height * OriginScale.Y;

        var scaledNegativeLeft = NegativeOffset.Left * OriginScale.X;
        var scaledNegativeTop = NegativeOffset.Top * OriginScale.Y;

        ActualOffset = OriginAlignment switch
        {
            Alignments.TopLeft => new Offset(scaledNegativeLeft, scaledNegativeTop),
            Alignments.TopCenter => new Offset(
                scaledOriginWidth / 2 + scaledNegativeLeft,
                scaledNegativeTop
            ),
            Alignments.TopRight => new Offset(
                scaledOriginWidth + scaledNegativeLeft,
                scaledNegativeTop
            ),
            Alignments.CenterLeft => new Offset(
                scaledNegativeLeft,
                scaledOriginHeight / 2 + scaledNegativeTop
            ),
            Alignments.Center => new Offset(
                scaledOriginWidth / 2 + scaledNegativeLeft,
                scaledOriginHeight / 2 + scaledNegativeTop
            ),
            Alignments.CenterRight => new Offset(
                scaledOriginWidth + scaledNegativeLeft,
                scaledOriginHeight / 2 + scaledNegativeTop
            ),
            Alignments.BottomLeft => new Offset(
                scaledNegativeLeft,
                scaledOriginHeight + scaledNegativeTop
            ),
            Alignments.BottomCenter => new Offset(
                scaledOriginWidth / 2 + scaledNegativeLeft,
                scaledOriginHeight + scaledNegativeTop
            ),
            Alignments.BottomRight => new Offset(
                scaledOriginWidth + scaledNegativeLeft,
                scaledOriginHeight + scaledNegativeTop
            ),
            _ => new Offset(
                scaledOriginWidth / 2 + scaledNegativeLeft,
                scaledOriginHeight / 2 + scaledNegativeTop
            )
        };
    }
    partial void OnOriginSizeChanged(Size oldValue, Size newValue) => Update();
    partial void OnOriginScaleChanged(Scale oldValue, Scale newValue) => Update();
    partial void OnPositiveOffsetChanged(Offset oldValue, Offset newValue) => Update();
    partial void OnNegativeOffsetChanged(Offset oldValue, Offset newValue) => Update();
    partial void OnOriginAlignmentChanged(Alignments oldValue, Alignments newValue) => Update();
}
