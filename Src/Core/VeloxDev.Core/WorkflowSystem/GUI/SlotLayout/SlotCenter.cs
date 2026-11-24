namespace VeloxDev.WorkflowSystem;

/// <summary>A measured point in the coordinate space named by <see cref="ISlotPartLocator.CoordinateSpace"/>.</summary>
public readonly struct SlotCenter : IEquatable<SlotCenter>
{
    /// <summary>Creates a point from its two coordinates.</summary>
    /// <param name="x">The horizontal coordinate.</param>
    /// <param name="y">The vertical coordinate.</param>
    public SlotCenter(double x, double y)
    {
        X = x;
        Y = y;
    }

    /// <summary>The horizontal coordinate.</summary>
    public double X { get; }

    /// <summary>The vertical coordinate.</summary>
    public double Y { get; }

    /// <inheritdoc />
    public bool Equals(SlotCenter other) => X == other.X && Y == other.Y;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is SlotCenter other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(X, Y);

    /// <summary>Returns whether <paramref name="left"/> and <paramref name="right"/> are the same point.</summary>
    public static bool operator ==(SlotCenter left, SlotCenter right) => left.Equals(right);

    /// <summary>Returns whether <paramref name="left"/> and <paramref name="right"/> differ.</summary>
    public static bool operator !=(SlotCenter left, SlotCenter right) => !left.Equals(right);
}
