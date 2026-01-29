using VeloxDev.Core.AOT;
using VeloxDev.Core.MVVM;

namespace VeloxDev.Core.WorkflowSystem;

[AOTReflection(Constructors: true, Methods: true, Properties: true, Fields: true)]
public sealed partial class Size(double width = double.NaN, double height = double.NaN) : ICloneable, IEquatable<Size>
{
    [VeloxProperty]
    private double _width = width;
    [VeloxProperty]
    private double _height = height;

    public override bool Equals(object? obj)
    {
        if (obj is Size size)
        {
            return Width == size.Width && Height == size.Height;
        }
        return false;
    }
    public override int GetHashCode() => HashCode.Combine(Width, Height);
    public override string ToString() => $"Size({Width},{Height})";
    public object Clone() => new Size(Width, Height);
    public bool Equals(Size? other) => other is not null && Width == other.Width && Height == other.Height;
    public static bool operator ==(Size a, Size b) => a.Equals(b);
    public static bool operator !=(Size a, Size b) => !a.Equals(b);
    public static Size operator +(Size a, Size b) => new(a.Width + b.Width, a.Height + b.Height);
    public static Size operator -(Size a, Size b) => new(a.Width - b.Width, a.Height - b.Height);
}
