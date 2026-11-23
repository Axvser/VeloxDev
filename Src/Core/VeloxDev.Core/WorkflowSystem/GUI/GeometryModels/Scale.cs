using VeloxDev.MVVM;

namespace VeloxDev.WorkflowSystem;

/// <summary>A two-dimensional scale factor; 1.0 means no scaling.</summary>
public sealed partial class Scale(double horizontal = 1d, double vertical = 1d) : ICloneable, IEquatable<Scale>
{
    [VeloxProperty]
    private double _horizontal = horizontal;

    [VeloxProperty]
    private double _vertical = vertical;

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        if (obj is Scale other)
        {
            return Horizontal == other.Horizontal && Vertical == other.Vertical;
        }
        return false;
    }

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Horizontal, Vertical);

    /// <inheritdoc />
    public override string ToString() => $"Scale({Horizontal},{Vertical})";

    /// <inheritdoc />
    public object Clone() => new Scale(Horizontal, Vertical);

    /// <summary>Returns whether <paramref name="other"/> has the same factors.</summary>
    public bool Equals(Scale? other) => other is not null && Horizontal == other.Horizontal && Vertical == other.Vertical;

    /// <summary>Returns whether <paramref name="left"/> and <paramref name="right"/> are equal.</summary>
    public static bool operator ==(Scale left, Scale right) => left.Equals(right);

    /// <summary>Returns whether <paramref name="left"/> and <paramref name="right"/> differ.</summary>
    public static bool operator !=(Scale left, Scale right) => !left.Equals(right);

    /// <summary>Adds two scales component-wise.</summary>
    public static Scale operator +(Scale left, Scale right) => new(left.Horizontal + right.Horizontal, left.Vertical + right.Vertical);

    /// <summary>Subtracts <paramref name="right"/> from <paramref name="left"/> component-wise.</summary>
    public static Scale operator -(Scale left, Scale right) => new(left.Horizontal - right.Horizontal, left.Vertical - right.Vertical);
}
