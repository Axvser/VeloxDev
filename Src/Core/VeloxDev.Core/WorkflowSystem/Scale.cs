using VeloxDev.Core.AOT;
using VeloxDev.Core.MVVM;

namespace VeloxDev.Core.WorkflowSystem;

[AOTReflection(Constructors: true, Methods: true, Properties: true, Fields: true)]
public partial class Scale(double x = 0d, double y = 0d) : ICloneable, IEquatable<Scale>
{
    [VeloxProperty]
    private double _x = x;
    [VeloxProperty]
    private double _y = y;

    public object Clone() => new Scale(X, Y);
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
    public bool Equals(Scale? other) => other is not null && X == other.X && Y == other.Y;

    public static bool operator ==(Scale a, Scale b) => a.Equals(b);
    public static bool operator !=(Scale a, Scale b) => !a.Equals(b);
    public static Scale operator +(Scale a, Scale b) => new(a.X + b.X, a.Y + b.Y);
    public static Scale operator -(Scale a, Scale b) => new(a.X - b.X, a.Y - b.Y);
}
