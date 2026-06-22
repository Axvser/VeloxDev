using VeloxDev.AI;
using VeloxDev.MVVM;
using VeloxDev.TransitionSystem;

namespace VeloxDev.WorkflowSystem;

[AgentContext(AgentLanguages.Chinese, "表示一个二维坐标偏移量")]
[AgentContext(AgentLanguages.English, "Represents a two-dimensional coordinate offset")]
public sealed partial class Offset(double left = 0d, double top = 0d) : ICloneable, IEquatable<Offset>, IInterpolable
{
    [VeloxProperty]
    [AgentContext(AgentLanguages.Chinese, "水平偏移量，像素单位")]
    [AgentContext(AgentLanguages.English, "Horizontal offset in pixels")]
    private double _horizontal = left;

    [VeloxProperty]
    [AgentContext(AgentLanguages.Chinese, "垂直偏移量，像素单位")]
    [AgentContext(AgentLanguages.English, "Vertical offset in pixels")]
    private double _vertical = top;

    public override bool Equals(object? obj)
    {
        if (obj is Offset other)
        {
            return Horizontal == other.Horizontal && Vertical == other.Vertical;
        }
        return false;
    }
    public override int GetHashCode() => HashCode.Combine(Horizontal, Vertical);
    public override string ToString() => $"Offset({Horizontal},{Vertical})";
    public object Clone() => new Offset(Horizontal, Vertical);
    public bool Equals(Offset? other) => other is not null && Horizontal == other.Horizontal && Vertical == other.Vertical;

    public List<object?> Interpolate(object? start, object? end, int steps, object? options = null)
    {
        if (steps <= 0) return [];

        var s1 = start as Offset ?? new Offset();
        var s2 = end as Offset ?? s1;
        if (steps == 1) return [s2];

        var deltaH = s2.Horizontal - s1.Horizontal;
        var deltaV = s2.Vertical - s1.Vertical;

        List<object?> result = new(steps);
        for (int i = 0; i < steps; i++)
        {
            var t = (double)i / (steps - 1);
            result.Add(new Offset(
                s1.Horizontal + deltaH * t,
                s1.Vertical + deltaV * t
            ));
        }
        result[0] = start;
        result[steps - 1] = end;
        return result;
    }

    public static bool operator ==(Offset left, Offset right) => left.Equals(right);
    public static bool operator !=(Offset left, Offset right) => !left.Equals(right);
    public static Offset operator +(Offset left, Offset right) => new(left.Horizontal + right.Horizontal, left.Vertical + right.Vertical);
    public static Offset operator -(Offset left, Offset right) => new(left.Horizontal - right.Horizontal, left.Vertical - right.Vertical);
}
