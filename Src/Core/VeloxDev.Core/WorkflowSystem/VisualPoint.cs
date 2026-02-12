namespace VeloxDev.Core.WorkflowSystem;

public enum VisualUnit
{
    Relative,
    Absolute
}

public readonly struct VisualPoint(double left = 0d, double top = 0d, VisualUnit unit = VisualUnit.Relative, Alignments align = Alignments.Center) : IEquatable<VisualPoint>
{
    public double Left { get; } = left;
    public double Top { get; } = top;
    public VisualUnit Unit { get; } = unit;
    public Alignments Alignment { get; } = align;

    public override bool Equals(object? obj)
        => obj is VisualPoint other && Equals(other);
    public bool Equals(VisualPoint other)
        => Left == other.Left &&
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
