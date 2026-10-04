namespace VeloxDev.WorkflowSystem;

/// <summary>A spatial-grid cell address: an integer column and row.</summary>
public readonly struct CellKey(int x, int y) : IEquatable<CellKey>
{
    /// <summary>The column index.</summary>
    public readonly int X = x;
    /// <summary>The row index.</summary>
    public readonly int Y = y;

    /// <summary>Returns whether <paramref name="other"/> is the same cell.</summary>
    public bool Equals(CellKey other) => X == other.X && Y == other.Y;
    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is CellKey k && Equals(k);
    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(X, Y);
    /// <inheritdoc />
    public override string ToString() => $"CellKey({X}, {Y})";
    /// <summary>Returns whether <paramref name="left"/> and <paramref name="right"/> are the same cell.</summary>
    public static bool operator ==(CellKey left, CellKey right) => left.Equals(right);
    /// <summary>Returns whether <paramref name="left"/> and <paramref name="right"/> differ.</summary>
    public static bool operator !=(CellKey left, CellKey right) => !left.Equals(right);
}
