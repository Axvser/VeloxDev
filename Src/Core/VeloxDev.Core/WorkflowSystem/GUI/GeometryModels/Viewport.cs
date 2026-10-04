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

    /// <summary>Creates a viewport from its left, top, width and height.</summary>
    public Viewport(double left, double top, double width, double height)
    {
        _horizontal = left;
        _vertical = top;
        _width = width;
        _height = height;
    }

    /// <summary>The empty viewport, whose width and height are zero.</summary>
    public static Viewport Empty => default;

    /// <summary>The left edge.</summary>
    public double Horizontal => _horizontal;

    /// <summary>The top edge.</summary>
    public double Vertical => _vertical;

    /// <summary>The width.</summary>
    public double Width => _width;

    /// <summary>The height.</summary>
    public double Height => _height;

    /// <summary>The right edge, <see cref="Horizontal"/> plus <see cref="Width"/>.</summary>
    public double Right => Horizontal + Width;

    /// <summary>The bottom edge, <see cref="Vertical"/> plus <see cref="Height"/>.</summary>
    public double Bottom => Vertical + Height;

    /// <summary>Whether the viewport has no area.</summary>
    public bool IsEmpty => Width <= 0 || Height <= 0;

    /// <inheritdoc />
    public IReadOnlyList<ITransitionProperty> GetAnimatableMembers() =>
        TransitionProperty.ReadableMembers<Viewport>(v => v.Horizontal, v => v.Vertical, v => v.Width, v => v.Height);

    /// <inheritdoc />
    public object? CreateFrameValue(IReadOnlyList<object?> memberValues) =>
        new Viewport(
            (double?)memberValues[0] ?? 0d,
            (double?)memberValues[1] ?? 0d,
            (double?)memberValues[2] ?? 0d,
            (double?)memberValues[3] ?? 0d);

    /// <summary>Returns whether this viewport intersects the rectangle at <paramref name="left"/>/<paramref name="top"/>.</summary>
    public bool IntersectsWith(double left, double top, double width, double height)
    {
        return left < Right &&
               left + width > Horizontal &&
               top < Bottom &&
               top + height > Vertical;
    }

    /// <summary>Returns whether this viewport intersects <paramref name="other"/>.</summary>
    public bool IntersectsWith(Viewport other) => IntersectsWith(other.Horizontal, other.Vertical, other.Width, other.Height);

    /// <summary>Returns whether the point (<paramref name="x"/>, <paramref name="y"/>) lies inside this viewport.</summary>
    public bool Contains(double x, double y) => x >= Horizontal && x < Right && y >= Vertical && y < Bottom;

    /// <summary>Returns whether <paramref name="other"/> lies entirely inside this viewport.</summary>
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

    /// <summary>Returns whether <paramref name="other"/> has the same bounds.</summary>
    public bool Equals(Viewport other) =>
        Horizontal.Equals(other.Horizontal) &&
        Vertical.Equals(other.Vertical) &&
        Width.Equals(other.Width) &&
        Height.Equals(other.Height);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is Viewport other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Horizontal, Vertical, Width, Height);

    /// <inheritdoc />
    public override string ToString() => $"Viewport({Horizontal}, {Vertical}, {Width}, {Height})";

    /// <summary>Returns whether <paramref name="left"/> and <paramref name="right"/> are equal.</summary>
    public static bool operator ==(Viewport left, Viewport right) => left.Equals(right);

    /// <summary>Returns whether <paramref name="left"/> and <paramref name="right"/> differ.</summary>
    public static bool operator !=(Viewport left, Viewport right) => !left.Equals(right);
}
