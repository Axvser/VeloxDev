using VeloxDev.MVVM;

namespace VeloxDev.WorkflowSystem;

/// <summary>A two-dimensional coordinate offset.</summary>
public sealed partial class Offset(double left = 0d, double top = 0d) : ICloneable, IEquatable<Offset>
{
    [VeloxProperty]
    private double _horizontal = left;

    [VeloxProperty]
    private double _vertical = top;

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        if (obj is Offset other)
        {
            return Horizontal == other.Horizontal && Vertical == other.Vertical;
        }
        return false;
    }

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Horizontal, Vertical);

    /// <inheritdoc />
    public override string ToString() => $"Offset({Horizontal},{Vertical})";

    /// <inheritdoc />
    public object Clone() => new Offset(Horizontal, Vertical);

    /// <summary>Returns whether <paramref name="other"/> has the same offset.</summary>
    public bool Equals(Offset? other) => other is not null && Horizontal == other.Horizontal && Vertical == other.Vertical;

    /// <summary>Returns whether <paramref name="left"/> and <paramref name="right"/> are equal.</summary>
    public static bool operator ==(Offset left, Offset right) => left.Equals(right);

    /// <summary>Returns whether <paramref name="left"/> and <paramref name="right"/> differ.</summary>
    public static bool operator !=(Offset left, Offset right) => !left.Equals(right);

    /// <summary>Adds two offsets component-wise.</summary>
    public static Offset operator +(Offset left, Offset right) => new(left.Horizontal + right.Horizontal, left.Vertical + right.Vertical);

    /// <summary>Subtracts <paramref name="right"/> from <paramref name="left"/> component-wise.</summary>
    public static Offset operator -(Offset left, Offset right) => new(left.Horizontal - right.Horizontal, left.Vertical - right.Vertical);
}
