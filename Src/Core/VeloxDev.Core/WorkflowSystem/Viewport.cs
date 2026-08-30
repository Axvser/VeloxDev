using VeloxDev.TransitionSystem;
using VeloxDev.TransitionSystem.Abstractions;

namespace VeloxDev.WorkflowSystem;

/// <summary>
/// A readonly rectangle (left, top, width, height). Declares its members for animation via
/// <see cref="ISampleable"/> — as a struct it is reassembled through its constructor (member order == ctor order).
/// </summary>
public readonly struct Viewport : IEquatable<Viewport>, ISampleable
{
    private readonly double _horizontal;
    private readonly double _vertical;
    private readonly double _width;
    private readonly double _height;

    public Viewport(double left, double top, double width, double height)
    {
        _horizontal = left;
        _vertical = top;
        _width = width;
        _height = height;
    }

    public static Viewport Empty => default;

    public double Horizontal => _horizontal;
    public double Vertical => _vertical;
    public double Width => _width;
    public double Height => _height;

    public double Right => Horizontal + Width;
    public double Bottom => Vertical + Height;
    public bool IsEmpty => Width <= 0 || Height <= 0;

    public IReadOnlyList<ITransitionProperty> GetAnimatableMembers() =>
        TransitionProperty.ReadableMembers<Viewport>(v => v.Horizontal, v => v.Vertical, v => v.Width, v => v.Height);

    public object? CreateFrameValue(IReadOnlyList<object?> memberValues) =>
        new Viewport(
            (double?)memberValues[0] ?? 0d,
            (double?)memberValues[1] ?? 0d,
            (double?)memberValues[2] ?? 0d,
            (double?)memberValues[3] ?? 0d);

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
