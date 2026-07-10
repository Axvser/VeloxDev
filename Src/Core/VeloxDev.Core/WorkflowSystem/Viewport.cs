namespace VeloxDev.WorkflowSystem;

public readonly struct Viewport(double left, double top, double width, double height) : IEquatable<Viewport>
{
    public static Viewport Empty => default;

    public readonly double Horizontal = left;
    public readonly double Vertical = top;
    public readonly double Width = width;
    public readonly double Height = height;

    public double Right => Horizontal + Width;
    public double Bottom => Vertical + Height;
    public bool IsEmpty => Width <= 0 || Height <= 0;

    public bool IntersectsWith(double left, double top, double width, double height)
    {
        return left < Right &&
               left + width > Horizontal &&
               top < Bottom &&
               top + height > Vertical;
    }
    public bool IntersectsWith(Viewport other) => IntersectsWith(other.Horizontal, other.Vertical, other.Width, other.Height);
    public bool Contains(double x, double y) => x >= Horizontal && x < Right && y >= Vertical && y < Bottom;
    public bool Contains(Viewport other)
    {
        return other.Horizontal >= Horizontal &&
               other.Right < Right &&
               other.Vertical >= Vertical &&
               other.Bottom < Bottom;
    }

    /// <summary>Returns the minimal viewport that covers both <paramref name="a"/> and <paramref name="b"/>.</summary>
    public static Viewport Union(Viewport a, Viewport b)
    {
        if (a.IsEmpty) return b;
        if (b.IsEmpty) return a;
        var left = Math.Min(a.Horizontal, b.Horizontal);
        var top = Math.Min(a.Vertical, b.Vertical);
        var right = Math.Max(a.Right, b.Right);
        var bottom = Math.Max(a.Bottom, b.Bottom);
        return new Viewport(left, top, right - left, bottom - top);
    }

    public bool Equals(Viewport other) =>
        Horizontal.Equals(other.Horizontal) &&
        Vertical.Equals(other.Vertical) &&
        Width.Equals(other.Width) &&
        Height.Equals(other.Height);
    public override bool Equals(object? obj) => obj is Viewport other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Horizontal, Vertical, Width, Height);
    public override string ToString() => $"Viewport({Horizontal}, {Vertical}, {Width}, {Height})";
    public static bool operator ==(Viewport left, Viewport right) => left.Equals(right);
    public static bool operator !=(Viewport left, Viewport right) => !left.Equals(right);
}
