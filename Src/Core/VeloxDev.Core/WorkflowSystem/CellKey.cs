namespace VeloxDev.Core.WorkflowSystem;

public readonly struct CellKey(int x, int y) : IEquatable<CellKey>
{
    public readonly int X = x;
    public readonly int Y = y;

    public bool Equals(CellKey other) => X == other.X && Y == other.Y;
    public override bool Equals(object? obj) => obj is CellKey k && Equals(k);
    public override int GetHashCode() => HashCode.Combine(X, Y);
    public override string ToString() => $"CellKey({X}, {Y})";
    public static bool operator ==(CellKey left, CellKey right) => left.Equals(right);
    public static bool operator !=(CellKey left, CellKey right) => !left.Equals(right);
}
