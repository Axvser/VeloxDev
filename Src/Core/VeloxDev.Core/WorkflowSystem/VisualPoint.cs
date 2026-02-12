using VeloxDev.Core.AOT;
using VeloxDev.Core.MVVM;

namespace VeloxDev.Core.WorkflowSystem;

public enum VisualUnit
{
    Relative,
    Absolute
}

[AOTReflection(Constructors: true, Methods: true, Properties: true, Fields: true)]
public partial class VisualPoint(double left = 0d, double top = 0d, VisualUnit unit = VisualUnit.Relative, Alignments align = Alignments.Center) : ICloneable, IEquatable<VisualPoint>
{
    [VeloxProperty] private double _left = left;
    [VeloxProperty] private double _top = top;
    [VeloxProperty] private VisualUnit _unit = unit;
    [VeloxProperty] private Alignments _alignment = align;

    public object Clone() => new VisualPoint(Left, Top, Unit, Alignment);
    public override bool Equals(object? obj)
        => obj is VisualPoint other && Equals(other);
    public bool Equals(VisualPoint? other)
        => other is not null &&
           Left == other.Left &&
           Top == other.Top &&
           Unit == other.Unit &&
           Alignment == other.Alignment;
    public override int GetHashCode()
        => HashCode.Combine(Left, Top, Unit, Alignment);
    public override string ToString()
        => $"VisualPoint({Left},{Top},{Unit},{Alignment})";
    public static bool operator ==(VisualPoint left, VisualPoint right) => left.Equals(right);
    public static bool operator !=(VisualPoint left, VisualPoint right) => !left.Equals(right);
}
