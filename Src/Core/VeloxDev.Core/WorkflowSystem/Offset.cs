using VeloxDev.Core.AOT;
using VeloxDev.Core.MVVM;

namespace VeloxDev.Core.WorkflowSystem;

[AOTReflection(Constructors: true, Methods: true, Properties: true, Fields: true)]
public sealed partial class Offset(double left = 0d, double top = 0d) : ICloneable, IEquatable<Offset>
{
    [VeloxProperty]
    private double _left = left;
    [VeloxProperty]
    private double _top = top;

    public override bool Equals(object? obj)
    {
        if (obj is Offset other)
        {
            return Left == other.Left && Top == other.Top;
        }
        return false;
    }
    public override int GetHashCode() => HashCode.Combine(Left, Top);
    public override string ToString() => $"Offset({Left},{Top})";
    public object Clone() => new Offset(Left, Top);
    public bool Equals(Offset? other) => other is not null && Left == other.Left && Top == other.Top;
    public static bool operator ==(Offset left, Offset right) => left.Equals(right);
    public static bool operator !=(Offset left, Offset right) => !left.Equals(right);
    public static Offset operator +(Offset left, Offset right) => new(left.Left + right.Left, left.Top + right.Top);
    public static Offset operator -(Offset left, Offset right) => new(left.Left - right.Left, left.Top - right.Top);
}
