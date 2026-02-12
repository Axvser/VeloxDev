namespace VeloxDev.Core.WorkflowSystem;

public readonly struct Scale(double x = 0d, double y = 0d) : IEquatable<Scale>
{
    public double X { get; } = x;
    public double Y { get; } = y;

    public override bool Equals(object? obj)
    {
        if (obj is Scale size)
        {
            return X == size.X && Y == size.Y;
        }
        return false;
    }
    public override int GetHashCode() => HashCode.Combine(X, Y);
    public override string ToString() => $"Scale({X},{Y})";
    public bool Equals(Scale other) => X == other.X && Y == other.Y;

    public static bool operator ==(Scale a, Scale b) => a.Equals(b);
    public static bool operator !=(Scale a, Scale b) => !a.Equals(b);
    public static Scale operator +(Scale a, Scale b) => new(a.X + b.X, a.Y + b.Y);
    public static Scale operator -(Scale a, Scale b) => new(a.X - b.X, a.Y - b.Y);
}
