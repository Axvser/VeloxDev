namespace VeloxDev.Core.WorkflowSystem;

public readonly struct Viewport(double left, double top, double width, double height) : IEquatable<Viewport>
{
    public readonly double Left = left;
    public readonly double Top = top;
    public readonly double Width = width;
    public readonly double Height = height;

    public double Right => Left + Width;
    public double Bottom => Top + Height;
    public bool IsEmpty => Width <= 0 || Height <= 0;

    public bool IntersectsWith(double left, double top, double width, double height)
    {
        return left < Right &&
               left + width > Left &&
               top < Bottom &&
               top + height > Top;
    }
    public bool IntersectsWith(Viewport other) => IntersectsWith(other.Left, other.Top, other.Width, other.Height);
    public bool Contains(double x, double y) => x >= Left && x < Right && y >= Top && y < Bottom; 
    public bool Contains(Viewport other)
    {
        return other.Left >= Left &&
               other.Right < Right &&
               other.Top >= Top &&
               other.Bottom < Bottom;
    }
    public bool Equals(Viewport other) =>
        Left.Equals(other.Left) &&
        Top.Equals(other.Top) &&
        Width.Equals(other.Width) &&
        Height.Equals(other.Height);
    public override bool Equals(object? obj) => obj is Viewport other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Left, Top, Width, Height);
    public override string ToString() => $"Viewport({Left}, {Top}, {Width}, {Height})";
    public static bool operator ==(Viewport left, Viewport right) => left.Equals(right);
    public static bool operator !=(Viewport left, Viewport right) => !left.Equals(right);
}
